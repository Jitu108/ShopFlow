using System.Text.Json;
using Cart.Application.DTOs;
using Cart.Application.Interfaces;
using StackExchange.Redis;

namespace Cart.Infrastructure.Persistence;

public class RedisCartRepository : ICartRepository
{
    private static readonly TimeSpan Ttl = TimeSpan.FromDays(7);

    private readonly IConnectionMultiplexer _connectionMultiplexer;

    public RedisCartRepository(IConnectionMultiplexer connectionMultiplexer)
    {
        _connectionMultiplexer = connectionMultiplexer;
    }

    private IDatabase Database => _connectionMultiplexer.GetDatabase();

    public async Task<IReadOnlyDictionary<Guid, CartItemDto>> GetCartAsync(Guid userId, CancellationToken ct)
    {
        var key = CartKeys.ForUser(userId);
        var entries = await Database.HashGetAllAsync(key);

        if (entries.Length > 0)
        {
            await Database.KeyExpireAsync(key, Ttl);
        }

        return entries.ToDictionary(
            e => Guid.Parse((string)e.Name!),
            e => JsonSerializer.Deserialize<CartItemDto>((string)e.Value!)!);
    }

    public async Task UpsertItemAsync(Guid userId, CartItemDto item, CancellationToken ct)
    {
        var key = CartKeys.ForUser(userId);
        await Database.HashSetAsync(key, item.ProductId.ToString(), JsonSerializer.Serialize(item));
        await Database.KeyExpireAsync(key, Ttl);
    }

    public async Task RemoveItemAsync(Guid userId, Guid productId, CancellationToken ct)
    {
        var key = CartKeys.ForUser(userId);
        await Database.HashDeleteAsync(key, productId.ToString());

        if (await Database.HashLengthAsync(key) > 0)
        {
            await Database.KeyExpireAsync(key, Ttl);
        }
    }

    public async Task ClearCartAsync(Guid userId, CancellationToken ct)
        => await Database.KeyDeleteAsync(CartKeys.ForUser(userId));
}
