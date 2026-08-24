# Redis

## Abstract

ShopFlow runs one Redis instance (`redis:7-alpine`, see [docker-compose.yml](../../docker-compose.yml)) but uses it for two structurally different jobs. Product uses it as a **cache-aside layer in front of SQL Server** — a `ProductDto` or catalog list can be recomputed from `ProductDb` at any time, so Redis is only ever a performance shortcut that's safe to lose. Cart uses it as **the entire persistence layer** — there is no `CartDb` to fall back to; a Redis Hash *is* the cart, per [Cart-Service.md](../Architecture/Cart-Service.md). Same technology, two unrelated correctness stories.

## What it is

Redis is an in-memory key-value data store. Every operation is close to O(1) and served from RAM, so it's orders of magnitude faster than a round-trip to a relational database for simple lookups — the tradeoff is that data isn't relational (no joins, no foreign keys) and typically isn't the durable system of record for anything with real business invariants. Redis supports several value types beyond plain strings; ShopFlow uses exactly two:

- **Strings** — a single serialized blob per key (Product's cache: one JSON-encoded `ProductDto` or catalog list per key).
- **Hashes** — a key that maps to its own set of field→value pairs, like a mini dictionary attached to one key (Cart: one Redis key per user, one hash field per product in that user's cart).

Both services talk to Redis through `StackExchange.Redis`'s `IConnectionMultiplexer`, registered once as a **Singleton** and turned into an `IDatabase` on demand (`_connectionMultiplexer.GetDatabase()`) — the multiplexer itself manages the actual TCP connection pool underneath, so services never open a connection per request.

## Why ShopFlow uses it

**Product: cache-aside, to protect SQL Server from repeated reads.** A product detail page or catalog listing is read far more often than it's written, and every read otherwise means a `SELECT` against `ProductDb`. Caching the *result* of that query — not the query itself — in Redis, with a short TTL, means most reads never touch SQL Server at all, while a write actively invalidates the cache so stale data has a small, bounded window in the worst case (the TTL) and no window at all on writes that go through the app.

**Cart: Redis as the system of record, not a cache in front of one.** Per [Cart-Service.md](../Architecture/Cart-Service.md), a shopping cart has no invariants worth modeling as a Domain entity — it's just "whatever's in the cart right now" for one user. Standing up a SQL Server database and an `EF Core` model for that would add a persistence layer with no correctness value over what Redis already gives for free (fast per-user reads/writes, and a TTL that expires abandoned carts automatically with zero cleanup code). There is deliberately no cache *in front of* Cart's Redis store either — `GetCartQueryHandler` reads Redis directly on every call — because Redis already is the fast path; there's nothing left to cache.

## How it's used

### Product's cache-aside pattern

**[CacheKeys.cs](../../Services/Product/Product.Application/CacheKeys.cs)** — the naming policy lives in Application (not Infrastructure), because handlers need to compute cache keys directly:

```csharp
public static class CacheKeys
{
    public const string Catalog = "product:catalog";

    public static string Product(Guid id) => $"product:{id}";
}
```

Two key shapes: one constant key for "the whole active catalog," one per-product key. `ICacheService` is the Application-owned interface (`GetAsync<T>` / `SetAsync<T>` / `RemoveAsync`), implemented by **[RedisCacheService](../../Services/Product/Product.Infrastructure/Caching/RedisCacheService.cs)** using plain Redis strings:

```csharp
public async Task<T?> GetAsync<T>(string key, CancellationToken ct)
{
    var value = await Database.StringGetAsync(key);
    return value.IsNullOrEmpty ? default : JsonSerializer.Deserialize<T>((string)value!);
}

public async Task SetAsync<T>(string key, T value, TimeSpan expiry, CancellationToken ct)
{
    var serialized = JsonSerializer.Serialize(value);
    await Database.StringSetAsync(key, serialized, expiry);
}
```

The read side, in **[GetProductByIdQueryHandler](../../Services/Product/Product.Application/Queries/GetProductByIdQueryHandler.cs)**, is the textbook cache-aside sequence — check cache, fall back to the repository, repopulate the cache:

```csharp
var cacheKey = CacheKeys.Product(query.Id);

var cached = await _cacheService.GetAsync<ProductDto>(cacheKey, ct);
if (cached is not null)
    return cached;

var product = await _productRepository.GetByIdAsync(query.Id, ct)
    ?? throw new NotFoundException(nameof(ProductEntity), query.Id);

var dto = product.ToDto();
await _cacheService.SetAsync(cacheKey, dto, CacheDuration, ct);
```

with `CacheDuration = TimeSpan.FromMinutes(10)` for a single product, and a shorter `TimeSpan.FromMinutes(5)` for the whole-catalog list in **[GetProductListQueryHandler](../../Services/Product/Product.Application/Queries/GetProductListQueryHandler.cs)** — the catalog list changes more often (any product create/update/delete touches it) so it's given a shorter safe-staleness window than a single product lookup.

The write side actively invalidates rather than waiting out the TTL. **[UpdateProductCommandHandler](../../Services/Product/Product.Application/Commands/UpdateProductCommandHandler.cs)**:

```csharp
await _productRepository.UpdateAsync(product, ct);
await _cacheService.RemoveAsync(CacheKeys.Product(product.Id), ct);
await _cacheService.RemoveAsync(CacheKeys.Catalog, ct);
```

Both the specific product key *and* the catalog key are invalidated on every update, create ([CreateProductCommandHandler](../../Services/Product/Product.Application/Commands/CreateProductCommandHandler.cs)), and delete ([DeleteProductCommandHandler](../../Services/Product/Product.Application/Commands/DeleteProductCommandHandler.cs) — a soft delete via `product.Deactivate()`) — a single new/changed/removed product invalidates the catalog cache wholesale rather than trying to patch a cached list in place.

Registration in [Product.Api/Program.cs](../../Services/Product/Product.Api/Program.cs):

```csharp
builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
    ConnectionMultiplexer.Connect(builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379"));
...
builder.Services.AddScoped<ICacheService, RedisCacheService>();
```

and the `/health` endpoint chains `.AddRedis(...)` alongside `.AddSqlServer(...)`, so a broken Redis connection shows up in health checks even though the app would still function (slower) without it.

### Cart's Hash-based storage with sliding TTL

**[CartKeys.cs](../../Services/Cart/Cart.Infrastructure/Persistence/CartKeys.cs)** — a single key shape, since Cart caches nothing else:

```csharp
public static class CartKeys
{
    public static string ForUser(Guid userId) => $"cart:{userId}";
}
```

**[RedisCartRepository](../../Services/Cart/Cart.Infrastructure/Persistence/RedisCartRepository.cs)** stores the entire cart for a user as one Redis Hash under `cart:{userId}`, with each hash field being one product id and each value the full `CartItemDto` serialized as JSON:

```csharp
private static readonly TimeSpan Ttl = TimeSpan.FromDays(7);

public async Task<IReadOnlyDictionary<Guid, CartItemDto>> GetCartAsync(Guid userId, CancellationToken ct)
{
    var key = CartKeys.ForUser(userId);
    var entries = await Database.HashGetAllAsync(key);

    if (entries.Length > 0)
    {
        await Database.KeyExpireAsync(key, Ttl);
    }

    return entries.ToDictionary(
        e => Guid.Parse((string)e.Name!),
        e => JsonSerializer.Deserialize<CartItemDto>((string)e.Value!)!);
}

public async Task UpsertItemAsync(Guid userId, CartItemDto item, CancellationToken ct)
{
    var key = CartKeys.ForUser(userId);
    await Database.HashSetAsync(key, item.ProductId.ToString(), JsonSerializer.Serialize(item));
    await Database.KeyExpireAsync(key, Ttl);
}
```

Storing the *entire* `CartItemDto` (product name and unit price included, not just quantity) as the hash value — rather than the minimal `productId → quantity` shape a cart spec might suggest — is a deliberate choice documented in [Cart-Service.md](../Architecture/Cart-Service.md): it avoids a synchronous call back to Product Service on every cart read.

**Sliding 7-day TTL**: every write (`UpsertItemAsync`) unconditionally resets the key's expiry to 7 days from now. Every *read* that finds a non-empty cart also resets it (`GetCartAsync`'s `if (entries.Length > 0)` branch), so an actively-viewed cart never expires as long as it's touched at least once a week, while a cart nobody looks at for 7 straight days disappears with no cleanup job required. `RemoveItemAsync` mirrors this — it resets the TTL only if the hash still has fields left after the delete:

```csharp
public async Task RemoveItemAsync(Guid userId, Guid productId, CancellationToken ct)
{
    var key = CartKeys.ForUser(userId);
    await Database.HashDeleteAsync(key, productId.ToString());

    if (await Database.HashLengthAsync(key) > 0)
    {
        await Database.KeyExpireAsync(key, Ttl);
    }
}
```

`ClearCartAsync` is the simplest operation — a single `KeyDeleteAsync` on the whole key, with no per-field iteration:

```csharp
public async Task ClearCartAsync(Guid userId, CancellationToken ct)
    => await Database.KeyDeleteAsync(CartKeys.ForUser(userId));
```

This is also the method the `OrderPlacedConsumer` calls directly when an order is placed — see [06-rabbitmq-masstransit.md](./06-rabbitmq-masstransit.md).

Cart's `Program.cs` registers the same Singleton `IConnectionMultiplexer` pattern as Product, but here `ICartRepository` (Scoped, `RedisCartRepository`) is Cart's *only* store — `/health` checks Redis alone, with no SQL Server check to add (`AspNetCore.HealthChecks.SqlServer` isn't even referenced in `Cart.Api`).

## Key naming and value-shape comparison

| | Product's cache | Cart's store |
| --- | --- | --- |
| Redis type | String | Hash |
| Key(s) | `product:catalog`, `product:{id}` | `cart:{userId}` |
| Value | JSON `ProductDto` / `IReadOnlyList<ProductDto>` | one JSON `CartItemDto` per hash field, field name = `productId` |
| Expiry | Fixed TTL per key type (5 or 10 minutes), no reset on read | Sliding 7-day TTL, reset on every write and every non-empty read |
| What invalidates it | Explicit `RemoveAsync` on create/update/delete, or natural TTL expiry | Never explicitly invalidated except `ClearCartAsync` (checkout) or `RemoveItemAsync` on the last item; otherwise only the sliding TTL |
| Source of truth if Redis is lost | SQL Server (`ProductDb`) — cache rebuilds itself on next read | Nothing — the cart is gone |

## Gotchas & deviations

- **Losing Redis means losing every cart in progress**, with no SQL Server fallback — an intentional tradeoff, not an oversight, per Cart's design goal of being fast and self-contained (see [Cart-Service.md](../Architecture/Cart-Service.md)).
- **Cart's TTL resets are conditional on non-empty results** (`entries.Length > 0` / `HashLengthAsync(key) > 0`) — reading or removing from an already-empty/nonexistent cart never re-arms a TTL on a key that isn't there, avoiding a wasted `KeyExpireAsync` call against a key Redis has already expired or that was never created.
- **Product's cache has no explicit versioning or stampede protection** — a cache miss under load means every concurrent request independently falls through to `IProductRepository` and recomputes the same `ProductDto`, each writing the same key back. At Product's current scale this is a non-issue, but it's worth noting as a real, unaddressed simplification rather than a hidden safeguard.
