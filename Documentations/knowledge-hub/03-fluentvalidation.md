# FluentValidation

## Abstract

Every service validates its MediatR Commands and Queries with **FluentValidation v11** (`FluentValidation.DependencyInjectionExtensions` in the `*.Api` project), wired in as the `ValidationBehavior<TRequest, TResponse>` MediatR pipeline behavior documented in [02-cqrs-mediatr.md](./02-cqrs-mediatr.md). This document covers what FluentValidation is, why ShopFlow chose it over data-annotation attributes, and the exact mechanics of how a validation failure turns into an HTTP 400 — including the short-circuit path for request types that have no validator at all.

## What it is

FluentValidation is a code-based validation library: rules are written as C# expressions in a dedicated `AbstractValidator<T>` subclass — `RuleFor(x => x.Property).NotEmpty().WithMessage(...)` — rather than as `[Required]`/`[Range]` attributes decorating the model itself. A validator is a plain class, resolved from DI like any other service, and can be unit-tested by instantiating it directly and calling `.Validate(...)` with no ASP.NET Core model-binding pipeline involved.

## Why ShopFlow uses it

1. **Keeps validation out of Domain and DTOs.** Cart's `CartItemDto` is a bare `record` with no attributes at all: `public record CartItemDto(Guid ProductId, string ProductName, decimal UnitPrice, int Quantity);`. The rules that would otherwise decorate it — product name required, ≤ 200 chars, price ≥ 0, quantity ≥ 1 — live entirely in [AddCartItemCommandValidator.cs](../../Services/Cart/Cart.Application/Validators/AddCartItemCommandValidator.cs) instead. Per [Cart-Service.md §1](../Architecture/Cart-Service.md#1-cartdomain--exceptions-only): "there are no invariants to protect at this level... quantity/price/name validation lives in FluentValidation at the Application boundary instead."
2. **Composable and independently testable.** A validator class has no dependency on `HttpContext`, MVC model binding, or MediatR — it's tested by constructing it and calling `Validate()`/`TestValidate()` directly, exactly as ShopFlow's `*.Application.Tests` projects do (e.g. Cart's validator tests, part of its 23 `Cart.Application.Tests`).
3. **One rule set per request shape**, not one attribute set per DTO shared across multiple use cases — `AddCartItemCommandValidator` and `UpdateCartItemCommandValidator` both validate a `Quantity`, but with deliberately different rules (see [Gotchas](#gotchas--deviations)), which attribute-based validation on a single shared model couldn't easily express.

## How it's used

### Wiring: registration in `Program.cs`

[Product.Api/Program.cs](../../Services/Product/Product.Api/Program.cs):

```csharp
builder.Services.AddValidatorsFromAssembly(typeof(CreateProductCommandValidator).Assembly);
builder.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
builder.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
```

`AddValidatorsFromAssembly` (from the `FluentValidation.DependencyInjectionExtensions` package, referenced only in the `*.Api` project — see [Product.API.csproj](../../Services/Product/Product.Api/Product.API.csproj)) scans `Product.Application` and registers every `AbstractValidator<T>` it finds as `IValidator<T>` in DI. No individual validator is registered by hand.

### The pipeline behavior that actually runs validation

[ValidationBehavior.cs](../../Services/Product/Product.Application/Behaviors/ValidationBehavior.cs) (byte-for-byte identical across Product and Cart):

```csharp
public class ValidationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly IEnumerable<IValidator<TRequest>> _validators;

    public ValidationBehavior(IEnumerable<IValidator<TRequest>> validators)
    {
        _validators = validators;
    }

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        if (!_validators.Any())
            return await next();

        var context = new ValidationContext<TRequest>(request);
        var failures = _validators
            .Select(v => v.Validate(context))
            .SelectMany(r => r.Errors)
            .Where(f => f != null)
            .ToList();

        if (failures.Count != 0)
            throw new ValidationException(failures);

        return await next();
    }
}
```

Two mechanics worth calling out explicitly:

- **The short-circuit.** `IEnumerable<IValidator<TRequest>>` is injected, not a single `IValidator<TRequest>` — DI happily resolves an *empty* enumerable when no validator is registered for that exact `TRequest`. `if (!_validators.Any()) return await next();` is what lets [GetProductByIdQuery](../../Services/Product/Product.Application/Queries/GetProductByIdQuery.cs) and [DeleteProductCommand](../../Services/Product/Product.Application/Commands/DeleteProductCommand.cs) — neither of which has a validator class — pass straight through to the handler with zero validation overhead. Cart documents the same behavior explicitly for `RemoveCartItemCommand`, `ClearCartCommand`, and `GetCartQuery` (see [Cart-Service.md §2](../Architecture/Cart-Service.md#2-cartapplication--use-cases-cqrs)): "No validator exists for `RemoveCartItemCommand`, `ClearCartCommand`, or `GetCartQuery` — `ValidationBehavior` short-circuits to `next()` when no validators are registered for a request type."
- **Aggregation across multiple validators.** `_validators.Select(v => v.Validate(context)).SelectMany(r => r.Errors)` runs *every* `IValidator<TRequest>` registered for that type (in practice there's normally exactly one per request type in this codebase) and collects every failure from all of them into a single list before throwing — a request fails all-at-once with every broken rule reported together, not fast-fail on the first validator.

### Two real validator classes

[AddCartItemCommandValidator.cs](../../Services/Cart/Cart.Application/Validators/AddCartItemCommandValidator.cs):

```csharp
public class AddCartItemCommandValidator : AbstractValidator<AddCartItemCommand>
{
    public AddCartItemCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty().WithMessage("UserId is required.");
        RuleFor(x => x.ProductId).NotEmpty().WithMessage("ProductId is required.");
        RuleFor(x => x.ProductName)
            .NotEmpty().WithMessage("ProductName is required.")
            .MaximumLength(200).WithMessage("ProductName must not exceed 200 characters.");
        RuleFor(x => x.UnitPrice).GreaterThanOrEqualTo(0).WithMessage("UnitPrice cannot be negative.");
        RuleFor(x => x.Quantity).GreaterThanOrEqualTo(1).WithMessage("Quantity must be at least 1.");
    }
}
```

[UpdateCartItemCommandValidator.cs](../../Services/Cart/Cart.Application/Validators/UpdateCartItemCommandValidator.cs) — same shape, one rule, deliberately different message:

```csharp
public class UpdateCartItemCommandValidator : AbstractValidator<UpdateCartItemCommand>
{
    public UpdateCartItemCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty().WithMessage("UserId is required.");
        RuleFor(x => x.ProductId).NotEmpty().WithMessage("ProductId is required.");
        RuleFor(x => x.Quantity)
            .GreaterThanOrEqualTo(1)
            .WithMessage("Quantity must be at least 1. To remove an item, use the delete endpoint instead.");
    }
}
```

And [CreateProductCommandValidator.cs](../../Services/Product/Product.Application/Validators/CreateProductCommandValidator.cs) from Product, showing the same fluent chain style applied to a different aggregate:

```csharp
public class CreateProductCommandValidator : AbstractValidator<CreateProductCommand>
{
    public CreateProductCommandValidator()
    {
        RuleFor(x => x.VendorId).NotEmpty().WithMessage("VendorId is required.");
        RuleFor(x => x.Name).NotEmpty().WithMessage("Name is required.").MaximumLength(200).WithMessage("Name must not exceed 200 characters.");
        RuleFor(x => x.Price).GreaterThanOrEqualTo(0).WithMessage("Price cannot be negative.");
        RuleFor(x => x.StockQuantity).GreaterThanOrEqualTo(0).WithMessage("Stock quantity cannot be negative.");
        RuleFor(x => x.CategoryId).NotEmpty().WithMessage("CategoryId is required.");
    }
}
```

### From thrown `ValidationException` to HTTP 400

`ValidationBehavior` throws `FluentValidation.ValidationException`, which propagates up through the (un-caught) handler call and MediatR's `Send`, past the controller action, until ASP.NET Core's middleware pipeline reaches [ExceptionHandlingMiddleware.cs](../../Services/Product/Product.Api/Middleware/ExceptionHandlingMiddleware.cs) — registered *before* Swagger/auth in `Program.cs` so it wraps the whole request:

```csharp
public async Task InvokeAsync(HttpContext context)
{
    try
    {
        await _next(context);
    }
    catch (ValidationException ex)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        context.Response.ContentType = "application/json";
        var errors = ex.Errors.Select(e => new { e.PropertyName, e.ErrorMessage });
        await context.Response.WriteAsJsonAsync(new { errors });
    }
    catch (NotFoundException ex) { /* 404 */ }
    catch (DomainException ex) { /* 400 */ }
    catch (Exception ex) { /* 500 */ }
}
```

`ex.Errors` is FluentValidation's own `ValidationFailure` collection — the exact aggregated list `ValidationBehavior` built from every failing rule — projected into `{ propertyName, errorMessage }` pairs and serialized as `{ "errors": [...] }`. The catch order matters: `ValidationException` and `NotFoundException` are caught ahead of the `DomainException` base-class catch and the final catch-all `Exception`, so a validation failure never falls through to the generic 500 handler. This exact catch order and body shape is identical across Identity, Product, and Cart's middleware.

## Gotchas & deviations

- **Not every request type has a validator**, and that's an intentional, documented state rather than an oversight — see the short-circuit explanation above. Before adding a new command/query, check whether a validator already exists in that service's `Validators/` folder ([Product.Application/Validators](../../Services/Product/Product.Application/Validators/), [Cart.Application/Validators](../../Services/Cart/Cart.Application/Validators/), [Order.Application/Validators](../../Services/Order/Order.Application/Validators/)) — its absence is not itself a bug.
- Validators are constructor-parameterless in every example read here (`public AddCartItemCommandValidator() { ... }`) — none of them takes an injected dependency (e.g. a repository for async/remote uniqueness checks). All rules validate shape/range of the request's own fields only; nothing here does an async `MustAsync` database check.
- `WithMessage` is applied consistently across every validator surveyed — no validator in Product or Cart relies on FluentValidation's default English messages, so every failure the client sees is an explicit, hand-written string.
- Package versions are pinned consistently: `FluentValidation` `11.11.0` in every `*.Application.csproj` (e.g. [Product.Application.csproj](../../Services/Product/Product.Application/Product.Application.csproj)), and `FluentValidation.DependencyInjectionExtensions` `11.11.0` in every `*.Api.csproj` (e.g. [Product.API.csproj](../../Services/Product/Product.Api/Product.API.csproj)) — the DI extension package is deliberately kept out of `Application`, since assembly scanning/registration is a composition-root concern that belongs in `Api`.
