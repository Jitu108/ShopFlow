# CQRS + MediatR

## Abstract

Every service's `Application` and `Api` layers use **MediatR v12** to implement **CQRS** (Command Query Responsibility Segregation): writes are `IRequest`/`IRequest<T>` *Commands*, reads are *Queries*, and each has exactly one handler. Controllers never call a handler directly — they call `IMediator.Send(...)` and MediatR resolves the matching handler, running it through two registered pipeline behaviors first. This document walks a real command and a real query from the Product service, shows the controller call site, and explains the two behaviors and their registration order.

## What it is

**CQRS** splits the model used to change state (Commands) from the model used to read it (Queries), instead of one service class with both a `Create(...)` and a `Get(...)` method sharing state and dependencies. In ShopFlow this is folder-level: every `*.Application` project has a `Commands/` folder and a `Queries/` folder, each command/query paired one-to-one with a handler file of the same name plus `Handler`.

**MediatR** is the in-process mediator that makes this practical: a controller depends on a single `IMediator` interface instead of depending on every individual handler class. `mediator.Send(request)` finds the one `IRequestHandler<TRequest, TResponse>` registered for `TRequest`'s concrete type and invokes it — a runtime dispatch, not a compile-time method call — and routes the call through any registered `IPipelineBehavior<,>` (validation, logging) first.

## Why ShopFlow uses it

1. **Decouples controllers from handler implementations.** A controller constructor takes only `IMediator`; it never `new`s up a handler or takes a handler's other dependencies (repository, cache, logger) as its own constructor parameters. Adding a new use case never touches a controller's constructor signature.
2. **Cross-cutting concerns without duplicating code in every handler.** Validation and logging are written exactly once each, as `IPipelineBehavior<TRequest, TResponse>` implementations, and apply to *every* command and query automatically because they're registered as open generics. No handler calls a validator or a logger itself.
3. **One command/query = one focused class**, each independently unit-testable with a mocked repository and no ASP.NET Core host running at all — this is the whole strategy behind `*.Application.Tests` across every service (see e.g. Cart's 23 Application-layer tests documented in [Cart-Service.md §5](../Architecture/Cart-Service.md#5-test-projects)).

## How it's used

### A real Command + Handler (write path)

[CreateProductCommand.cs](../../Services/Product/Product.Application/Commands/CreateProductCommand.cs):

```csharp
public record CreateProductCommand(
    Guid VendorId,
    string Name,
    string Description,
    decimal Price,
    int StockQuantity,
    Guid CategoryId
) : IRequest<ProductDto>;
```

[CreateProductCommandHandler.cs](../../Services/Product/Product.Application/Commands/CreateProductCommandHandler.cs):

```csharp
public class CreateProductCommandHandler : IRequestHandler<CreateProductCommand, ProductDto>
{
    private readonly IProductRepository _productRepository;
    private readonly ICacheService _cacheService;

    public async Task<ProductDto> Handle(CreateProductCommand command, CancellationToken ct)
    {
        var product = ProductEntity.Create(
            command.VendorId, command.Name, command.Description, command.Price, command.StockQuantity, command.CategoryId);

        await _productRepository.AddAsync(product, ct);
        await _cacheService.RemoveAsync(CacheKeys.Catalog, ct);

        return product.ToDto();
    }
}
```

Note the command constructs the `ProductEntity` through its Domain factory (`ProductEntity.Create`) rather than the handler building the entity's fields itself — invariant enforcement stays in `Domain` (see [01-clean-architecture.md](./01-clean-architecture.md)), while the handler's own job is orchestration: build the entity, persist it, invalidate the cache, map to a DTO.

### A real Query + Handler (read path)

[GetProductByIdQuery.cs](../../Services/Product/Product.Application/Queries/GetProductByIdQuery.cs):

```csharp
public record GetProductByIdQuery(Guid Id) : IRequest<ProductDto>;
```

[GetProductByIdQueryHandler.cs](../../Services/Product/Product.Application/Queries/GetProductByIdQueryHandler.cs):

```csharp
public class GetProductByIdQueryHandler : IRequestHandler<GetProductByIdQuery, ProductDto>
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(10);
    ...
    public async Task<ProductDto> Handle(GetProductByIdQuery query, CancellationToken ct)
    {
        var cacheKey = CacheKeys.Product(query.Id);

        var cached = await _cacheService.GetAsync<ProductDto>(cacheKey, ct);
        if (cached is not null)
            return cached;

        var product = await _productRepository.GetByIdAsync(query.Id, ct)
            ?? throw new NotFoundException(nameof(ProductEntity), query.Id);

        var dto = product.ToDto();
        await _cacheService.SetAsync(cacheKey, dto, CacheDuration, ct);

        return dto;
    }
}
```

Queries and commands both return DTOs (never the `ProductEntity` itself) via `ToDto()` extension methods in [Product.Application/Mapping](../../Services/Product/Product.Application/Mapping/) — the Domain entity never crosses back out through the `IMediator.Send` boundary.

### The controller — the only caller of `IMediator.Send`

[ProductsController.cs](../../Services/Product/Product.Api/Controllers/ProductsController.cs):

```csharp
[ApiController]
[Route("api/products")]
public class ProductsController : ControllerBase
{
    private readonly IMediator _mediator;
    public ProductsController(IMediator mediator) => _mediator = mediator;

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
        => Ok(await _mediator.Send(new GetProductByIdQuery(id), ct));

    [HttpPost]
    [Authorize(Policy = "RequireVendor")]
    public async Task<IActionResult> Create(CreateProductRequest request, CancellationToken ct)
    {
        var command = new CreateProductCommand(VendorId, request.Name, request.Description, request.Price, request.StockQuantity, request.CategoryId);
        var result = await _mediator.Send(command, ct);
        return StatusCode(StatusCodes.Status201Created, result);
    }
    ...
    private Guid VendorId => Guid.Parse(User.FindFirstValue("userId")!);
}
```

The controller's only job per action: build the request object (often merging a route/body value with a JWT claim like `VendorId`), call `_mediator.Send`, and translate the return value into an HTTP status code (`Ok`, `StatusCode(201, ...)`, `NoContent()`). No business logic, no repository or cache dependency, lives in this class.

### The two pipeline behaviors

Both live in [Product.Application/Behaviors](../../Services/Product/Product.Application/Behaviors/) and run around every `Handle` call, in registration order.

[ValidationBehavior.cs](../../Services/Product/Product.Application/Behaviors/ValidationBehavior.cs):

```csharp
public class ValidationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly IEnumerable<IValidator<TRequest>> _validators;

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        if (!_validators.Any())
            return await next();

        var context = new ValidationContext<TRequest>(request);
        var failures = _validators.Select(v => v.Validate(context)).SelectMany(r => r.Errors).Where(f => f != null).ToList();

        if (failures.Count != 0)
            throw new ValidationException(failures);

        return await next();
    }
}
```

[LoggingBehavior.cs](../../Services/Product/Product.Application/Behaviors/LoggingBehavior.cs):

```csharp
public class LoggingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        var requestName = typeof(TRequest).Name;
        _logger.LogInformation("Handling {RequestName}", requestName);
        var response = await next();
        _logger.LogInformation("Handled {RequestName}", requestName);
        return response;
    }
}
```

### Registration — MediatR and both behaviors, in [Program.cs](../../Services/Product/Product.Api/Program.cs)

```csharp
// ── MediatR ───────────────────────────────────────────────────────────────────
builder.Services.AddMediatR(cfg =>
    cfg.RegisterServicesFromAssembly(typeof(CreateProductCommand).Assembly));

// ── FluentValidation ─────────────────────────────────────────────────────────
builder.Services.AddValidatorsFromAssembly(typeof(CreateProductCommandValidator).Assembly);
builder.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
builder.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
```

`AddMediatR` scans the assembly containing `CreateProductCommand` (i.e. `Product.Application`) for every `IRequestHandler<,>` implementation and registers them automatically — no per-handler registration line anywhere in `Program.cs`. Both behaviors are registered as **open generics** (`IPipelineBehavior<,>`), so a single `AddTransient` call wires them in front of *every* command and query without per-type repetition.

**Registration order is execution order.** `ValidationBehavior` is registered first, so it runs first and wraps everything after it — including `LoggingBehavior` — meaning a request that fails validation never reaches `LoggingBehavior`'s "Handling ..." log line at all; the `ValidationException` is thrown from inside `ValidationBehavior` before `next()` (which would invoke `LoggingBehavior`) is ever called. This exact order — `ValidationBehavior<,>` then `LoggingBehavior<,>` — is identical across Product, Cart ([confirmed in Cart-Service.md §2](../Architecture/Cart-Service.md#2-cartapplication--use-cases-cqrs)), and the other services.

## Gotchas & deviations

- Some commands/queries have **no registered validator at all** — e.g. [DeleteProductCommand](../../Services/Product/Product.Application/Commands/DeleteProductCommand.cs) and `GetProductByIdQuery`. `ValidationBehavior`'s `if (!_validators.Any()) return await next();` guard means these simply skip straight through with no validation step — see [03-fluentvalidation.md](./03-fluentvalidation.md) for the full mechanics of that short-circuit.
- Commands that return nothing use plain `IRequest` (no generic parameter, implicitly `IRequest<Unit>` under the hood via MediatR) — e.g. `DeleteProductCommand : IRequest` and Cart's `RemoveCartItemCommand : IRequest` — while commands and queries returning a value use `IRequest<TResponse>`. Both shapes flow through the same two behaviors, since `ValidationBehavior<TRequest, TResponse>` and `LoggingBehavior<TRequest, TResponse>` are generic over the response type too.
- Handlers call the repository/cache *interfaces* declared in `Application`, never a concrete `Infrastructure` type — see [01-clean-architecture.md](./01-clean-architecture.md) for why that boundary exists and how it's wired.
