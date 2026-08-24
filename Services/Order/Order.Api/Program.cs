using System.Text;
using FluentValidation;
using MassTransit;
using MediatR;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using Microsoft.Extensions.Options;
using Order.Api.Middleware;
using Order.Application.Behaviors;
using Order.Application.Commands;
using Order.Application.Interfaces;
using Order.Application.Validators;
using Order.Infrastructure.Events;
using Order.Infrastructure.Persistence;
using Order.Infrastructure.Persistence.Repositories;
using Order.Infrastructure.Settings;
using RabbitMQ.Client;
using Serilog;
using ShopFlow.Shared.Events;

var builder = WebApplication.CreateBuilder(args);

// ── Logging ──────────────────────────────────────────────────────────────────

builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .WriteTo.Console());

// ── Settings ─────────────────────────────────────────────────────────────────

builder.Services.Configure<JwtSettings>(
    builder.Configuration.GetSection(JwtSettings.SectionName));

// ── Database ─────────────────────────────────────────────────────────────────

builder.Services.AddDbContext<AppDbContext>(opts =>
    opts.UseSqlServer(builder.Configuration.GetConnectionString("Default")));

// ── Repositories & Services ───────────────────────────────────────────────────

builder.Services.AddScoped<IOrderRepository, OrderRepository>();
builder.Services.AddScoped<IOrderEventPublisher, OrderEventPublisher>();
builder.Services.AddScoped<IStockAvailabilityChecker, StockAvailabilityChecker>();

// ── MediatR ───────────────────────────────────────────────────────────────────

builder.Services.AddMediatR(cfg =>
    cfg.RegisterServicesFromAssembly(typeof(PlaceOrderCommand).Assembly));

// ── FluentValidation ─────────────────────────────────────────────────────────

builder.Services.AddValidatorsFromAssembly(typeof(PlaceOrderCommandValidator).Assembly);
builder.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
builder.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));

// ── MassTransit / RabbitMQ ─────────────────────────────────────────────────────

builder.Services.AddMassTransit(x =>
{
    x.AddRequestClient<CheckStockRequest>(TimeSpan.FromSeconds(10));
    x.UsingRabbitMq((ctx, cfg) =>
    {
        cfg.Host(builder.Configuration["RabbitMQ:Host"] ?? "localhost", "/", h =>
        {
            h.Username(builder.Configuration["RabbitMQ:User"] ?? "guest");
            h.Password(builder.Configuration["RabbitMQ:Pass"] ?? "guest");
        });
    });
});

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

builder.Services.AddAuthorization(opts =>
{
    opts.AddPolicy("RequireVerifiedEmail", p => p.RequireClaim("emailVerified", "true"));
    opts.AddPolicy("RequireAdmin", p => p.RequireRole("Admin"));
});

// ── Health Checks ─────────────────────────────────────────────────────────────

builder.Services.AddHealthChecks()
    .AddSqlServer(builder.Configuration.GetConnectionString("Default") ?? string.Empty)
    .AddRabbitMQ(_ =>
    {
        var factory = new ConnectionFactory
        {
            HostName = builder.Configuration["RabbitMQ:Host"] ?? "localhost",
            UserName = builder.Configuration["RabbitMQ:User"] ?? "guest",
            Password = builder.Configuration["RabbitMQ:Pass"] ?? "guest"
        };
        return factory.CreateConnectionAsync();
    });

// ── Controllers & OpenAPI ─────────────────────────────────────────────────────

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(opts =>
{
    opts.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "Bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Enter your JWT token. Example: eyJhbG..."
    });
    opts.AddSecurityRequirement(doc =>
    {
        var requirement = new OpenApiSecurityRequirement();
        requirement.Add(new OpenApiSecuritySchemeReference("Bearer", doc), new List<string>());
        return requirement;
    });
});
builder.Services.AddOpenApi();

// ─────────────────────────────────────────────────────────────────────────────

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
}

app.UseSerilogRequestLogging();

app.UseMiddleware<ExceptionHandlingMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHealthChecks("/health");

app.Run();

public partial class Program { }
