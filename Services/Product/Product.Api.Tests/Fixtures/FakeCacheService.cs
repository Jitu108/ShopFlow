using Product.Application.Interfaces;

namespace Product.Api.Tests.Fixtures;

public class FakeCacheService : ICacheService
{
    private readonly Dictionary<string, object> _store = new();

    public Task<T?> GetAsync<T>(string key, CancellationToken ct)
    {
        _store.TryGetValue(key, out var value);
        return Task.FromResult(value is T typed ? typed : default);
    }

    public Task SetAsync<T>(string key, T value, TimeSpan expiry, CancellationToken ct)
    {
        _store[key] = value!;
        return Task.CompletedTask;
    }

    public Task RemoveAsync(string key, CancellationToken ct)
    {
        _store.Remove(key);
        return Task.CompletedTask;
    }
}
