using Cart.Application.Interfaces;
using Cart.Infrastructure.Events;
using FluentAssertions;
using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using ShopFlow.Shared.Events;

namespace Cart.Infrastructure.Tests.Events;

public class OrderPlacedConsumerTests
{
    [Fact]
    public async Task Consume_ShouldClearCart_ForEventCustomerId()
    {
        var cartRepository = Substitute.For<ICartRepository>();

        await using var provider = new ServiceCollection()
            .AddSingleton(cartRepository)
            .AddMassTransitTestHarness(cfg =>
            {
                cfg.AddConsumer<OrderPlacedConsumer>();
            })
            .BuildServiceProvider(true);

        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        var customerId = Guid.NewGuid();
        await harness.Bus.Publish(new OrderPlacedEvent(
            Guid.NewGuid(), customerId, "user@test.com", [], 45m, DateTime.UtcNow));

        (await harness.Consumed.Any<OrderPlacedEvent>()).Should().BeTrue();
        await cartRepository.Received(1).ClearCartAsync(customerId, Arg.Any<CancellationToken>());
    }
}
