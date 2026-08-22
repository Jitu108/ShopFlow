using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;
using Notification.Application.Interfaces;
using Notification.Application.Templates;
using Notification.Infrastructure.Settings;

namespace Notification.Infrastructure.Email;

public class MailKitEmailService : IEmailService
{
    private readonly EmailSettings _settings;

    public MailKitEmailService(IOptions<EmailSettings> settings)
    {
        _settings = settings.Value;
    }

    public async Task SendOrderConfirmationAsync(
        string toEmail,
        Guid orderId,
        List<OrderLineItem> items,
        decimal total,
        CancellationToken ct)
    {
        var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse(_settings.From));
        message.To.Add(MailboxAddress.Parse(toEmail));
        message.Subject = OrderConfirmationEmailTemplate.Subject(orderId);
        message.Body = new TextPart("plain")
        {
            Text = OrderConfirmationEmailTemplate.Body(orderId, items, total)
        };

        using var client = new SmtpClient();
        await client.ConnectAsync(_settings.Host, _settings.Port, SecureSocketOptions.Auto, ct);
        await client.AuthenticateAsync(_settings.From, _settings.Password, ct);
        await client.SendAsync(message, ct);
        await client.DisconnectAsync(true, ct);
    }
}
