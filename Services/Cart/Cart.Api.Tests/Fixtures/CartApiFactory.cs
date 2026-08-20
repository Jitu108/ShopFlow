using Cart.Application.Interfaces;
using Cart.Infrastructure.Events;
using Cart.Infrastructure.Settings;
using MassTransit;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Cart.Api.Tests.Fixtures;

public class CartApiFactory : WebApplicationFactory<Program>
{
    public FakeCartRepository CartRepository { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{JwtSettings.SectionName}:Secret"] = JwtTokenHelper.TestSecret,
                [$"{JwtSettings.SectionName}:Issuer"] = JwtTokenHelper.TestIssuer,
                [$"{JwtSettings.SectionName}:Audience"] = JwtTokenHelper.TestAudience,
                ["ConnectionStrings:Redis"] = "localhost:6379"
            });
        });

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ICartRepository>();
            services.AddSingleton<ICartRepository>(CartRepository);

            // Program.cs wires AddMassTransit(...).UsingRabbitMq(...), which would otherwise try
            // to reach a real broker when the test host starts. Swap every MassTransit-registered
            // service for the in-memory test transport so API tests never touch a network broker.
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
        });
    }
}
