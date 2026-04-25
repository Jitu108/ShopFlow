using System.Text;
using FluentValidation;
using Microsoft.Extensions.Options;
using Identity.Application.Behaviors;
using Identity.Application.Commands;
using Identity.Application.Interfaces;
using Identity.Application.Validators;
using Identity.Api.Middleware;
using Identity.Infrastructure.Jwt;
using Identity.Infrastructure.Persistence;
using Identity.Infrastructure.Persistence.Repositories;
using Identity.Infrastructure.Settings;
using MediatR;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// ── Settings ─────────────────────────────────────────────────────────────────

builder.Services.Configure<JwtSettings>(
    builder.Configuration.GetSection(JwtSettings.SectionName));

// ── Database ─────────────────────────────────────────────────────────────────

builder.Services.AddDbContext<AppDbContext>(opts =>
    opts.UseSqlServer(builder.Configuration.GetConnectionString("Default")));

// ── Repositories & Services ───────────────────────────────────────────────────

builder.Services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
builder.Services.AddScoped<IUserRepository,         UserRepository>();
builder.Services.AddScoped<ITokenService,           TokenService>();

// ── MediatR ───────────────────────────────────────────────────────────────────

builder.Services.AddMediatR(cfg =>
    cfg.RegisterServicesFromAssembly(typeof(RegisterUserCommand).Assembly));

// ── FluentValidation ─────────────────────────────────────────────────────────

builder.Services.AddValidatorsFromAssembly(typeof(RegisterUserCommandValidator).Assembly);
builder.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));

// ── Authentication & Authorisation ────────────────────────────────────────────

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer();

// Configure JWT options lazily so WebApplicationFactory config overrides are respected
builder.Services
    .AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IOptions<JwtSettings>>((jwtOpts, settings) =>
    {
        var s = settings.Value;
        jwtOpts.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer           = true,
            ValidIssuer              = s.Issuer,
            ValidateAudience         = true,
            ValidAudience            = s.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey         = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(s.Secret)),
            ValidateLifetime         = true,
            ClockSkew                = TimeSpan.Zero
        };
    });

builder.Services.AddAuthorization(opts =>
{
    opts.AddPolicy("RequireVendor",        p => p.RequireRole("Vendor"));
    opts.AddPolicy("RequireAdmin",         p => p.RequireRole("Admin"));
    opts.AddPolicy("RequireVerifiedEmail", p => p.RequireClaim("emailVerified", "true"));
});

// ── Health Checks ─────────────────────────────────────────────────────────────

builder.Services.AddHealthChecks()
    .AddSqlServer(builder.Configuration.GetConnectionString("Default") ?? string.Empty);

// ── Controllers & OpenAPI ─────────────────────────────────────────────────────

builder.Services.AddControllers();
builder.Services.AddOpenApi();

// ─────────────────────────────────────────────────────────────────────────────

var app = builder.Build();

app.UseMiddleware<ExceptionHandlingMiddleware>();

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHealthChecks("/health");

app.Run();

public partial class Program { }