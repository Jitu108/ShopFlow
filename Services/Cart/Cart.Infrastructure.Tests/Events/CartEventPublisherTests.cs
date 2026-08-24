using Cart.Infrastructure.Events;
using FluentAssertions;
using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using ShopFlow.Shared.Events;

namespace Cart.Infrastructure.Tests.Events;

public class CartEventPublisherTests
{
    [Fact]
    public async Task PublishStockAdjustedAsync_ShouldPublish_CartStockAdjustedEvent_WithGivenDelta()
    {
        await using var provider = new ServiceCollection()
            .AddMassTransitTestHarness()
            .BuildServiceProvider(true);

        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        var productId = Guid.NewGuid();
        var publisher = new CartEventPublisher(harness.Bus);
        await publisher.PublishStockAdjustedAsync(productId, 3, default);

        (await harness.Published.Any<CartStockAdjustedEvent>(x =>
            x.Context.Message.ProductId == productId &&
            x.Context.Message.QuantityDelta == 3)).Should().BeTrue();
    }
}
