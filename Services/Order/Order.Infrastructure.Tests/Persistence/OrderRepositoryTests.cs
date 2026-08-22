using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Order.Domain.Entities;
using Order.Infrastructure.Persistence;
using Order.Infrastructure.Persistence.Repositories;
using Testcontainers.MsSql;

namespace Order.Infrastructure.Tests.Persistence;

public class OrderRepositoryTests : IAsyncLifetime
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

    private static OrderEntity ValidOrder(Guid customerId) => OrderEntity.Create(
        customerId, "customer@example.com",
        [
            OrderItemEntity.Create(Guid.NewGuid(), "Widget", 10.00m, 2),
            OrderItemEntity.Create(Guid.NewGuid(), "Gadget", 25.00m, 1)
        ]);

    [Fact]
    public async Task AddAsync_ThenGetById_ShouldReturnOrder_WithItems()
    {
        var repo = new OrderRepository(CreateContext());
        var order = ValidOrder(Guid.NewGuid());

        await repo.AddAsync(order, default);
        var found = await repo.GetByIdAsync(order.Id, default);

        found.Should().NotBeNull();
        found!.CustomerEmail.Should().Be("customer@example.com");
        found.OrderItems.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetByIdAsync_WithUnknownId_ShouldReturnNull()
    {
        var repo = new OrderRepository(CreateContext());

        var found = await repo.GetByIdAsync(Guid.NewGuid(), default);

        found.Should().BeNull();
    }

    [Fact]
    public async Task GetByCustomerIdAsync_ShouldReturn_OnlyThatCustomersOrders()
    {
        var repo = new OrderRepository(CreateContext());
        var customerId = Guid.NewGuid();
        var ownOrder = ValidOrder(customerId);
        var otherOrder = ValidOrder(Guid.NewGuid());

        await repo.AddAsync(ownOrder, default);
        await repo.AddAsync(otherOrder, default);

        var result = await repo.GetByCustomerIdAsync(customerId, default);

        result.Should().ContainSingle(x => x.Id == ownOrder.Id);
    }

    [Fact]
    public async Task GetAllAsync_ShouldReturn_EveryOrder()
    {
        var repo = new OrderRepository(CreateContext());
        await repo.AddAsync(ValidOrder(Guid.NewGuid()), default);
        await repo.AddAsync(ValidOrder(Guid.NewGuid()), default);

        var result = await repo.GetAllAsync(default);

        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task UpdateAsync_ShouldPersist_StatusChange()
    {
        var repo = new OrderRepository(CreateContext());
        var order = ValidOrder(Guid.NewGuid());
        await repo.AddAsync(order, default);

        order.Confirm();
        await repo.UpdateAsync(order, default);

        var found = await repo.GetByIdAsync(order.Id, default);
        found!.Status.Should().Be(order.Status);
    }
}
