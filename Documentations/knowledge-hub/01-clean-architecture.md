# Clean Architecture

## Abstract

Every backend service in ShopFlow — Identity, Product, Cart, Order, Notification — is split into the same four .NET projects: `*.Domain`, `*.Application`, `*.Infrastructure`, `*.Api`. This is Clean Architecture: a dependency-direction rule enforced by project references, not just a folder convention. Inner layers (`Domain`, `Application`) never reference outer layers (`Infrastructure`, `Api`); outer layers depend inward. This document explains the rule, why ShopFlow adopted it, and walks a concrete interface-inversion example from the Product service, with a pointer to the one service that departs from the shape.

## What it is

Clean Architecture (also called Onion or Hexagonal Architecture in its close variants) is built on the **Dependency Inversion Principle**: source code dependencies point only inward, toward more abstract, more stable layers, regardless of the direction control flow takes at runtime. Concretely, in ShopFlow:

```text
*.Domain            entities + domain exceptions — zero project references
       ↑
*.Application       use cases (CQRS commands/queries) — depends only on Domain
       ↑
*.Infrastructure    EF Core, Redis, MassTransit — depends on Domain + Application
       ↑
*.Api               ASP.NET Core host, controllers, DI composition root — depends on Application + Infrastructure
```

The arrows point from outer to inner because that's the direction of `ProjectReference`, not the direction of a request. A request flows outer → inner → outer (`Api` → `Application` → back out through an interface to `Infrastructure`), but the *compiled dependency* — what `.csproj` a project can even `using` — only ever points up this chain. `Product.Domain`, for example, has no `PackageReference` and no `ProjectReference` at all:

```xml
<!-- Services/Product/Product.Domain/Product.Domain.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
</Project>
```

See [Product.Domain.csproj](../../Services/Product/Product.Domain/Product.Domain.csproj) — no NuGet package, no other project, nothing. It cannot know that SQL Server, Redis, or ASP.NET Core exist, because it has no way to reference them even if someone tried.

The mechanism that lets an inner layer *use* something an outer layer provides, without referencing it, is an **interface declared in the inner layer and implemented in the outer layer** — see [How it's used](#hows-it-used) below for the real example.

## Why ShopFlow uses it

1. **Testability without infrastructure.** `Product.Application.Tests` mocks `IProductRepository` with NSubstitute and never touches SQL Server; `Cart.Application.Tests` does the same for `ICartRepository` and never touches Redis. Because `Application` only knows about an interface, not `ProductRepository` or `RedisCartRepository`, a test double satisfies the contract with no database, cache, or container running. This is stated directly in [Cart-Service.md](../Architecture/Cart-Service.md): *"That inversion is what lets `Cart.Application.Tests` mock the repository with NSubstitute, and what let `Cart.Api.Tests` swap Redis for an in-memory dictionary fake without touching a single handler."*
2. **Swappable infrastructure.** The clearest proof in this codebase is Cart itself: every other service persists its aggregate in SQL Server via EF Core, but Cart persists `CartItemDto` in a Redis Hash instead ([RedisCartRepository](../../Services/Cart/Cart.Infrastructure/Persistence/RedisCartRepository.cs)) — and `Cart.Application`'s commands, handlers, and validators required zero changes to accommodate that, because they only ever call `ICartRepository`. The storage technology is entirely an `Infrastructure`-layer decision.
3. **A stable core that domain rules don't leak out of.** `ProductEntity.Create`/`Update` enforce invariants (name required, price ≥ 0, stock ≥ 0) directly in the entity — see [ProductEntity.cs](../../Services/Product/Product.Domain/Entities/ProductEntity.cs) — so those rules exist exactly once, are enforced no matter which handler or which future caller constructs a product, and can be unit-tested with no mocks at all (`Product.Domain.Tests` has no NuGet package beyond xUnit/FluentAssertions, mirroring the production project's isolation).

## How it's used

Walking `Services/Product`'s four production projects and one real inversion point end to end:

### 1. `Product.Domain` — entities and invariants

[ProductEntity.cs](../../Services/Product/Product.Domain/Entities/ProductEntity.cs) is a plain class with a private constructor and private setters — the only way to build or mutate one is through its own factory/behavior methods:

```csharp
private ProductEntity() { }

public static ProductEntity Create(Guid vendorId, string name, string description, decimal price, int stockQuantity, Guid categoryId)
{
    Validate(name, price, stockQuantity);
    var now = DateTime.UtcNow;
    return new ProductEntity { Id = Guid.NewGuid(), VendorId = vendorId, Name = name, /* ... */ CreatedAt = now, UpdatedAt = now };
}

public void DecrementStock(int quantity)
{
    if (quantity <= 0)
        throw new DomainException("Quantity to decrement must be positive.");
    StockQuantity = Math.Max(0, StockQuantity - quantity);
    UpdatedAt = DateTime.UtcNow;
}
```

No EF Core attributes, no `[Required]` data annotations, no reference to `Microsoft.EntityFrameworkCore` anywhere in this file or this project — `Product.Domain` doesn't carry a package reference to EF Core at all. Persistence mapping (how `ProductEntity` becomes rows) is entirely `Product.Infrastructure`'s concern, configured in `AppDbContext`.

### 2. `Product.Application` — the interface that inverts the dependency

[IProductRepository.cs](../../Services/Product/Product.Application/Interfaces/IProductRepository.cs), declared in `Application`, not `Infrastructure`:

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

A handler in this same layer, [CreateProductCommandHandler.cs](../../Services/Product/Product.Application/Commands/CreateProductCommandHandler.cs), depends only on this interface:

```csharp
public class CreateProductCommandHandler : IRequestHandler<CreateProductCommand, ProductDto>
{
    private readonly IProductRepository _productRepository;
    private readonly ICacheService _cacheService;
    ...
    public async Task<ProductDto> Handle(CreateProductCommand command, CancellationToken ct)
    {
        var product = ProductEntity.Create(command.VendorId, command.Name, command.Description, command.Price, command.StockQuantity, command.CategoryId);
        await _productRepository.AddAsync(product, ct);
        await _cacheService.RemoveAsync(CacheKeys.Catalog, ct);
        return product.ToDto();
    }
}
```

`Product.Application` never references `Microsoft.EntityFrameworkCore` or `StackExchange.Redis` — its `.csproj` lists only `FluentValidation`, `MediatR`, and `Microsoft.Extensions.Logging.Abstractions` as packages, plus a `ProjectReference` to `Product.Domain`. It cannot know or care that the repository will turn out to be backed by SQL Server.

### 3. `Product.Infrastructure` — the implementation

[ProductRepository.cs](../../Services/Product/Product.Infrastructure/Persistence/Repositories/ProductRepository.cs) implements the interface against EF Core:

```csharp
public class ProductRepository : IProductRepository
{
    private readonly AppDbContext _context;
    public ProductRepository(AppDbContext context) => _context = context;

    public async Task<ProductEntity?> GetByIdAsync(Guid id, CancellationToken ct)
        => await _context.Products.SingleOrDefaultAsync(x => x.Id == id, ct);

    public async Task AddAsync(ProductEntity product, CancellationToken ct)
    {
        await _context.Products.AddAsync(product, ct);
        await _context.SaveChangesAsync(ct);
    }
    ...
}
```

This project references `Product.Domain` and `Product.Application` ([Product.Infrastructure.csproj](../../Services/Product/Product.Infrastructure/Product.Infrastructure.csproj)) so it can implement `IProductRepository` and return `ProductEntity` — but nothing in `Domain` or `Application` references back to `Product.Infrastructure`. The arrow only ever points one way.

### 4. `Product.Api` — where the choice is wired

The composition happens in exactly one place, [Program.cs](../../Services/Product/Product.Api/Program.cs):

```csharp
builder.Services.AddScoped<IProductRepository, ProductRepository>();
builder.Services.AddScoped<ICategoryRepository, CategoryRepository>();
builder.Services.AddScoped<ICacheService, RedisCacheService>();
```

This is the only line in the entire codebase that mentions both `IProductRepository` and `ProductRepository` by name. Every handler upstream only ever sees `IProductRepository`. If `Product.Infrastructure` swapped SQL Server for something else, this registration is the only place that would need to change.

### Layer dependency table (same shape for Identity, Product, Order; Cart is the exception — see below)

| Project | References | Knows about |
| --- | --- | --- |
| `*.Domain` | *(none)* | Nothing outside itself |
| `*.Application` | `*.Domain` | Domain entities/exceptions; declares interfaces it needs |
| `*.Infrastructure` | `*.Domain` + `*.Application` (+ `ShopFlow.Shared` where messaging is involved) | Concrete tech: EF Core, Redis, MassTransit; implements Application's interfaces |
| `*.Api` | `*.Application` + `*.Infrastructure` | Everything — it's the composition root |

## Gotchas & deviations

- **Cart has no `Domain` entities at all.** `Cart.Domain` contains only `DomainException` and `NotFoundException` — no `Entities/` folder, no aggregate. Cart's state lives entirely in a Redis Hash (`cart:{userId}`), and the closest thing to an entity is `CartItemDto`, a plain record declared in `Cart.Application`, not `Cart.Domain`:

  ```csharp
  public record CartItemDto(Guid ProductId, string ProductName, decimal UnitPrice, int Quantity);
  ```

  See [Cart-Service.md §1](../Architecture/Cart-Service.md#1-cartdomain--exceptions-only) for the full rationale: "there's no aggregate to model... the hash itself is the persisted state, so there's no separate in-memory entity to load, mutate, and save." Even `NotFoundException` in Cart is raised with `nameof(CartItemDto)` — an Application DTO, not a Domain type — since there's no Domain type to name it after.
- Every service's `Domain` project has zero `PackageReference` entries and zero `ProjectReference` entries — this isolation is identical across Identity, Product, Order, and Cart, and is enforced structurally (nothing to `using` even by accident), not just by convention or code review.
- Deep-dive architecture docs exist per service and go far beyond this overview: [Identity-Service.md](../Architecture/Identity-Service.md), [Product-Service.md](../Architecture/Product-Service.md), [Cart-Service.md](../Architecture/Cart-Service.md), [Order-Service.md](../Architecture/Order-Service.md), [Notification-Service.md](../Architecture/Notification-Service.md).
