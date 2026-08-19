using System.Text;
using FluentValidation;
using Microsoft.OpenApi;
using Microsoft.Extensions.Options;
using Identity.Application.Behaviors;
using Identity.Application.Commands;
using Identity.Application.Interfaces;
using Identity.Application.Validators;
using Identity.Api.Middleware;
using Identity.Domain.Entities;
using Identity.Domain.Enums;
using Identity.Infrastructure.Jwt;
using Identity.Infrastructure.Persistence;
using Identity.Infrastructure.Persistence.Repositories;
using Identity.Infrastructure.Settings;
using MediatR;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
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
builder.Services.AddScoped<IPasswordHasher<ApplicationUser>, PasswordHasher<ApplicationUser>>();

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
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(opts =>
{
    opts.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name         = "Authorization",
        Type         = SecuritySchemeType.Http,
        Scheme       = "Bearer",
        BearerFormat = "JWT",
        In           = ParameterLocation.Header,
        Description  = "Enter your JWT token. Example: eyJhbG..."
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
    var sp  = scope.ServiceProvider;
    var db  = sp.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();

    var cfg     = app.Configuration.GetSection("AdminSeed");
    var email   = cfg["Email"]!;
    var password = cfg["Password"]!;
    var displayName = cfg["DisplayName"]!;

    if (!db.Users.Any(u => u.Email == email))
    {
        var hasher = sp.GetRequiredService<IPasswordHasher<ApplicationUser>>();
        var admin  = ApplicationUser.Create(email, displayName);
        admin.AssignRole(UserRole.Admin);
        admin.SetPasswordHash(hasher.HashPassword(admin, password));
        db.Users.Add(admin);
        db.SaveChanges();
        app.Logger.LogInformation("Admin account seeded: {Email}", email);
    }
}

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