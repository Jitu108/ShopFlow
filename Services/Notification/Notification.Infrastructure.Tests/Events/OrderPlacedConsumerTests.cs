using FluentAssertions;
using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Notification.Application.Interfaces;
using Notification.Infrastructure.Events;
using ShopFlow.Shared.Events;

namespace Notification.Infrastructure.Tests.Events;

public class OrderPlacedConsumerTests
{
    [Fact]
    public async Task Consume_ShouldSendOrderConfirmationEmail_WithMappedItems()
    {
        var emailService = Substitute.For<IEmailService>();

        await using var provider = new ServiceCollection()
            .AddSingleton(emailService)
            .AddMassTransitTestHarness(cfg =>
            {
                cfg.AddConsumer<OrderPlacedConsumer>();
            })
            .BuildServiceProvider(true);

        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        var orderId = Guid.NewGuid();
        var customerId = Guid.NewGuid();

        var sourceItems = new List<OrderItemDto>
        {
            new(Guid.NewGuid(), "Widget", 10.00m, 2),
            new(Guid.NewGuid(), "Gadget", 25.00m, 1)
        };

        await harness.Bus.Publish(new OrderPlacedEvent(
            orderId, customerId, "user@test.com", sourceItems, 45m, DateTime.UtcNow));

        (await harness.Consumed.Any<OrderPlacedEvent>()).Should().BeTrue();

        var expectedItems = new List<OrderLineItem>
        {
            new("Widget", 10.00m, 2),
            new("Gadget", 25.00m, 1)
        };

        await emailService.Received(1).SendOrderConfirmationAsync(
            "user@test.com",
            orderId,
            Arg.Is<List<OrderLineItem>>(items => items.SequenceEqual(expectedItems)),
            45m,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Consume_WithNoItems_ShouldSendEmailWithEmptyItemsListAndZeroTotal()
    {
        var emailService = Substitute.For<IEmailService>();

        await using var provider = new ServiceCollection()
            .AddSingleton(emailService)
            .AddMassTransitTestHarness(cfg =>
            {
                cfg.AddConsumer<OrderPlacedConsumer>();
            })
            .BuildServiceProvider(true);

        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        var orderId = Guid.NewGuid();
        var customerId = Guid.NewGuid();

        await harness.Bus.Publish(new OrderPlacedEvent(
            orderId, customerId, "empty@test.com", [], 0m, DateTime.UtcNow));

        (await harness.Consumed.Any<OrderPlacedEvent>()).Should().BeTrue();

        await emailService.Received(1).SendOrderConfirmationAsync(
            "empty@test.com",
            orderId,
            Arg.Is<List<OrderLineItem>>(items => items.Count == 0),
            0m,
            Arg.Any<CancellationToken>());
    }
}
