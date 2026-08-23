using System.Text;
using Gateway.Api.Settings;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Ocelot.DependencyInjection;
using Ocelot.Middleware;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// ── Logging ──────────────────────────────────────────────────────────────────

builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .WriteTo.Console());

// ── Settings ─────────────────────────────────────────────────────────────────

builder.Services.Configure<JwtSettings>(
    builder.Configuration.GetSection(JwtSettings.SectionName));

builder.Configuration.AddJsonFile("ocelot.json", optional: false, reloadOnChange: true);

// ── Authentication ────────────────────────────────────────────────────────────

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer();

// Configure JWT options lazily so config overrides (e.g. from environment variables) are respected
builder.Services
    .AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IOptions<JwtSettings>>((jwtOpts, settings) =>
    {
        var s = settings.Value;
        jwtOpts.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = s.Issuer,
            ValidateAudience = true,
            ValidAudience = s.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(s.Secret)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero
        };
    });

// ── Health Checks ─────────────────────────────────────────────────────────────

builder.Services.AddHealthChecks();

// ── Ocelot ────────────────────────────────────────────────────────────────────

builder.Services.AddOcelot(builder.Configuration);

// ─────────────────────────────────────────────────────────────────────────────

var app = builder.Build();

app.UseSerilogRequestLogging();

app.UseAuthentication();

// UseHealthChecks (not MapHealthChecks) so this runs inline, ahead of UseOcelot's
// terminal middleware — endpoint-routed Map* calls are dispatched too late in the
// pipeline to ever be reached once Ocelot has taken over the request.
app.UseHealthChecks("/health");

// Ocelot's rate limiter identifies "the client" purely via a pre-shared ClientId
// header (an API-key model) — it has no IP fallback and 503s any request missing
// one. This system has no API-key concept anywhere else, so stamp the caller's
// remote IP in as the ClientId when the caller hasn't supplied one, giving
// NFR-28's "100 requests/minute per client" an IP-based meaning instead.
app.Use(async (context, next) =>
{
    if (!context.Request.Headers.ContainsKey("ClientId"))
    {
        context.Request.Headers["ClientId"] = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }
    await next();
});

await app.UseOcelot();

app.Run();

public partial class Program { }
