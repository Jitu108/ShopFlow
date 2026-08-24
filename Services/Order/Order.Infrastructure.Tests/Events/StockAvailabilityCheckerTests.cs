using FluentAssertions;
using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using Order.Domain.Entities;
using Order.Infrastructure.Events;
using ShopFlow.Shared.Events;

namespace Order.Infrastructure.Tests.Events;

public class StockAvailabilityCheckerTests
{
    [Fact]
    public async Task CheckAsync_WhenResponseIndicatesAvailable_ShouldReturnAvailableResult()
    {
        await using var provider = new ServiceCollection()
            .AddMassTransitTestHarness(cfg =>
            {
                cfg.AddHandler<CheckStockRequest>(async context =>
                    await context.RespondAsync(new CheckStockResponse(true, [])));
                cfg.AddRequestClient<CheckStockRequest>();
            })
            .BuildServiceProvider(true);

        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        using var scope = provider.CreateScope();
        var requestClient = scope.ServiceProvider.GetRequiredService<IRequestClient<CheckStockRequest>>();
        var checker = new StockAvailabilityChecker(requestClient);

        var items = new List<OrderItemEntity> { OrderItemEntity.Create(Guid.NewGuid(), "Widget", 9.99m, 2) };

        var result = await checker.CheckAsync(items, default);

        result.IsAvailable.Should().BeTrue();
        result.InsufficientProductIds.Should().BeEmpty();
    }

    [Fact]
    public async Task CheckAsync_WhenResponseIndicatesUnavailable_ShouldReturnInsufficientProductIds()
    {
        var insufficientProductId = Guid.NewGuid();

        await using var provider = new ServiceCollection()
            .AddMassTransitTestHarness(cfg =>
            {
                cfg.AddHandler<CheckStockRequest>(async context =>
                    await context.RespondAsync(new CheckStockResponse(false, [insufficientProductId])));
                cfg.AddRequestClient<CheckStockRequest>();
            })
            .BuildServiceProvider(true);

        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        using var scope = provider.CreateScope();
        var requestClient = scope.ServiceProvider.GetRequiredService<IRequestClient<CheckStockRequest>>();
        var checker = new StockAvailabilityChecker(requestClient);

        var items = new List<OrderItemEntity> { OrderItemEntity.Create(insufficientProductId, "Widget", 9.99m, 50) };

        var result = await checker.CheckAsync(items, default);

        result.IsAvailable.Should().BeFalse();
        result.InsufficientProductIds.Should().Contain(insufficientProductId);
    }
}
