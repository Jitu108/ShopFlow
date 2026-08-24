# The Testing Stack

## Abstract

Every ShopFlow backend service — Identity, Product, Cart, Order, Notification — is tested with the same toolset, laid out per Clean Architecture layer: xUnit as the runner, FluentAssertions for readable asserts, NSubstitute for mocking interfaces near the core, Testcontainers for real SQL Server/Redis at the infrastructure edge, `Microsoft.AspNetCore.Mvc.Testing`'s `WebApplicationFactory` for full-stack in-process HTTP tests, `EntityFrameworkCore.InMemory` as a lighter EF Core double at the API layer, and `coverlet.collector` wired into every test project so `dotnet test` can report coverage without extra setup. This document walks each tool with real code from the repo, and explains the "inside-out" TDD order — Domain → Application → Infrastructure → Api — that [ShopFlow-TDD-Guide.md](../ShopFlow-TDD-Guide.md) prescribes and that every service's test suite actually follows.

## What it is

| Tool | Package | Layer(s) it appears in |
| --- | --- | --- |
| xUnit | `xunit`, `xunit.runner.visualstudio` | All four test projects, every service |
| FluentAssertions | `FluentAssertions` | All four test projects, every service |
| NSubstitute | `NSubstitute` | Application.Tests everywhere; also some Infrastructure.Tests (consumer tests) |
| Testcontainers | `Testcontainers.MsSql`, `Testcontainers.Redis`, plain `Testcontainers` | Infrastructure.Tests (SQL Server, Redis, and — for Notification — a raw SMTP container) |
| `Microsoft.AspNetCore.Mvc.Testing` (`WebApplicationFactory`) | `Microsoft.AspNetCore.Mvc.Testing` | Api.Tests everywhere, plus one Infrastructure.Tests project (Notification, explained below) |
| `EntityFrameworkCore.InMemory` | `Microsoft.EntityFrameworkCore.InMemory` | Api.Tests for the three SQL-backed services (Identity, Product, Order) |
| coverlet.collector | `coverlet.collector` | Every single test project, all layers |

## Why ShopFlow uses it

The guiding rule, stated directly in [ShopFlow-TDD-Guide.md](../ShopFlow-TDD-Guide.md), is to work **inside-out**: "Domain → Application → Infrastructure → API," with cheap, deterministic tests near the Domain and real infrastructure only at the edges where it can't be avoided. The tool selection follows directly from that rule:

- Near the core (Domain, Application), tests must be fast and have zero external dependencies — hence pure xUnit/FluentAssertions for Domain, and NSubstitute-mocked interfaces for Application.
- At the true infrastructure boundary (a repository or cache implementation), a mock would only prove the mock behaves correctly, not that the real technology does — hence Testcontainers spins up the actual SQL Server, Redis, or SMTP server in Docker.
- At the API boundary, the goal shifts again: prove the whole request pipeline (routing, auth, model binding, exception middleware, MediatR pipeline) works together, without needing a live database or broker for every test run — hence `WebApplicationFactory` hosts the real `Program.cs` in-process, with only the narrowest possible seams (a repository, a message bus) swapped for fakes/test harnesses.

## How it's used

### xUnit — the runner everywhere

Every test project targets the same shape: `<PackageReference Include="xunit" Version="2.9.3" />` plus `xunit.runner.visualstudio`, and every `.csproj` adds a global `<Using Include="Xunit" />` so `[Fact]`/`[Theory]` need no `using` statement in test files. See any test csproj, e.g. [Cart.Domain.Tests.csproj](../../Services/Cart/Cart.Domain.Tests/Cart.Domain.Tests.csproj). `[Fact]` is the only attribute used across the sampled test files in this repo — no parameterized `[Theory]` cases were found in the files read for this document.

### FluentAssertions — fluent `.Should()` over raw `Assert.Equal`

Every assertion in every test file sampled uses the `.Should()` chain, never `Assert.Equal`/`Assert.True`. From [RedisCartRepositoryTests.cs](../../Services/Cart/Cart.Infrastructure.Tests/Persistence/RedisCartRepositoryTests.cs):

```csharp
cart.Should().HaveCount(1);
cart[productId].Quantity.Should().Be(5);
```

and a TTL assertion that would be painful to express with raw `Assert`:

```csharp
ttl.Should().NotBeNull();
ttl!.Value.Should().BeCloseTo(TimeSpan.FromDays(7), TimeSpan.FromMinutes(1));
```

The reason to prefer this over `Assert.Equal(TimeSpan.FromDays(7), ttl.Value)` is exactly this case: `BeCloseTo` expresses "close enough" (Redis's actual TTL will never be *exactly* 7 days to the tick) as a single readable statement, and failure messages read as English ("Expected ttl.Value to be within 1m from 7.00:00:00, but found ..."). Collection assertions like `.Should().ContainKey(...)`, `.Should().ContainSingle(x => ...)`, and `.Should().NotContainKey(...)` (also from the same file, and from [ProductRepositoryTests.cs](../../Services/Product/Product.Infrastructure.Tests/Persistence/ProductRepositoryTests.cs)) are similarly more direct than the LINQ+`Assert.True` equivalent.

### NSubstitute — mocking interfaces in Application.Tests

Application-layer handlers depend only on interfaces (`ICartRepository`, `IProductRepository`, `IEmailService`, ...), so their tests substitute those interfaces instead of standing up real infrastructure. A complete example from [AddCartItemCommandHandlerTests.cs](../../Services/Cart/Cart.Application.Tests/Commands/AddCartItemCommandHandlerTests.cs):

```csharp
private readonly ICartRepository _cartRepository = Substitute.For<ICartRepository>();
private readonly AddCartItemCommandHandler _handler;

public AddCartItemCommandHandlerTests()
{
    _handler = new AddCartItemCommandHandler(_cartRepository);
}

[Fact]
public async Task Handle_WithNewProduct_ShouldUpsertWithRequestedQuantity()
{
    var userId = Guid.NewGuid();
    var command = new AddCartItemCommand(userId, Guid.NewGuid(), "Widget", 9.99m, 2);
    _cartRepository.GetCartAsync(userId, default)
        .Returns(new Dictionary<Guid, CartItemDto>());

    var result = await _handler.Handle(command, default);

    result.Quantity.Should().Be(2);
    await _cartRepository.Received(1).UpsertItemAsync(userId,
        Arg.Is<CartItemDto>(i => i.ProductId == command.ProductId && i.Quantity == 2), default);
}
```

This is the `Substitute.For<T>()` → `.Returns(...)` (stub) → `Received(1)`/`Arg.Is<T>(...)` (verify) shape used throughout Application.Tests in every service. The same pattern reaches into one Infrastructure.Tests project too: Cart's [OrderPlacedConsumerTests.cs](../../Services/Cart/Cart.Infrastructure.Tests/Events/OrderPlacedConsumerTests.cs) and Notification's [OrderPlacedConsumerTests.cs](../../Services/Notification/Notification.Infrastructure.Tests/Events/OrderPlacedConsumerTests.cs) both substitute the *repository/service* the consumer calls (`ICartRepository`, `IEmailService`) while still publishing the event through a **real** in-process MassTransit bus — only the leaf dependency is faked, not the messaging plumbing being tested:

```csharp
var emailService = Substitute.For<IEmailService>();
...
await emailService.Received(1).SendOrderConfirmationAsync(
    "user@test.com", orderId,
    Arg.Is<List<OrderLineItem>>(items => items.SequenceEqual(expectedItems)),
    45m, Arg.Any<CancellationToken>());
```

### Testcontainers — real Docker containers for Infrastructure.Tests

`Testcontainers.MsSql` and `Testcontainers.Redis` spin up an actual, disposable SQL Server or Redis instance in Docker for the duration of a test class, so repository code runs against the real query engine/data structure it will hit in production — not a fake that could silently diverge in behavior. Both follow the same `IAsyncLifetime` shape: start the container in `InitializeAsync`, dispose it in `DisposeAsync`.

SQL Server, from [ProductRepositoryTests.cs](../../Services/Product/Product.Infrastructure.Tests/Persistence/ProductRepositoryTests.cs) (the same shape is used by Identity's and Order's `Infrastructure.Tests`):

```csharp
public class ProductRepositoryTests : IAsyncLifetime
{
    private readonly MsSqlContainer _sql = new MsSqlBuilder().Build();

    public async Task InitializeAsync() => await _sql.StartAsync();
    public async Task DisposeAsync() => await _sql.DisposeAsync();

    private AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(_sql.GetConnectionString())
            .Options;
        var ctx = new AppDbContext(options);
        ctx.Database.EnsureCreated();
        return ctx;
    }
    ...
}
```

Redis, from [RedisCartRepositoryTests.cs](../../Services/Cart/Cart.Infrastructure.Tests/Persistence/RedisCartRepositoryTests.cs):

```csharp
private readonly RedisContainer _redis = new RedisBuilder().Build();
private IConnectionMultiplexer _connectionMultiplexer = null!;
private RedisCartRepository _repository = null!;

public async Task InitializeAsync()
{
    await _redis.StartAsync();
    _connectionMultiplexer = await ConnectionMultiplexer.ConnectAsync(_redis.GetConnectionString());
    _repository = new RedisCartRepository(_connectionMultiplexer);
}
```

This is why [Cart-Service.md §5](../Architecture/Cart-Service.md#5-test-projects) and [STATUS.md](../STATUS.md) both flag Infrastructure.Tests projects with a `*` — they need Docker running locally (or in CI) to pass at all, unlike every other layer.

There is no dedicated Testcontainers module for SMTP, so Notification's Infrastructure.Tests reaches for the generic `Testcontainers` package directly against the `rnwood/smtp4dev:v3` image — see [12 → MailKit doc](./13-mailkit-notifications.md) for the full example; the comment in [MailKitEmailServiceTests.cs](../../Services/Notification/Notification.Infrastructure.Tests/Email/MailKitEmailServiceTests.cs) states the rationale explicitly: *"consistent with this project's own NFR-25 philosophy of testing infrastructure against real dependencies... there is no dedicated Testcontainers module for SMTP, so this uses the generic Testcontainers package directly."*

### `WebApplicationFactory` + `Microsoft.AspNetCore.Mvc.Testing` — full-stack HTTP tests

Every service's Api.Tests project hosts the real `Program.cs` in-process via `WebApplicationFactory<Program>` and issues real `HttpClient` requests against it — routing, `[Authorize]`, model binding, the MediatR pipeline, and the exception-to-status middleware all run for real. The only things swapped out are the true I/O edges: the repository (so no database round-trip is needed) and the message bus (so no test ever dials a real broker).

Cart's [CartApiFactory.cs](../../Services/Cart/Cart.Api.Tests/Fixtures/CartApiFactory.cs) is the clearest example of both swaps in one place:

```csharp
public class CartApiFactory : WebApplicationFactory<Program>
{
    public FakeCartRepository CartRepository { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{JwtSettings.SectionName}:Secret"] = JwtTokenHelper.TestSecret,
                ...
                ["ConnectionStrings:Redis"] = "localhost:6379"
            });
        });

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ICartRepository>();
            services.AddSingleton<ICartRepository>(CartRepository);

            // Program.cs wires AddMassTransit(...).UsingRabbitMq(...), which would otherwise try
            // to reach a real broker when the test host starts. Swap every MassTransit-registered
            // service for the in-memory test transport so API tests never touch a network broker.
            var massTransitDescriptors = services
                .Where(d =>
                    (d.ServiceType.Namespace?.StartsWith("MassTransit", StringComparison.Ordinal) ?? false) ||
                    (d.ImplementationType?.Namespace?.StartsWith("MassTransit", StringComparison.Ordinal) ?? false))
                .ToList();

            foreach (var descriptor in massTransitDescriptors)
            {
                services.Remove(descriptor);
            }

            services.AddMassTransitTestHarness(cfg =>
            {
                cfg.AddConsumer<OrderPlacedConsumer>();
            });
        });
    }
}
```

`ConfigureAppConfiguration` overrides JWT settings and connection strings so `WebApplicationFactory`'s config wins over `appsettings.Development.json`; `ConfigureTestServices` is where `services.RemoveAll<T>()` + `services.AddSingleton<T>(fake)` swaps the real repository for [FakeCartRepository](../../Services/Cart/Cart.Api.Tests/Fixtures/FakeCartRepository.cs), an in-memory dictionary reproducing `ICartRepository`'s four methods. The MassTransit-namespace sweep is the most distinctive step: it walks the DI container and removes *every* descriptor whose service or implementation type lives under `MassTransit`, because `Program.cs`'s real `AddMassTransit(...).UsingRabbitMq(...)` call would otherwise try to dial an actual broker the instant the test host starts. `AddMassTransitTestHarness` then re-registers the same consumer against an in-memory transport. The identical sweep-and-replace pattern appears in [Notification's HealthCheckTests.cs](../../Services/Notification/Notification.Infrastructure.Tests/HealthCheckTests.cs), with an explicit comment: *"mirrors Cart's CartApiFactory."*

Order's, Identity's, and Product's Api.Tests use the same `WebApplicationFactory` + fake-repository shape (`OrderApiFactory`, `IdentityApiFactory`, `ProductApiFactory`), each under `Fixtures/`.

### `EntityFrameworkCore.InMemory` — a lighter EF Core double at the Api layer

Identity, Product, and Order all reference `Microsoft.EntityFrameworkCore.InMemory` in their **Api.Tests** projects, while Cart and Notification don't (Cart has no SQL Server anywhere; Notification's Infrastructure.Tests reaches for `WebApplicationFactory` directly instead of having a separate Api.Tests project — see the Gotchas section). [ProductApiFactory.cs](../../Services/Product/Product.Api.Tests/Fixtures/ProductApiFactory.cs) shows the pattern:

```csharp
builder.ConfigureTestServices(services =>
{
    var inMemoryServiceProvider = new ServiceCollection()
        .AddEntityFrameworkInMemoryDatabase()
        .BuildServiceProvider();

    services.RemoveAll<DbContextOptions<AppDbContext>>();
    services.RemoveAll<AppDbContext>();
    services.AddDbContext<AppDbContext>(opts =>
        opts.UseInMemoryDatabase("ProductApiTests")
            .UseInternalServiceProvider(inMemoryServiceProvider));

    services.RemoveAll<IProductRepository>();
    services.AddSingleton<IProductRepository>(ProductRepository);
    ...
});
```

Note the split this reveals: `IProductRepository` is *also* swapped for `FakeProductRepository` right below the `AppDbContext` override, so the in-memory `AppDbContext` isn't actually read through by the handlers under test at all — it exists only so `AppDbContext`'s own DI registration (used elsewhere in `Program.cs`, e.g. for a health check) resolves to something that doesn't require a live SQL Server connection string. The real, behavior-accurate repository testing against SQL Server semantics (query translation, constraints, concurrency) happens once, in Infrastructure.Tests, via `Testcontainers.MsSql` — paying that cost at the API layer for every test class, across every service, would be redundant and slow. This is an inference from reading the code (the fakes fully replace repository access), not a comment found verbatim in the source — noted here as such rather than presented as a stated fact.

### coverlet.collector — code coverage during `dotnet test`

Every test project across every service and every layer references `coverlet.collector` (e.g. `<PackageReference Include="coverlet.collector" Version="6.0.4" />` in [Cart.Domain.Tests.csproj](../../Services/Cart/Cart.Domain.Tests/Cart.Domain.Tests.csproj)). This is a `dotnet test` data collector, not a separate CLI step — it lets `dotnet test --collect:"XPlat Code Coverage"` produce a coverage report for any of these projects with no other configuration. No `.runsettings` file or CI coverage-gate script was found in the repository at the time of writing, so the package is present and ready to use but not yet wired into an enforced threshold.

## Inside-out TDD, per service — the real numbers

[STATUS.md](../STATUS.md) records these test totals ("as of pre-Phase-6 gap fixes"):

| Service | Domain | Application | Infrastructure* | Api | Total |
| --- | --- | --- | --- | --- | --- |
| Identity | 21 | 52 | 16 | 29 | 118 |
| Product | 10 | 44 | 8 | 21 | 83 |
| Cart | 1 | 23 | 6 | 10 | 40 |
| Order | 14 | 25 | 6 | 17 | 62 |
| Notification | 0 | 5 | 5 | 0 | 10 |
| **Total** | | | | | **313** |

\* Infrastructure tests require Docker running (Testcontainers).

The shape confirms the inside-out philosophy in practice: Application layers (the CQRS handlers, where most branching logic lives) consistently carry the largest test count per service, while Infrastructure — gated behind real containers — carries the fewest, since the point is to prove the technology integration works, not to re-test business rules already covered above it. Domain counts track directly with how much (if any) entity logic that service's Domain layer actually has — Cart's Domain.Tests has exactly 1 test because [Cart.Domain has no entities at all](../Architecture/Cart-Service.md#1-cartdomain--exceptions-only), only exceptions.

## Gotchas & deviations

- **Notification has no `Domain` project and no dedicated `Api.Tests` project** — the only two structural exceptions to the four-project-per-service pattern in this stack. `Notification.Infrastructure.Tests` project-references *both* `Notification.Infrastructure` **and** `Notification.Api` (see [Notification.Infrastructure.Tests.csproj](../../Services/Notification/Notification.Infrastructure.Tests/Notification.Infrastructure.Tests.csproj)), and its lone API-level test, [HealthCheckTests.cs](../../Services/Notification/Notification.Infrastructure.Tests/HealthCheckTests.cs), hosts `Notification.Api`'s real `Program.cs` via `WebApplicationFactory<Program>` from inside the Infrastructure.Tests project rather than a sibling Api.Tests project — because Notification has no controllers to test beyond `/health` (it's a pure message consumer, see [13-mailkit-notifications.md](./13-mailkit-notifications.md)), a separate Api.Tests project was apparently judged not worth the scaffolding. This is inferred from the project layout, not stated in any doc read for this page.
- **`WebApplicationFactory` MassTransit-namespace sweep is duplicated, not shared**, across `CartApiFactory`, `HealthCheckTests` (Notification), and equivalent Order/Identity/Product fixtures — each service repeats the same `services.Where(d => d.ServiceType.Namespace?.StartsWith("MassTransit", ...))` block rather than sharing it from a common test-support library. No shared test-infrastructure package exists in this repo at the time of writing.
- **InMemory vs. Testcontainers.MsSql is not an either/or per service** — Identity, Product, and Order use *both*: `Testcontainers.MsSql` in Infrastructure.Tests (real SQL Server, testing the actual `ProductRepository`/`OrderRepository`/etc.), and `EntityFrameworkCore.InMemory` in Api.Tests (a placeholder `AppDbContext` registration, since the real repository interface is separately swapped for an in-memory fake at that layer). Cart never needs either, since it has no SQL Server dependency anywhere.
- **`dotnet test ShopFlow.sln`** runs everything at once, per [RUNNING.md](../RUNNING.md); Infrastructure.Tests projects will fail outright if Docker isn't running, since Testcontainers has no fallback path.
