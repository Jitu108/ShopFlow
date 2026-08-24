---
name: dotnet-backend-conventions
description: Conventions for adding or changing backend code in any ShopFlow .NET service (Identity, Product, Order, Cart, Notification) — Clean Architecture layering, CQRS with MediatR, FluentValidation pipeline behavior, domain entity invariants, EF Core repositories, and the matching TDD test project. Use whenever writing a command/query/handler, a domain entity, a repository, or tests in a Services/*/*.{Domain,Application,Infrastructure,Api} project.
---

# .NET Backend Conventions

Every service under `Services/<Name>` follows the same four-project Clean Architecture split, seen most fully in `Services/Product`:

```
<Name>.Domain            entities, value objects, domain exceptions — no framework deps
<Name>.Application       commands/queries/handlers, validators, behaviors, DTOs, mapping, interfaces
<Name>.Infrastructure     EF Core DbContext + repositories, Redis cache, MassTransit consumers
<Name>.Api                controllers, middleware, Program.cs wiring
```

Each has a parallel `<Name>.<Layer>.Tests` project. **Every new class gets a matching test in the same run** — this is a TDD project, not "tests later."

## Domain layer

- Entities are mutable classes with **private setters** and no public constructor. Construction goes through a static `Create(...)` factory; all field validation happens in one private `Validate(...)` helper called from both `Create` and any mutating method (`Update`, etc.).
- Invalid input throws a domain-specific exception (`DomainException`), never returns a bool/null or throws a framework exception.
- State transitions are named methods (`Deactivate()`, `DecrementStock(int)`), not public setters — encode business rules in the method, not the caller.
- Reference: [ProductEntity.cs](../../../Services/Product/Product.Domain/Entities/ProductEntity.cs), tests in [ProductTests.cs](../../../Services/Product/Product.Domain.Tests/Entities/ProductTests.cs) (xUnit `[Fact]` + FluentAssertions `.Should()`).

## Application layer (CQRS via MediatR)

- One command/query = one `record` implementing `IRequest<TResponse>`, in `Commands/` or `Queries/`, e.g. `CreateProductCommand(Guid VendorId, ...) : IRequest<ProductDto>`.
- One handler class per command/query implementing `IRequestHandler<TCommand, TResponse>`, named `<Command>Handler`, in the same folder. Handlers take repository/cache interfaces via constructor injection — never `DbContext` directly.
- Validation is **not** written inside handlers. Add a `FluentValidation` `AbstractValidator<TCommand>` in `Validators/`; the generic `ValidationBehavior<TRequest,TResponse>` MediatR pipeline behavior (registered once) runs all matching validators before the handler and throws `FluentValidation.ValidationException` on failure. A command with no validator just skips validation — don't add a no-op validator.
- Cross-cutting concerns (logging, validation) are `IPipelineBehavior<,>` implementations in `Behaviors/`, not decorators or handler base classes.
- Entity → DTO mapping is a `static` extension method in `Mapping/` (`product.ToDto()`), not AutoMapper, not inline in the handler.
- A command that changes cached data must invalidate the relevant cache key itself (`await _cacheService.RemoveAsync(CacheKeys.X, ct)`) — caching is not automatic.

## Infrastructure layer

- Repositories implement an interface defined in `Application/Interfaces`, wrap `AppDbContext`, and call `SaveChangesAsync` themselves — the handler never touches the `DbContext`.
- Redis access goes through `ICacheService` (`RedisCacheService`), never `IConnectionMultiplexer` directly outside that class.
- Cross-service async communication is MassTransit `IConsumer<TEvent>` classes in `Events/`, consuming shared event contracts from `ShopFlow.Shared.Events` (e.g. `OrderPlacedEvent`) — not raw RabbitMQ client code, and not synchronous HTTP calls between services for these flows.
- Each consumer gets its own named `ReceiveEndpoint` (e.g. `"product-order-placed-queue"` — `<service>-<event>-queue`) registered in `Program.cs`'s `AddMassTransit`/`UsingRabbitMq` block, with `UseMessageRetry(r => r.Exponential(...))` on the endpoint — don't let a transient consumer failure dead-letter on the first attempt. A service that only publishes (no consumers) still calls `AddMassTransit(x => x.UsingRabbitMq(...))` to register the bus, just without `AddConsumer`/`ReceiveEndpoint`.

## API layer

- Controllers are thin: parse the request, build the command/query record, `await _mediator.Send(...)`, return the result. No business logic, no direct repository/DbContext use in a controller.
- Authorization is declarative: `[Authorize(Policy = "RequireVendor")]` / `"RequireAdmin"` on the action, policies defined once in that service's `Program.cs`. The acting user's id comes from `User.FindFirstValue("userId")`, never trust a caller-supplied vendor/user id in the request body for ownership-sensitive actions.
- Exceptions are translated to HTTP responses in one place: `ExceptionHandlingMiddleware` maps `ValidationException`→400 (field errors), `NotFoundException`→404, `DomainException`→400 (message), anything else→500 + logged. Don't add per-controller try/catch for these — throw and let the middleware handle it.

## Tests

- Domain: xUnit `[Fact]`s directly against the entity's public API, asserting both the happy path and every `DomainException` case, using FluentAssertions (`.Should().Be(...)`, `.Should().Throw<DomainException>()`).
- Application/Infrastructure: matching `.Tests` projects test handlers and repositories in isolation (mock the repository/cache interfaces for handler tests).
- When adding a command/query/entity method, add its test in the same change — don't defer it.
