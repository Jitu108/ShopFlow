# Order Saga with RabbitMQ — Draft

**Status:** Draft / proposal, not implemented. Written for discussion, evaluated against the stock-tracking design that shipped on `TJKG-014` (merged in #24) — this is not a description of current behavior.

## Why revisit this after TJKG-014

TJKG-014 already closed the main gap that motivated this: an order could previously be confirmed with no stock check at all. The shipped design reserves stock at cart-mutation time (`CartStockAdjustedEvent`, choreography — see [Cart-Service.md](../Architecture/Cart-Service.md), [Product-Service.md](../Architecture/Product-Service.md)) and re-verifies it synchronously at confirmation (`CheckStockRequest`/`CheckStockResponse` request-response — [Order-Service.md §2](../Architecture/Order-Service.md#2-orderapplication--use-cases-cqrs)).

That design is documented as having three known, accepted gaps:

1. **No DB-level concurrency protection** — `ProductRepository.UpdateAsync` is a plain read-modify-write, so two concurrent `CartStockAdjustedEvent`s for the same product can still lose an update to each other (Product-Service.md §4).
2. **`ClearCartCommand` doesn't release reserved stock** — clearing a cart leaks every unit those items had reserved until the 7-day Redis TTL expires (Cart-Service.md §2, §4).
3. **No compensation on confirm-time failure** — if `CheckStockRequest` comes back unavailable, `ConfirmOrderCommandHandler` throws a `DomainException` and leaves the order `Pending` forever; nothing tells the customer *when* stock frees up, and nothing automatically retries or cancels the order.
4. **No resilience to a broker/Product outage** — `StockAvailabilityChecker.CheckAsync` has a 10s timeout with no retry, circuit breaker, or fallback; an outage turns every confirmation into a `500`.

None of these are solved by adding a saga on top of the current design — a saga doesn't fix a missing optimistic-concurrency check. What a saga *would* change is gap 3: instead of "throw and leave Pending forever," an orchestrated saga gives the system an explicit, resumable state machine for what happens after a failed check (timeout-and-cancel, retry-on-restock, notify) — currently that logic doesn't exist anywhere.

## Sketch: orchestration on top of the existing reservation model

This does **not** replace `CartStockAdjustedEvent`/`CheckStockRequest` — cart-time reservation stays the primary mechanism. The saga only takes over the confirm→settle window, replacing the current "check once, throw on failure" step with an explicit state machine that can wait, retry, or compensate.

### New contracts — `Shared/ShopFlow.Shared/Events/`

```csharp
public record OrderConfirmationRequested(Guid OrderId, List<OrderItemDto> Items, string CustomerEmail, decimal Total);
public record StockConfirmed(Guid OrderId);                         // Product → saga, stock held
public record StockUnavailable(Guid OrderId, List<Guid> ProductIds); // Product → saga, insufficient
public record OrderConfirmationSucceeded(Guid OrderId, string CustomerEmail, decimal Total); // saga → Notification
public record OrderConfirmationFailed(Guid OrderId, string CustomerEmail, string Reason);    // saga → Notification
```

### Saga — `Services/Order/Order.Infrastructure/Sagas/`

```csharp
public class OrderConfirmationSagaState : SagaStateMachineInstance
{
    public Guid CorrelationId { get; set; }   // = OrderId
    public string CurrentState { get; set; } = default!;
    public string CustomerEmail { get; set; } = default!;
    public decimal Total { get; set; }
    public int RetryCount { get; set; }
}

public class OrderConfirmationSagaStateMachine : MassTransitStateMachine<OrderConfirmationSagaState>
{
    public State AwaitingStockConfirmation { get; private set; } = default!;
    public State Confirmed { get; private set; } = default!;
    public State Failed { get; private set; } = default!;

    public Event<OrderConfirmationRequested> Requested { get; private set; } = default!;
    public Event<StockConfirmed> Confirmed_ { get; private set; } = default!;
    public Event<StockUnavailable> Unavailable { get; private set; } = default!;
    public Schedule<OrderConfirmationSagaState, ConfirmationTimeoutExpired> Timeout { get; private set; } = default!;

    public OrderConfirmationSagaStateMachine()
    {
        InstanceState(x => x.CurrentState);

        Event(() => Requested, e => e.CorrelateById(m => m.Message.OrderId));
        Event(() => Confirmed_, e => e.CorrelateById(m => m.Message.OrderId));
        Event(() => Unavailable, e => e.CorrelateById(m => m.Message.OrderId));

        Initially(
            When(Requested)
                .Then(ctx => { ctx.Saga.CustomerEmail = ctx.Message.CustomerEmail; ctx.Saga.Total = ctx.Message.Total; })
                .Send(new Uri("queue:product-check-stock"), ctx => new CheckStockRequest(ctx.Message.Items))
                .Schedule(Timeout, ctx => ctx.Init<ConfirmationTimeoutExpired>(new { ctx.Message.OrderId }), ctx => TimeSpan.FromSeconds(10))
                .TransitionTo(AwaitingStockConfirmation));

        During(AwaitingStockConfirmation,
            When(Confirmed_)
                .Unschedule(Timeout)
                .Publish(ctx => new OrderConfirmationSucceeded(ctx.Saga.CorrelationId, ctx.Saga.CustomerEmail, ctx.Saga.Total))
                .TransitionTo(Confirmed)
                .Finalize(),
            When(Unavailable)
                .Unschedule(Timeout)
                .Publish(ctx => new OrderConfirmationFailed(ctx.Saga.CorrelationId, ctx.Saga.CustomerEmail,
                    $"Insufficient stock for: {string.Join(", ", ctx.Message.ProductIds)}"))
                .TransitionTo(Failed)
                .Finalize(),
            When(Timeout.Received)
                .Publish(ctx => new OrderConfirmationFailed(ctx.Saga.CorrelationId, ctx.Saga.CustomerEmail, "Stock check timed out"))
                .TransitionTo(Failed)
                .Finalize());

        SetCompletedWhenFinalized();
    }
}
```

Registered in `Order.Api/Program.cs`:

```csharp
x.AddSagaStateMachine<OrderConfirmationSagaStateMachine, OrderConfirmationSagaState>()
    .EntityFrameworkRepository(r => { /* AppDbContext, same SQL Server as OrderEntity */ });
```

### What changes in the existing flow

- `ConfirmOrderCommandHandler` no longer calls `IStockAvailabilityChecker.CheckAsync` synchronously in the request. Instead it publishes `OrderConfirmationRequested` and returns an `OrderDto` with status `Pending` — confirmation becomes asynchronous, not a blocking HTTP call.
- `CheckStockConsumer` in Product gains a sibling that responds with `StockConfirmed`/`StockUnavailable` events instead of (or in addition to) the request/response reply, so the saga can correlate on `OrderId`.
- A new consumer in `Order.Infrastructure/Events/` reacts to `OrderConfirmationSucceeded`/`OrderConfirmationFailed` and calls `order.Confirm()` / `order.Cancel()` (needs adding, mirroring the sketch discussed earlier in this session) through the existing repository — keeping domain mutation in the Application layer, not the saga.

### Trade-off vs. the shipped design

| | Shipped (TJKG-014) | Saga on top |
|---|---|---|
| Confirmation latency | Synchronous, blocks on a single 10s-timeout RPC | Asynchronous — client polls or gets a webhook/notification |
| Failure handling | Throw `DomainException`, order stuck `Pending` | Explicit `Failed` state, automatic cancellation + customer notification |
| Retry-on-restock | None | Straightforward to add as another saga state (`Timeout` → re-check instead of fail) |
| Complexity | Low — one interface, one request/response pair | Higher — new saga persistence table, new events, async API contract change |

**Recommendation:** don't build this now. The synchronous check-then-confirm gate is simpler and adequate for the current gap it closes. Revisit orchestration if/when there's a real requirement for retry-on-restock or the confirm endpoint needs to stop blocking on Product's availability. If pursued, it should compose with `CheckStockRequest`/`CartStockAdjustedEvent`, not replace them — and gaps 1 and 2 above (DB concurrency, `ClearCart` leak) need fixing independently either way.
