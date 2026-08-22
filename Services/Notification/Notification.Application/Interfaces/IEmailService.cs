namespace Notification.Application.Interfaces;

public record OrderLineItem(string ProductName, decimal UnitPrice, int Quantity);

public interface IEmailService
{
    Task SendOrderConfirmationAsync(
        string toEmail,
        Guid orderId,
        List<OrderLineItem> items,
        decimal total,
        CancellationToken ct);
}
