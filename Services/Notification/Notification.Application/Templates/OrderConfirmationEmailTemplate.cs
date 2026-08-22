using System.Globalization;
using System.Text;
using Notification.Application.Interfaces;

namespace Notification.Application.Templates;

public static class OrderConfirmationEmailTemplate
{
    public static string Subject(Guid orderId) =>
        $"Order Confirmation - {orderId}";

    public static string Body(Guid orderId, List<OrderLineItem> items, decimal total)
    {
        var sb = new StringBuilder();

        sb.AppendLine($"Thank you for your order {orderId}!");
        sb.AppendLine();
        sb.AppendLine("Order summary:");

        foreach (var item in items)
        {
            var lineTotal = item.UnitPrice * item.Quantity;
            sb.AppendLine(
                $"  - {item.ProductName} x{item.Quantity} @ {item.UnitPrice.ToString("F2", CultureInfo.InvariantCulture)} = {lineTotal.ToString("F2", CultureInfo.InvariantCulture)}");
        }

        sb.AppendLine();
        sb.AppendLine($"Total: {total.ToString("F2", CultureInfo.InvariantCulture)}");

        return sb.ToString();
    }
}
