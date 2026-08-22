using MassTransit;
using Notification.Application.Interfaces;
using ShopFlow.Shared.Events;

namespace Notification.Infrastructure.Events;

public class OrderPlacedConsumer : IConsumer<OrderPlacedEvent>
{
    private readonly IEmailService _emailService;

    public OrderPlacedConsumer(IEmailService emailService)
    {
        _emailService = emailService;
    }

    public async Task Consume(ConsumeContext<OrderPlacedEvent> context)
    {
        var message = context.Message;

        var items = message.Items
            .Select(i => new OrderLineItem(i.ProductName, i.UnitPrice, i.Quantity))
            .ToList();

        await _emailService.SendOrderConfirmationAsync(
            message.CustomerEmail,
            message.OrderId,
            items,
            message.Total,
            context.CancellationToken);
    }
}
