using MassTransit;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Order.Application.Interfaces;
using Order.Infrastructure.Persistence;
using Order.Infrastructure.Settings;
using ShopFlow.Shared.Events;

namespace Order.Api.Tests.Fixtures;

public class OrderApiFactory : WebApplicationFactory<Program>
{
    public FakeOrderRepository OrderRepository { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{JwtSettings.SectionName}:Secret"] = JwtTokenHelper.TestSecret,
                [$"{JwtSettings.SectionName}:Issuer"] = JwtTokenHelper.TestIssuer,
                [$"{JwtSettings.SectionName}:Audience"] = JwtTokenHelper.TestAudience,
                ["ConnectionStrings:Default"] = "Server=.;Database=TestDb;"
            });
        });

        builder.ConfigureTestServices(services =>
        {
            var inMemoryServiceProvider = new ServiceCollection()
                .AddEntityFrameworkInMemoryDatabase()
                .BuildServiceProvider();

            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.RemoveAll<AppDbContext>();
            services.AddDbContext<AppDbContext>(opts =>
                opts.UseInMemoryDatabase("OrderApiTests")
                    .UseInternalServiceProvider(inMemoryServiceProvider));

            services.RemoveAll<IOrderRepository>();
            services.AddSingleton<IOrderRepository>(OrderRepository);

            // Program.cs wires AddMassTransit(...).UsingRabbitMq(...), which would otherwise try
            // to reach a real broker when the test host starts. Swap every MassTransit-registered
            // service for the in-memory test transport so API tests never touch a network broker.
            // IOrderEventPublisher stays wired to the real OrderEventPublisher, and
            // IStockAvailabilityChecker to the real StockAvailabilityChecker — both resolve
            // their MassTransit dependencies from the test harness instead of a real connection.
            // The harness answers every CheckStockRequest as available so Confirm tests don't
            // need Product's real CheckStockConsumer; insufficient-stock handling is covered by
            // ConfirmOrderCommandHandlerTests at the application layer.
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
                cfg.AddHandler<CheckStockRequest>(async context =>
                    await context.RespondAsync(new CheckStockResponse(true, [])));
                cfg.AddRequestClient<CheckStockRequest>();
            });
        });
    }
}
