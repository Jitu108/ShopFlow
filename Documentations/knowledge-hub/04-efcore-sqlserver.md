# EF Core 10 + SQL Server

## Abstract

Three of ShopFlow's five services — Identity, Product, and Order — persist their state in SQL Server via `Microsoft.EntityFrameworkCore.SqlServer` 10.0.0. Each service owns a single, separate database (`IdentityDb`, `ProductDb`, `OrderDb`) reachable through exactly one `AppDbContext`, and every SQL statement is reached through a repository interface declared in that service's Application layer — never directly from a handler. Cart is the deliberate exception (see [05-redis.md](./05-redis.md)): it has no relational invariants worth protecting, so it skips EF Core and SQL Server entirely.

## What it is

Entity Framework Core is Microsoft's object-relational mapper (ORM) for .NET: it maps CLR classes to relational tables, tracks in-memory changes to those objects (the "change tracker"), and translates LINQ queries into SQL. Two ideas matter most for how ShopFlow uses it:

- **`DbContext`** is the unit-of-work/session object — it owns a database connection, holds `DbSet<T>` properties (one per mapped entity/table), and batches everything queued against it into a single transaction on `SaveChangesAsync()`.
- **Code-first modeling**: instead of writing `CREATE TABLE` by hand, you write plain C# entity classes and describe their table mapping in code (`OnModelCreating`), and EF Core generates the schema from that model.

`Microsoft.EntityFrameworkCore.SqlServer` is the provider package that teaches EF Core how to talk to SQL Server specifically (T-SQL dialect, `decimal(18,2)` column types, etc.); it's referenced at the same `10.0.0` version in all three services — see [Order.Infrastructure.csproj](../../Services/Order/Order.Infrastructure/Order.Infrastructure.csproj), [Product.Infrastructure.csproj](../../Services/Product/Product.Infrastructure/Product.Infrastructure.csproj), and [Identity.Infrastructure.csproj](../../Services/Identity/Identity.Infrastructure/Identity.Infrastructure.csproj) (which additionally pulls in `Microsoft.AspNetCore.Identity.EntityFrameworkCore` for its `ApplicationUser`/role shape).

## Why ShopFlow uses it

Identity, Product, and Order all model data with **real invariants that a relational schema is good at enforcing**: a unique user email, a product that must belong to exactly one category, an order whose total is the sum of line items that must live and die with their parent. Contrast this with Cart, which has no such invariants — "the cart" is just whatever key-value state exists right now, so it uses Redis directly instead (see [05-redis.md](./05-redis.md)).

Specifically:

- **Identity** needs a unique index on email and a strongly-typed 1-to-many between a user and their refresh tokens — a relational foreign key with cascade delete is the natural fit.
- **Product** needs a product to reference a category (`Restrict` delete, so deleting a category can't silently orphan products) and to be queried/filtered efficiently by vendor — hence an index on `VendorId`.
- **Order** needs an order and its line items to be transactionally consistent — an order is never saved without its items, and deleting an order must delete its items too (`Cascade`).

Each service also keeps **one database per service** (`IdentityDb` / `ProductDb` / `OrderDb`, per the `ConnectionStrings:Default` values in each service's `appsettings.Development.json` and in [docker-compose.yml](../../docker-compose.yml)), consistent with the microservices boundary — no service reaches into another's tables.

## How it's used

### A real `DbContext`

Each service's `AppDbContext` is minimal: one or two `DbSet<T>` properties, plus a Fluent API model in `OnModelCreating`. Order's is representative of the aggregate case:

```csharp
public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<OrderEntity> Orders => Set<OrderEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<OrderEntity>(entity =>
        {
            entity.ToTable("Orders");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedNever();
            ...
```

— [Order.Infrastructure/Persistence/AppDbContext.cs](../../Services/Order/Order.Infrastructure/Persistence/AppDbContext.cs).

`ValueGeneratedNever()` appears on every entity's `Id` in all three services — primary keys are `Guid`s generated in the Domain layer's factory methods (e.g. `Id = Guid.NewGuid()` inside `OrderEntity.Create`), not database identity columns. This keeps entity creation entirely inside Domain, with no round-trip to the database needed just to learn an entity's own id.

### Fluent API entity configuration (not data annotations)

All three services configure entities via Fluent API inside `OnModelCreating` rather than `[Column]`/`[Required]` attributes on the entity classes themselves — this keeps the Domain entities free of any EF Core reference (Domain projects have zero NuGet dependencies). A representative excerpt, Product's relationship and money-column mapping:

```csharp
entity.Property(x => x.Price).HasColumnType("decimal(18,2)");
entity.Property(x => x.StockQuantity).IsRequired();
...
entity.HasIndex(x => x.VendorId);

entity.HasOne(x => x.Category)
      .WithMany(c => c.Products)
      .HasForeignKey(x => x.CategoryId)
      .OnDelete(DeleteBehavior.Restrict);
```

— [Product.Infrastructure/Persistence/AppDbContext.cs](../../Services/Product/Product.Infrastructure/Persistence/AppDbContext.cs).

Every money column across all three services (`Price`, `UnitPrice`, `TotalAmount`) is explicitly `decimal(18,2)` — EF Core's default `decimal` mapping loses precision/scale information, so this is a deliberate, repeated override, not boilerplate. Enum properties (`Status` on `OrderEntity`, `Role` on `ApplicationUser`) are stored with `.HasConversion<int>()` rather than as strings.

### The repository pattern — interfaces in Application, implementations in Infrastructure

Application never references EF Core directly. It declares a narrow repository interface, e.g.:

```csharp
public interface IProductRepository
{
    Task<ProductEntity?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<ProductEntity>> GetAllActiveAsync(CancellationToken ct);
    Task<IReadOnlyList<ProductEntity>> GetByVendorIdAsync(Guid vendorId, CancellationToken ct);
    Task AddAsync(ProductEntity product, CancellationToken ct);
    Task UpdateAsync(ProductEntity product, CancellationToken ct);
}
```

— [Product.Application/Interfaces/IProductRepository.cs](../../Services/Product/Product.Application/Interfaces/IProductRepository.cs) — and Infrastructure implements it directly against `AppDbContext`:

```csharp
public class ProductRepository : IProductRepository
{
    private readonly AppDbContext _context;

    public ProductRepository(AppDbContext context) => _context = context;

    public async Task<ProductEntity?> GetByIdAsync(Guid id, CancellationToken ct)
        => await _context.Products.SingleOrDefaultAsync(x => x.Id == id, ct);
    ...
    public async Task AddAsync(ProductEntity product, CancellationToken ct)
    {
        await _context.Products.AddAsync(product, ct);
        await _context.SaveChangesAsync(ct);
    }
}
```

— [Product.Infrastructure/Persistence/Repositories/ProductRepository.cs](../../Services/Product/Product.Infrastructure/Persistence/Repositories/ProductRepository.cs). This is the same inversion pattern documented for Cart's `ICartRepository`/`RedisCartRepository` in [Cart-Service.md](../Architecture/Cart-Service.md) — only the concrete technology differs. `AddAsync`/`UpdateAsync` each call `SaveChangesAsync` immediately (no separate unit-of-work/commit step spanning multiple repository calls); every write is its own transaction.

### Order's aggregate: `OrderEntity` → `OrderItemEntity`, owned one-to-many with cascade delete

Order is the clearest aggregate-root example in the codebase. `OrderEntity` keeps its `OrderItems` collection private and only exposes it read-only:

```csharp
private readonly List<OrderItemEntity> _orderItems = new();
public IReadOnlyList<OrderItemEntity> OrderItems => _orderItems.AsReadOnly();
```

— [OrderEntity.cs](../../Services/Order/Order.Domain/Entities/OrderEntity.cs). Items can only enter the collection through `OrderEntity.Create(...)`, which also computes `TotalAmount = items.Sum(i => i.UnitPrice * i.Quantity)` at construction time — the total is derived, never set independently, so it can't drift from its line items.

The relational mapping enforces the same one-to-many-owned-by-the-order shape at the database level:

```csharp
entity.HasMany(x => x.OrderItems)
      .WithOne()
      .HasForeignKey(oi => oi.OrderId)
      .OnDelete(DeleteBehavior.Cascade);
```

— [AppDbContext.cs](../../Services/Order/Order.Infrastructure/Persistence/AppDbContext.cs). `.WithOne()` with no navigation argument means `OrderItemEntity` has no back-reference to its parent `OrderEntity` — the relationship is one-directional, matching the read-only `OrderItems` list on the aggregate root. `OnDelete(DeleteBehavior.Cascade)` means deleting an `Orders` row deletes its `OrderItems` rows too, keeping the aggregate's lifetime consistent in the database, not just in memory. `OrderRepository.GetByIdAsync` always eager-loads the child collection (`.Include(o => o.OrderItems)`) — see [OrderRepository.cs](../../Services/Order/Order.Infrastructure/Persistence/Repositories/OrderRepository.cs) — since an `OrderEntity` without its items is a broken read of the aggregate.

Identity uses the identical shape for `ApplicationUser` → `RefreshToken` (`HasMany(...).WithOne().HasForeignKey(rt => rt.UserId).OnDelete(DeleteBehavior.Cascade)` in [Identity.Infrastructure/Persistence/AppDbContext.cs](../../Services/Identity/Identity.Infrastructure/Persistence/AppDbContext.cs)) — deleting a user cascades to their refresh tokens.

### Connection strings and schema creation

Connection strings live under `ConnectionStrings:Default` in each service's `appsettings.Development.json`, e.g. Order's:

```json
"ConnectionStrings": {
  "Default": "Server=localhost,1433;Database=OrderDb;User Id=sa;Password=YourStrong@Password123;TrustServerCertificate=True"
}
```

registered in `Program.cs` the same way in all three services:

```csharp
builder.Services.AddDbContext<AppDbContext>(opts =>
    opts.UseSqlServer(builder.Configuration.GetConnectionString("Default")));
```

Under Docker Compose, the same setting is overridden per-service via environment variables (e.g. `ConnectionStrings__Default=Server=sqlserver;Database=OrderDb;...` — see [docker-compose.yml](../../docker-compose.yml)), pointing at the shared `sqlserver` container instead of `localhost`.

**No `Migrations/` folder exists anywhere in the repository for any of the three services.** Rather than EF Core's usual code-first *migrations* workflow (`dotnet ef migrations add`, then `Database.Migrate()` at startup), all three `Program.cs` files call `Database.EnsureCreated()` once, guarded by an environment check:

```csharp
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
}
```

(identical in [Order.Api/Program.cs](../../Services/Order/Order.Api/Program.cs), [Product.Api/Program.cs](../../Services/Product/Product.Api/Program.cs), and [Identity.Api/Program.cs](../../Services/Identity/Identity.Api/Program.cs)). `EnsureCreated()` creates the schema from the current model in one shot if the database doesn't exist yet, but — unlike `Migrate()` — it has no concept of incremental schema change: if the model changes after the database already exists, `EnsureCreated()` does nothing, and there is no migration history table to reconcile against. This is a workable shortcut for a project still assembling its schema in development, but it means there is currently no supported path in this codebase for evolving a table's shape once data exists in it. Per [Documentations/Phases/Phase5.md](../Phases/Phase5.md), the `EnsureCreated()`-created schema was confirmed against a real SQL Server container for Order, including the `FK_OrderItems_Orders_OrderId ... ON DELETE CASCADE` constraint appearing exactly as configured.

## Gotchas & deviations

- **No migrations, anywhere.** Despite EF Core's code-first *migrations* being the textbook production workflow, ShopFlow uses `EnsureCreated()` in all three SQL-backed services, gated behind `IsDevelopment()`. There is no `dotnet-ef` tooling reference in any Infrastructure `.csproj`, and no `Migrations/` folder in the repository.
- **`decimal(18,2)` is repeated by hand on every money column** (`Price`, `UnitPrice`, `TotalAmount`) rather than configured once globally — a copy-paste risk if a new money column is added without remembering the same override.
- **Database-per-service, no cross-service joins.** `ProductId`/`CustomerId` fields that appear in Order's and Product's tables are plain `Guid`s with no foreign key to another service's database — consistency across services is handled by events (see [06-rabbitmq-masstransit.md](./06-rabbitmq-masstransit.md)), not by relational constraints.
