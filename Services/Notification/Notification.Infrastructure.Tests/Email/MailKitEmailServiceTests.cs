using System.Net.Http.Json;
using System.Text.Json;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Notification.Application.Interfaces;
using Notification.Infrastructure.Email;
using Notification.Infrastructure.Settings;

namespace Notification.Infrastructure.Tests.Email;

/// <summary>
/// Integration test against a real SMTP server (smtp4dev, via Testcontainers) rather than a mock
/// of MailKit's SmtpClient — consistent with this project's own NFR-25 philosophy of testing
/// infrastructure against real dependencies (SQL Server, Redis, RabbitMQ all use Testcontainers
/// elsewhere; there is no dedicated Testcontainers module for SMTP, so this uses the generic
/// Testcontainers package directly against the rnwood/smtp4dev:v3 image).
/// </summary>
public class MailKitEmailServiceTests : IAsyncLifetime
{
    private readonly IContainer _smtp4Dev = new ContainerBuilder()
        .WithImage("rnwood/smtp4dev:v3")
        .WithPortBinding(25, true)
        .WithPortBinding(80, true)
        .WithWaitStrategy(Wait.ForUnixContainer()
            .UntilHttpRequestIsSucceeded(r => r.ForPort(80).ForPath("/api/messages")))
        .Build();

    private HttpClient _managementClient = null!;

    public async Task InitializeAsync()
    {
        await _smtp4Dev.StartAsync();
        _managementClient = new HttpClient
        {
            BaseAddress = new Uri($"http://localhost:{_smtp4Dev.GetMappedPublicPort(80)}")
        };
    }

    public async Task DisposeAsync()
    {
        _managementClient.Dispose();
        await _smtp4Dev.DisposeAsync();
    }

    private MailKitEmailService CreateSut() =>
        new(Options.Create(new EmailSettings
        {
            Host = "localhost",
            Port = _smtp4Dev.GetMappedPublicPort(25),
            From = "noreply@shopflow.com",
            Password = "does-not-matter-for-smtp4dev"
        }));

    private async Task<JsonElement> WaitForDeliveredMessageAsync()
    {
        for (var attempt = 0; attempt < 40; attempt++)
        {
            var page = await _managementClient.GetFromJsonAsync<JsonElement>("/api/messages");
            var results = page.GetProperty("results");
            if (results.GetArrayLength() > 0)
            {
                return results[0];
            }

            await Task.Delay(250);
        }

        throw new TimeoutException("smtp4dev did not report a delivered message in time.");
    }

    [Fact]
    public async Task SendOrderConfirmationAsync_ShouldDeliverEmail_WithCorrectRecipientAndSubject()
    {
        var sut = CreateSut();
        var orderId = Guid.NewGuid();
        var items = new List<OrderLineItem> { new("Widget", 10.00m, 2) };

        await sut.SendOrderConfirmationAsync("customer@example.com", orderId, items, 20.00m, default);

        var message = await WaitForDeliveredMessageAsync();

        message.GetProperty("from").GetString().Should().Be("noreply@shopflow.com");
        message.GetProperty("to").EnumerateArray().Select(x => x.GetString())
            .Should().Contain("customer@example.com");
        message.GetProperty("subject").GetString().Should().Contain(orderId.ToString());
    }

    [Fact]
    public async Task SendOrderConfirmationAsync_ShouldDeliverEmail_WithBodyContainingItemsAndTotal()
    {
        var sut = CreateSut();
        var orderId = Guid.NewGuid();
        var items = new List<OrderLineItem> { new("Gadget", 25.00m, 1) };

        await sut.SendOrderConfirmationAsync("customer2@example.com", orderId, items, 25.00m, default);

        var message = await WaitForDeliveredMessageAsync();
        var id = message.GetProperty("id").GetString();

        var plainTextBody = await _managementClient.GetStringAsync($"/api/messages/{id}/plaintext");

        plainTextBody.Should().Contain("Gadget");
        plainTextBody.Should().Contain("25.00");
    }
}
