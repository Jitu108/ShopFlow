using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Product.Application.Interfaces;
using Product.Infrastructure.Persistence;
using Product.Infrastructure.Settings;

namespace Product.Api.Tests.Fixtures;

public class ProductApiFactory : WebApplicationFactory<Program>
{
    public FakeProductRepository ProductRepository { get; } = new();
    public FakeCacheService CacheService { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{JwtSettings.SectionName}:Secret"] = JwtTokenHelper.TestSecret,
                [$"{JwtSettings.SectionName}:Issuer"] = JwtTokenHelper.TestIssuer,
                [$"{JwtSettings.SectionName}:Audience"] = JwtTokenHelper.TestAudience,
                ["ConnectionStrings:Default"] = "Server=.;Database=TestDb;",
                ["ConnectionStrings:Redis"] = "localhost:6379"
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
                opts.UseInMemoryDatabase("ProductApiTests")
                    .UseInternalServiceProvider(inMemoryServiceProvider));

            services.RemoveAll<IProductRepository>();
            services.AddSingleton<IProductRepository>(ProductRepository);

            services.RemoveAll<ICacheService>();
            services.AddSingleton<ICacheService>(CacheService);
        });
    }
}
