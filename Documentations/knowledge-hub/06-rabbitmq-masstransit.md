# RabbitMQ + MassTransit

## Abstract

ShopFlow uses RabbitMQ as its message broker and MassTransit 8 as the .NET abstraction over it, wiring Order Service to Cart and Notification without either of the latter two ever being called synchronously. Order publishes one event, `OrderPlacedEvent`; Cart and Notification each independently consume it from their own queue. Every MassTransit package in the solution is pinned to **`8.5.10`**, not the newer `9.x` line — a real, documented deviation caused by a licensing change, not a stylistic choice.

## What it is

**RabbitMQ** is a message broker: producers publish messages to an *exchange*, the broker routes them into *queues* based on bindings, and independent *consumers* read from those queues at their own pace. Producer and consumer never talk to each other directly or need to be online at the same time — the broker durably holds messages in between.

**MassTransit** is a .NET library that sits on top of a broker (RabbitMQ here) and gives application code a much higher-level API than raw AMQP: `IPublishEndpoint.Publish<T>()` to send a strongly-typed message, `IConsumer<T>.Consume(ConsumeContext<T>)` to receive one, declarative retry policies, and — critically for tests — an in-memory `AddMassTransitTestHarness` that behaves like a real bus without touching a socket.

## Why ShopFlow uses it

Order placement needs to trigger two side effects in other services — clear the customer's cart, and send them a confirmation email — but neither of those effects should block the HTTP response to "place order," and Order Service has no business knowing how Cart or Notification implement their side. A synchronous HTTP call from Order to each of those services would couple Order's availability to theirs (if Notification's SMTP relay is slow, placing an order would be slow too) and would require Order to know both services' addresses and contracts directly.

Publishing one `OrderPlacedEvent` and letting Cart and Notification consume it independently decouples all three: Order doesn't know or care who's listening, each consumer processes the event on its own schedule with its own retry policy, and adding a *third* future consumer of order-placement (e.g., analytics) requires zero changes to Order Service.

## How it's used

### The event contract — `ShopFlow.Shared`

Event contracts live in one shared library referenced by every service's Infrastructure layer, so the wire shape is defined exactly once:

```csharp
public record OrderPlacedEvent(
    Guid OrderId,
    Guid CustomerId,
    string CustomerEmail,
    List<OrderItemDto> Items,
    decimal Total,
    DateTime PlacedAt
);
```

— [Shared/ShopFlow.Shared/Events/OrderPlacedEvent.cs](../../Shared/ShopFlow.Shared/Events/OrderPlacedEvent.cs), alongside [OrderItemDto.cs](../../Shared/ShopFlow.Shared/Events/OrderItemDto.cs) (`ProductId`, `ProductName`, `UnitPrice`, `Quantity`). The same library also holds `OrderShippedEvent`, `CartStockAdjustedEvent`, and `CheckStockRequest`/`CheckStockResponse` for Product's stock-check request/response messaging — this document focuses on the Order→Cart and Order→Notification `OrderPlacedEvent` flow specifically.

### The publisher — Order Service

**[OrderEventPublisher](../../Services/Order/Order.Infrastructure/Events/OrderEventPublisher.cs)** is the only class in Order that touches `MassTransit.IPublishEndpoint`:

```csharp
public class OrderEventPublisher : IOrderEventPublisher
{
    private readonly IPublishEndpoint _publishEndpoint;

    public OrderEventPublisher(IPublishEndpoint publishEndpoint) => _publishEndpoint = publishEndpoint;

    public async Task PublishOrderPlacedAsync(OrderEntity order, CancellationToken ct)
    {
        var items = order.OrderItems
            .Select(i => new OrderItemDto(i.ProductId, i.ProductName, i.UnitPrice, i.Quantity))
            .ToList();

        await _publishEndpoint.Publish(new OrderPlacedEvent(
            order.Id, order.CustomerId, order.CustomerEmail, items, order.TotalAmount, DateTime.UtcNow
        ), ct);
    }
}
```

`IOrderEventPublisher` is declared in Application ([Order.Application/Interfaces](../../Services/Order/Order.Application/Interfaces/)), same inversion pattern as every repository interface in ShopFlow — Application depends on an abstraction, Infrastructure implements it against the real messaging library.

Order's `Program.cs` `AddMassTransit` block is deliberately the shortest of the three services — it only publishes, so it configures no consumer and no receive endpoint:

```csharp
builder.Services.AddMassTransit(x =>
{
    x.UsingRabbitMq((ctx, cfg) =>
    {
        cfg.Host(builder.Configuration["RabbitMQ:Host"] ?? "localhost", "/", h =>
        {
            h.Username(builder.Configuration["RabbitMQ:User"] ?? "guest");
            h.Password(builder.Configuration["RabbitMQ:Pass"] ?? "guest");
        });
    });
});
```

— [Order.Api/Program.cs](../../Services/Order/Order.Api/Program.cs).

### The consumers — Cart and Notification, on separate queues

Both Cart and Notification consume the exact same `OrderPlacedEvent`, for entirely different purposes. Cart's consumer is one line:

```csharp
public class OrderPlacedConsumer : IConsumer<OrderPlacedEvent>
{
    private readonly ICartRepository _cartRepository;
    public OrderPlacedConsumer(ICartRepository cartRepository) => _cartRepository = cartRepository;

    public async Task Consume(ConsumeContext<OrderPlacedEvent> context)
        => await _cartRepository.ClearCartAsync(context.Message.CustomerId, context.CancellationToken);
}
```

— [Cart.Infrastructure/Events/OrderPlacedConsumer.cs](../../Services/Cart/Cart.Infrastructure/Events/OrderPlacedConsumer.cs) — bypassing MediatR and the Application layer entirely, since there's no HTTP caller waiting and no validation a domain event could meaningfully fail (see [Cart-Service.md §6](../Architecture/Cart-Service.md#6-request-flow--end-to-end-example)).

Notification's consumer maps the event into an email:

```csharp
public async Task Consume(ConsumeContext<OrderPlacedEvent> context)
{
    var message = context.Message;
    var items = message.Items
        .Select(i => new OrderLineItem(i.ProductName, i.UnitPrice, i.Quantity))
        .ToList();

    await _emailService.SendOrderConfirmationAsync(
        message.CustomerEmail, message.OrderId, items, message.Total, context.CancellationToken);
}
```

— [Notification.Infrastructure/Events/OrderPlacedConsumer.cs](../../Services/Notification/Notification.Infrastructure/Events/OrderPlacedConsumer.cs). `SendOrderConfirmationAsync` (`IEmailService`) composes and sends the actual email via MailKit — outside this document's scope, which stops at the messaging boundary.

**Each consumer is bound to its own, distinctly-named queue** — this is a real hazard the team found and avoided, not an accident of naming:

```csharp
// Cart:
cfg.ReceiveEndpoint("order-placed-queue", e =>
{
    e.ConfigureConsumer<OrderPlacedConsumer>(ctx);
    e.UseMessageRetry(r => r.Exponential(3,
        TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(2)));
});
```

```csharp
// Notification — deliberately a different queue name:
cfg.ReceiveEndpoint("notification-order-placed-queue", e =>
{
    e.ConfigureConsumer<OrderPlacedConsumer>(ctx);
    e.UseMessageRetry(r => r.Exponential(3,
        TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(2)));
});
```

— [Cart.Api/Program.cs](../../Services/Cart/Cart.Api/Program.cs) and [Notification.Api/Program.cs](../../Services/Notification/Notification.Api/Program.cs). Both `ReceiveEndpoint`s bind to the same `ShopFlow.Shared.Events:OrderPlacedEvent` fan-out exchange (MassTransit's default exchange-naming convention from the message type), but each declares its **own** queue. Per [Documentations/Phases/Phase5.md](../Phases/Phase5.md):

> **Correctness hazard found and avoided**: Cart already binds a consumer to a RabbitMQ queue literally named `"order-placed-queue"`. Notification's receive endpoint uses a distinct name, `"notification-order-placed-queue"` — reusing Cart's name would have made Notification a second *competing* consumer on Cart's own queue (round-robin delivery) rather than an independent subscriber, silently breaking Cart's cart-clearing about half the time.

That distinction — one queue per logical subscriber, all bound to the same fan-out exchange — is what turns "publish once" into "every interested service gets its own copy," rather than the two consumers splitting messages between them. Phase5.md also records this was verified live against a running RabbitMQ management API: two queues on the `OrderPlacedEvent` exchange, each with exactly one active consumer.

### Retry policy

Both receive endpoints use the identical exponential backoff policy: `UseMessageRetry(r => r.Exponential(3, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(2)))` — up to 3 retry attempts, starting at a 1-second delay, capping at 10 seconds, stepping by 2 seconds between attempts. If a consumer throws (e.g., a transient Redis or SMTP failure), MassTransit redelivers the message on this schedule before giving up — there is no dead-letter/error-queue handling visible beyond MassTransit's own default fault behavior (messages that exhaust retries go to a MassTransit-managed `_error` queue by broker convention, but no custom handling of that queue exists in this codebase).

### Testing without a real broker

Every Infrastructure-layer messaging test spins up MassTransit's **in-process test harness** instead of a real RabbitMQ connection. The publisher side:

```csharp
await using var provider = new ServiceCollection()
    .AddMassTransitTestHarness()
    .BuildServiceProvider(true);

var harness = provider.GetRequiredService<ITestHarness>();
await harness.Start();

var publisher = new OrderEventPublisher(harness.Bus);
await publisher.PublishOrderPlacedAsync(order, default);

(await harness.Published.Any<OrderPlacedEvent>(x =>
    x.Context.Message.OrderId == order.Id && ...)).Should().BeTrue();
```

— [Order.Infrastructure.Tests/Events/OrderEventPublisherTests.cs](../../Services/Order/Order.Infrastructure.Tests/Events/OrderEventPublisherTests.cs). The consumer side registers the real consumer against the harness and asserts on the downstream mock, not on the message itself:

```csharp
var cartRepository = Substitute.For<ICartRepository>();

await using var provider = new ServiceCollection()
    .AddSingleton(cartRepository)
    .AddMassTransitTestHarness(cfg => cfg.AddConsumer<OrderPlacedConsumer>())
    .BuildServiceProvider(true);

var harness = provider.GetRequiredService<ITestHarness>();
await harness.Start();

await harness.Bus.Publish(new OrderPlacedEvent(Guid.NewGuid(), customerId, "user@test.com", [], 45m, DateTime.UtcNow));

(await harness.Consumed.Any<OrderPlacedEvent>()).Should().BeTrue();
await cartRepository.Received(1).ClearCartAsync(customerId, Arg.Any<CancellationToken>());
```

— [Cart.Infrastructure.Tests/Events/OrderPlacedConsumerTests.cs](../../Services/Cart/Cart.Infrastructure.Tests/Events/OrderPlacedConsumerTests.cs). At the API-test level, both `CartApiFactory` and `OrderApiFactory` go further: they walk the `WebApplicationFactory`'s service collection and **remove every descriptor whose service/implementation type lives under the `MassTransit` namespace** — the real `AddMassTransit(...).UsingRabbitMq(...)` registration from `Program.cs` would otherwise try to dial an actual broker the instant the test host starts — then re-add `AddMassTransitTestHarness` with the same consumer(s) wired to the in-memory transport. This means no test in the solution, at any layer, ever requires a running RabbitMQ container; only `Cart.Infrastructure.Tests`' Redis coverage and the SQL-Server-backed Infrastructure test projects need Docker.

## Gotchas & deviations

**MassTransit is pinned to `8.5.10`, not `9.x`.** Per [Documentations/Phases/Phase4.md](../Phases/Phase4.md):

> **Important deviation from the plan — MassTransit version:** the plan called for MassTransit(.RabbitMQ) latest stable, which at planning time was `9.2.0`. During the Docker smoke test, `9.2.0` failed at startup with `MassTransit.ConfigurationException: License must be specified with SetLicense/SetLicenseLocation...` — MassTransit introduced a mandatory commercial license starting at `9.0.0`. Since this project has no such license, **all MassTransit packages are pinned to `8.5.10`**, the last fully open-source (Apache 2.0) release before the licensing change. ... keep future services (Order, Notification in Phase 5) on `8.5.10` too unless the project acquires a MassTransit license.

This is confirmed as consistently applied across every service that references it — `MassTransit.RabbitMQ` is `8.5.10` in [Order.API.csproj](../../Services/Order/Order.Api/Order.API.csproj), [Order.Infrastructure.csproj](../../Services/Order/Order.Infrastructure/Order.Infrastructure.csproj), [Cart.Infrastructure.csproj](../../Services/Cart/Cart.Infrastructure/Cart.Infrastructure.csproj), [Cart.API.csproj](../../Services/Cart/Cart.Api/Cart.API.csproj), [Notification.API.csproj](../../Services/Notification/Notification.Api/Notification.API.csproj), [Notification.Infrastructure.csproj](../../Services/Notification/Notification.Infrastructure/Notification.Infrastructure.csproj), [Product.API.csproj](../../Services/Product/Product.Api/Product.API.csproj), and [Product.Infrastructure.csproj](../../Services/Product/Product.Infrastructure/Product.Infrastructure.csproj) — eight `.csproj` files, one version, no drift. Per [Phase5.md](../Phases/Phase5.md), pinning to `8.5.10` also resolves `RabbitMQ.Client` to `7.2.1` in both Order and Notification. Upgrading past `8.5.10` anywhere in the solution would require either acquiring a MassTransit commercial license or migrating off MassTransit's paid tier entirely — this is a live constraint on any future dependency-bump work, not historical trivia.

**Queue-per-consumer, same exchange** is the second real gotcha documented above: two consumers of the same event type must never share a queue name unless they're meant to load-balance the same work, or one will silently steal roughly half the other's messages with no error raised anywhere.

**No dead-letter handling is implemented.** Beyond the 3-attempt exponential retry, there is no custom code in this repository that inspects or reprocesses messages that exhaust their retries — they rely entirely on MassTransit's default fault/error-queue behavior with no visibility from ShopFlow's own code.
