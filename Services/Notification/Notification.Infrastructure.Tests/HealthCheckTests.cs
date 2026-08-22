using System.Net;
using FluentAssertions;
using MassTransit;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Notification.Infrastructure.Events;

namespace Notification.Infrastructure.Tests;

/// <summary>
/// Cheap insurance that Notification.Api's Program.cs actually boots as a Kestrel-hosted app
/// and serves /health — there are no controllers in this service to exercise beyond that.
/// </summary>
public class HealthCheckTests
{
    [Fact]
    public async Task GetHealth_ShouldReturn200_WhenAppBoots()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["SMTP_HOST"] = "localhost",
                    ["SMTP_PORT"] = "25",
                    ["SMTP_FROM"] = "noreply@shopflow.com",
                    ["SMTP_PASSWORD"] = string.Empty
                });
            });

            builder.ConfigureTestServices(services =>
            {
                // Program.cs wires AddMassTransit(...).UsingRabbitMq(...) plus a RabbitMQ health
                // check, both of which would otherwise try to reach a real broker when the test
                // host starts. Swap MassTransit for the in-memory test transport (mirrors Cart's
                // CartApiFactory) and clear health check registrations so this stays a cheap,
                // network-free boot check — the real broker/health wiring is exercised via the
                // live Docker Compose round trip, not here.
                var massTransitDescriptors = services
                    .Where(d =>
                        (d.ServiceType.Namespace?.StartsWith("MassTransit", StringComparison.Ordinal) ?? false) ||
                        (d.ImplementationType?.Namespace?.StartsWith("MassTransit", StringComparison.Ordinal) ?? false))
                    .ToList();

                foreach (var descriptor in massTransitDescriptors)
                {
                    services.Remove(descriptor);
                }

                services.AddMassTransitTestHarness(cfg =>
                {
                    cfg.AddConsumer<OrderPlacedConsumer>();
                });

                services.Configure<HealthCheckServiceOptions>(options => options.Registrations.Clear());
            });
        });

        using var client = factory.CreateClient();
        var response = await client.GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
