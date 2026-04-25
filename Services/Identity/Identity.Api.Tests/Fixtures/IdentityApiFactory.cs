using Identity.Application.Interfaces;
using Identity.Infrastructure.Persistence;
using Identity.Infrastructure.Settings;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Identity.Api.Tests.Fixtures;

public class IdentityApiFactory : WebApplicationFactory<Program>
{
    public FakeUserRepository         UserRepository         { get; } = new();
    public FakeRefreshTokenRepository RefreshTokenRepository { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{JwtSettings.SectionName}:Secret"]        = JwtTokenHelper.TestSecret,
                [$"{JwtSettings.SectionName}:Issuer"]        = JwtTokenHelper.TestIssuer,
                [$"{JwtSettings.SectionName}:Audience"]      = JwtTokenHelper.TestAudience,
                [$"{JwtSettings.SectionName}:ExpiryMinutes"] = "60",
                ["ConnectionStrings:Default"]                 = "Server=.;Database=TestDb;"
            });
        });

        // ConfigureTestServices runs AFTER the app's DI registrations — overrides take effect
        builder.ConfigureTestServices(services =>
        {
            // Replace DbContext with in-memory
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.RemoveAll<AppDbContext>();
            services.AddDbContext<AppDbContext>(opts =>
                opts.UseInMemoryDatabase("IdentityApiTests"));

            // Replace repositories with fakes
            services.RemoveAll<IUserRepository>();
            services.AddSingleton<IUserRepository>(UserRepository);

            services.RemoveAll<IRefreshTokenRepository>();
            services.AddSingleton<IRefreshTokenRepository>(RefreshTokenRepository);
        });
    }
}