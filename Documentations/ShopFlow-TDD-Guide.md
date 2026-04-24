# ShopFlow — Test-Driven Development Guide

## TDD Cycle
Write a failing test first, then write the minimum code to make it pass, then refactor.  
The Clean Architecture structure here is ideal for TDD because each layer has clear boundaries and dependencies point inward.

---

## Testing Stack

| Layer | Tool |
|---|---|
| .NET unit tests | xUnit + FluentAssertions + NSubstitute |
| .NET integration tests | xUnit + `WebApplicationFactory` + Testcontainers |
| EF Core in-memory | `UseInMemoryDatabase` (unit) or real SQL via Testcontainers (integration) |
| RabbitMQ / Redis | Testcontainers (spins real containers in tests) |
| Angular | Jest + Angular Testing Library |

---

## What to Test Per Layer

### Domain Layer — Pure Unit Tests
No dependencies, no mocks needed. Test entity behavior and invariants.

```csharp
// Test first:
[Fact]
public void Order_Create_ShouldCalculateTotalFromItems()
{
    var items = new List<CartItemDto>
    {
        new(ProductId: Guid.NewGuid(), "Widget", 10.00m, 2),
        new(ProductId: Guid.NewGuid(), "Gadget", 25.00m, 1)
    };

    var order = Order.Create(Guid.NewGuid(), items);

    order.TotalAmount.Should().Be(45.00m);
    order.Status.Should().Be(OrderStatus.Pending);
    order.OrderItems.Should().HaveCount(2);
}
```

Then write `Order.Create()` to make it pass.

---

### Application Layer — Unit Tests with Mocked Interfaces
All handlers receive interfaces (`IProductRepository`, `ICacheService`) — mock those with NSubstitute.

```csharp
[Fact]
public async Task GetProductByIdHandler_WhenCacheHit_ShouldNotCallRepository()
{
    var cache = Substitute.For<ICacheService>();
    var repo  = Substitute.For<IProductRepository>();
    var dto   = new ProductDto(Id: productId, Name: "Widget", Price: 9.99m);

    cache.GetAsync<ProductDto>($"product:{productId}").Returns(dto);

    var handler = new GetProductByIdQueryHandler(repo, cache);
    var result  = await handler.Handle(new GetProductByIdQuery(productId), default);

    result.Should().BeEquivalentTo(dto);
    await repo.DidNotReceive().GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
}
```

---

### Infrastructure Layer — Integration Tests with Testcontainers
Test real EF Core queries, Redis operations, and MassTransit consumers against real containers.

```csharp
// Testcontainers spins up a real SQL Server for the test run
public class ProductRepositoryTests : IAsyncLifetime
{
    private readonly MsSqlContainer _sqlContainer = new MsSqlBuilder().Build();

    public async Task InitializeAsync() => await _sqlContainer.StartAsync();
    public async Task DisposeAsync()    => await _sqlContainer.DisposeAsync();

    [Fact]
    public async Task AddProduct_ThenGetById_ShouldReturnProduct()
    {
        var ctx  = CreateDbContext(_sqlContainer.GetConnectionString());
        var repo = new ProductRepository(ctx);
        var product = Product.Create(vendorId, "Widget", 9.99m, categoryId);

        await repo.AddAsync(product, default);
        var fetched = await repo.GetByIdAsync(product.Id, default);

        fetched.Should().NotBeNull();
        fetched!.Name.Should().Be("Widget");
    }
}
```

---

### API Layer — Integration Tests with `WebApplicationFactory`
Test HTTP endpoints end-to-end within the service boundary. Replace infrastructure with test doubles.

```csharp
public class ProductsControllerTests : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task GetProducts_WithoutAuth_ShouldReturn200()
    {
        var response = await _client.GetAsync("/api/products");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task CreateProduct_WithoutVendorRole_ShouldReturn403()
    {
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", CustomerToken);

        var response = await _client.PostAsJsonAsync("/api/products", payload);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
```

---

## TDD Cycle Per Service — Practical Order

For each service, work **inside-out**: Domain → Application → Infrastructure → API.

```
1. Write domain entity test          → make it pass (pure C#)
2. Write command/query handler test  → make it pass (mock interfaces)
3. Write repository test             → make it pass (Testcontainers)
4. Write controller/endpoint test    → make it pass (WebApplicationFactory)
```

This means **the test file exists before the class file** at every step.

---

## MediatR Pipeline Behaviors — Test Separately

```csharp
[Fact]
public async Task ValidationBehavior_WithInvalidCommand_ShouldThrowValidationException()
{
    var command  = new CreateProductCommand(VendorId: Guid.Empty, Name: "", Price: -1);
    var behavior = new ValidationBehavior<CreateProductCommand, ProductDto>(
        new[] { new CreateProductCommandValidator() });

    var act = () => behavior.Handle(command, () => Task.FromResult(new ProductDto()), default);

    await act.Should().ThrowAsync<ValidationException>();
}
```

---

## Event Contracts — Test Consumer Behavior

```csharp
[Fact]
public async Task OrderPlacedConsumer_ShouldSendConfirmationEmail()
{
    var emailService = Substitute.For<IEmailService>();
    var consumer     = new OrderPlacedConsumer(emailService);
    var @event       = new OrderPlacedEvent(OrderId, CustomerId, "user@test.com", items, 45m, DateTime.UtcNow);

    await consumer.Consume(MockConsumeContext(@event));

    await emailService.Received(1)
        .SendOrderConfirmationAsync("user@test.com", OrderId, items, 45m);
}
```

---

## Angular — TDD with Jest

```typescript
// catalog.component.spec.ts — write before the component
it('should display products from the store', () => {
  const products = [{ id: '1', name: 'Widget', price: 9.99 }];
  store.overrideSelector(selectAllProducts, products);

  fixture.detectChanges();

  const cards = fixture.debugElement.queryAll(By.css('app-product-card'));
  expect(cards.length).toBe(1);
});
```

---

## Folder Convention

```
Services/Product/
├── Product.Domain.Tests/
│   └── Entities/ProductTests.cs
├── Product.Application.Tests/
│   ├── Commands/CreateProductCommandHandlerTests.cs
│   └── Queries/GetProductByIdQueryHandlerTests.cs
├── Product.Infrastructure.Tests/
│   └── Repositories/ProductRepositoryTests.cs       ← Testcontainers
└── Product.Api.Tests/
    └── Controllers/ProductsControllerTests.cs        ← WebApplicationFactory
```

---

## Recommended Sequence to Start

1. Add xUnit + FluentAssertions + NSubstitute + Testcontainers NuGet packages to each test project
2. Write `Order.Create()` domain tests first (no infrastructure needed)
3. Write `PlaceOrderCommandHandler` tests next (mock `IOrderRepository` + `IPublisher`)
4. Only then scaffold the actual handler to make them green
5. Add Testcontainers integration tests for `OrderRepository` once the handler is clean
