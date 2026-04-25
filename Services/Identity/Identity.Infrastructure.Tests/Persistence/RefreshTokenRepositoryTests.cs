using FluentAssertions;
using Identity.Domain.Entities;
using Identity.Infrastructure.Persistence;
using Identity.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Testcontainers.MsSql;

namespace Identity.Infrastructure.Tests.Persistence;

public class RefreshTokenRepositoryTests : IAsyncLifetime
{
    private readonly MsSqlContainer _sql = new MsSqlBuilder().Build();

    public async Task InitializeAsync() => await _sql.StartAsync();
    public async Task DisposeAsync()    => await _sql.DisposeAsync();

    private AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(_sql.GetConnectionString())
            .Options;
        var ctx = new AppDbContext(options);
        ctx.Database.EnsureCreated();
        return ctx;
    }

    [Fact]
    public async Task SaveAsync_ThenGetByToken_ShouldReturnToken()
    {
        var repo  = new RefreshTokenRepository(CreateContext());
        var token = RefreshToken.Create(Guid.NewGuid(), DateTime.UtcNow.AddDays(7));

        await repo.SaveAsync(token, default);
        var found = await repo.GetByTokenAsync(token.Token, default);

        found.Should().NotBeNull();
        found!.Token.Should().Be(token.Token);
    }

    [Fact]
    public async Task SaveAsync_ShouldPersist_CorrectUserId()
    {
        var userId = Guid.NewGuid();
        var repo   = new RefreshTokenRepository(CreateContext());
        var token  = RefreshToken.Create(userId, DateTime.UtcNow.AddDays(7));

        await repo.SaveAsync(token, default);
        var found = await repo.GetByTokenAsync(token.Token, default);

        found!.UserId.Should().Be(userId);
    }

    [Fact]
    public async Task SaveAsync_ShouldPersist_ExpiresAt()
    {
        var expiresAt = DateTime.UtcNow.AddDays(7).TruncateToSeconds();
        var repo      = new RefreshTokenRepository(CreateContext());
        var token     = RefreshToken.Create(Guid.NewGuid(), expiresAt);

        await repo.SaveAsync(token, default);
        var found = await repo.GetByTokenAsync(token.Token, default);

        found!.ExpiresAt.TruncateToSeconds().Should().Be(expiresAt);
    }

    [Fact]
    public async Task GetByTokenAsync_WithUnknownToken_ShouldReturnNull()
    {
        var repo  = new RefreshTokenRepository(CreateContext());

        var found = await repo.GetByTokenAsync("unknown-token", default);

        found.Should().BeNull();
    }

    [Fact]
    public async Task RevokeAsync_ThenGetByToken_ShouldReturnNull()
    {
        var repo  = new RefreshTokenRepository(CreateContext());
        var token = RefreshToken.Create(Guid.NewGuid(), DateTime.UtcNow.AddDays(7));

        await repo.SaveAsync(token, default);
        await repo.RevokeAsync(token.Token, default);
        var found = await repo.GetByTokenAsync(token.Token, default);

        found.Should().BeNull();
    }

    [Fact]
    public async Task RevokeAsync_OnNonExistentToken_ShouldNotThrow()
    {
        var repo = new RefreshTokenRepository(CreateContext());

        var act = async () => await repo.RevokeAsync("nonexistent-token", default);

        await act.Should().NotThrowAsync();
    }
}

file static class DateTimeExtensions
{
    public static DateTime TruncateToSeconds(this DateTime dt)
        => new(dt.Year, dt.Month, dt.Day, dt.Hour, dt.Minute, dt.Second, dt.Kind);
}