using Identity.Application.Interfaces;
using Identity.Domain.Entities;

namespace Identity.Api.Tests.Fixtures;

public class FakeRefreshTokenRepository : IRefreshTokenRepository
{
    private readonly Dictionary<string, RefreshToken> _store = new();

    public Task<RefreshToken?> GetByTokenAsync(string token, CancellationToken ct)
    {
        _store.TryGetValue(token, out var found);
        return Task.FromResult(found);
    }

    public Task SaveAsync(RefreshToken token, CancellationToken ct)
    {
        _store[token.Token] = token;
        return Task.CompletedTask;
    }

    public Task RevokeAsync(string token, CancellationToken ct)
    {
        _store.Remove(token);
        return Task.CompletedTask;
    }
}