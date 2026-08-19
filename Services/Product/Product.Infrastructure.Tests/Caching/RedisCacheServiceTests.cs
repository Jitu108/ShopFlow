using FluentAssertions;
using Product.Infrastructure.Caching;
using StackExchange.Redis;
using Testcontainers.Redis;

namespace Product.Infrastructure.Tests.Caching;

public class RedisCacheServiceTests : IAsyncLifetime
{
    private readonly RedisContainer _redis = new RedisBuilder().Build();
    private IConnectionMultiplexer _connectionMultiplexer = null!;

    public async Task InitializeAsync()
    {
        await _redis.StartAsync();
        _connectionMultiplexer = await ConnectionMultiplexer.ConnectAsync(_redis.GetConnectionString());
    }

    public async Task DisposeAsync()
    {
        await _connectionMultiplexer.DisposeAsync();
        await _redis.DisposeAsync();
    }

    private record SamplePayload(string Name, decimal Price);

    [Fact]
    public async Task SetAsync_ThenGetAsync_ShouldReturnValue()
    {
        var cache = new RedisCacheService(_connectionMultiplexer);
        var payload = new SamplePayload("Widget", 9.99m);

        await cache.SetAsync("key1", payload, TimeSpan.FromMinutes(5), default);
        var result = await cache.GetAsync<SamplePayload>("key1", default);

        result.Should().BeEquivalentTo(payload);
    }

    [Fact]
    public async Task GetAsync_WithMissingKey_ShouldReturnDefault()
    {
        var cache = new RedisCacheService(_connectionMultiplexer);

        var result = await cache.GetAsync<SamplePayload>("missing-key", default);

        result.Should().BeNull();
    }

    [Fact]
    public async Task RemoveAsync_ThenGetAsync_ShouldReturnDefault()
    {
        var cache = new RedisCacheService(_connectionMultiplexer);
        await cache.SetAsync("key2", new SamplePayload("Widget", 9.99m), TimeSpan.FromMinutes(5), default);

        await cache.RemoveAsync("key2", default);
        var result = await cache.GetAsync<SamplePayload>("key2", default);

        result.Should().BeNull();
    }
}
