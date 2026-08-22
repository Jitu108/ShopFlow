using MassTransit;
using Microsoft.Extensions.Options;
using Notification.Application.Interfaces;
using Notification.Infrastructure.Email;
using Notification.Infrastructure.Events;
using Notification.Infrastructure.Settings;
using RabbitMQ.Client;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// ── Logging ──────────────────────────────────────────────────────────────────

builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .WriteTo.Console());

// ── Settings ─────────────────────────────────────────────────────────────────

// Bound directly from the flat SMTP_HOST/SMTP_PORT/SMTP_FROM/SMTP_PASSWORD keys that
// .env.example already provisions for this service, rather than a nested config section.
// EmailSettings uses init-only properties (mirroring JwtSettings' shape), so it's built once
// via an object initializer and registered as IOptions<EmailSettings> directly.
builder.Services.AddSingleton(Options.Create(new EmailSettings
{
    Host = builder.Configuration["SMTP_HOST"] ?? "localhost",
    Port = int.TryParse(builder.Configuration["SMTP_PORT"], out var smtpPort) ? smtpPort : 25,
    From = builder.Configuration["SMTP_FROM"] ?? "noreply@shopflow.com",
    Password = builder.Configuration["SMTP_PASSWORD"] ?? string.Empty
}));

// ── Email ────────────────────────────────────────────────────────────────────

builder.Services.AddScoped<IEmailService, MailKitEmailService>();

// ── MassTransit / RabbitMQ ─────────────────────────────────────────────────────

builder.Services.AddMassTransit(x =>
{
    x.AddConsumer<OrderPlacedConsumer>();
    x.UsingRabbitMq((ctx, cfg) =>
    {
        cfg.Host(builder.Configuration["RabbitMQ:Host"] ?? "localhost", "/", h =>
        {
            h.Username(builder.Configuration["RabbitMQ:User"] ?? "guest");
            h.Password(builder.Configuration["RabbitMQ:Pass"] ?? "guest");
        });

        // Deliberately distinct from Cart's "order-placed-queue": Cart already binds a consumer
        // to that exact queue name, so reusing it here would make this a second *competing*
        // consumer on Cart's own queue (round-robin delivery) instead of an independent
        // subscriber — messages would only reach one service or the other, about half each,
        // silently breaking Cart's cart-clearing. Two distinct queues bound to the same
        // OrderPlacedEvent exchange give both services every message (fan-out).
        cfg.ReceiveEndpoint("notification-order-placed-queue", e =>
        {
            e.ConfigureConsumer<OrderPlacedConsumer>(ctx);
            e.UseMessageRetry(r => r.Exponential(3,
                TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(2)));
        });
    });
});

// ── Health Checks ─────────────────────────────────────────────────────────────

builder.Services.AddHealthChecks()
    .AddRabbitMQ(sp =>
    {
        var factory = new ConnectionFactory
        {
            HostName = builder.Configuration["RabbitMQ:Host"] ?? "localhost",
            UserName = builder.Configuration["RabbitMQ:User"] ?? "guest",
            Password = builder.Configuration["RabbitMQ:Pass"] ?? "guest"
        };
        return factory.CreateConnectionAsync();
    });

// ─────────────────────────────────────────────────────────────────────────────

var app = builder.Build();

app.UseSerilogRequestLogging();

app.MapHealthChecks("/health");

app.Run();

public partial class Program { }
