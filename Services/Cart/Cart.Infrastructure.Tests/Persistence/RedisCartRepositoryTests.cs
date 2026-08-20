using Cart.Application.DTOs;
using Cart.Infrastructure.Persistence;
using FluentAssertions;
using StackExchange.Redis;
using Testcontainers.Redis;

namespace Cart.Infrastructure.Tests.Persistence;

public class RedisCartRepositoryTests : IAsyncLifetime
{
    private readonly RedisContainer _redis = new RedisBuilder().Build();
    private IConnectionMultiplexer _connectionMultiplexer = null!;
    private RedisCartRepository _repository = null!;

    public async Task InitializeAsync()
    {
        await _redis.StartAsync();
        _connectionMultiplexer = await ConnectionMultiplexer.ConnectAsync(_redis.GetConnectionString());
        _repository = new RedisCartRepository(_connectionMultiplexer);
    }

    public async Task DisposeAsync()
    {
        await _connectionMultiplexer.DisposeAsync();
        await _redis.DisposeAsync();
    }

    [Fact]
    public async Task UpsertItemAsync_ThenGetCartAsync_ShouldRoundtripItem()
    {
        var userId = Guid.NewGuid();
        var item = new CartItemDto(Guid.NewGuid(), "Widget", 9.99m, 2);

        await _repository.UpsertItemAsync(userId, item, default);
        var cart = await _repository.GetCartAsync(userId, default);

        cart.Should().ContainKey(item.ProductId);
        cart[item.ProductId].Should().Be(item);
    }

    [Fact]
    public async Task UpsertItemAsync_WithExistingProduct_ShouldUpdateInPlace_NotDuplicate()
    {
        var userId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        await _repository.UpsertItemAsync(userId, new CartItemDto(productId, "Widget", 9.99m, 1), default);

        await _repository.UpsertItemAsync(userId, new CartItemDto(productId, "Widget", 9.99m, 5), default);
        var cart = await _repository.GetCartAsync(userId, default);

        cart.Should().HaveCount(1);
        cart[productId].Quantity.Should().Be(5);
    }

    [Fact]
    public async Task RemoveItemAsync_ShouldLeaveOtherItemsIntact()
    {
        var userId = Guid.NewGuid();
        var keep = new CartItemDto(Guid.NewGuid(), "Keep", 5m, 1);
        var remove = new CartItemDto(Guid.NewGuid(), "Remove", 5m, 1);
        await _repository.UpsertItemAsync(userId, keep, default);
        await _repository.UpsertItemAsync(userId, remove, default);

        await _repository.RemoveItemAsync(userId, remove.ProductId, default);
        var cart = await _repository.GetCartAsync(userId, default);

        cart.Should().ContainKey(keep.ProductId);
        cart.Should().NotContainKey(remove.ProductId);
    }

    [Fact]
    public async Task ClearCartAsync_ShouldRemoveWholeKey()
    {
        var userId = Guid.NewGuid();
        await _repository.UpsertItemAsync(userId, new CartItemDto(Guid.NewGuid(), "Widget", 9.99m, 1), default);

        await _repository.ClearCartAsync(userId, default);
        var cart = await _repository.GetCartAsync(userId, default);

        cart.Should().BeEmpty();
        (await _connectionMultiplexer.GetDatabase().KeyExistsAsync(CartKeys.ForUser(userId))).Should().BeFalse();
    }

    [Fact]
    public async Task UpsertItemAsync_ShouldSetTtl_CloseToSevenDays()
    {
        var userId = Guid.NewGuid();

        await _repository.UpsertItemAsync(userId, new CartItemDto(Guid.NewGuid(), "Widget", 9.99m, 1), default);
        var ttl = await _connectionMultiplexer.GetDatabase().KeyTimeToLiveAsync(CartKeys.ForUser(userId));

        ttl.Should().NotBeNull();
        ttl!.Value.Should().BeCloseTo(TimeSpan.FromDays(7), TimeSpan.FromMinutes(1));
    }
}
