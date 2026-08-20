using Cart.Application.DTOs;
using Cart.Application.Interfaces;

namespace Cart.Api.Tests.Fixtures;

public class FakeCartRepository : ICartRepository
{
    private readonly Dictionary<Guid, Dictionary<Guid, CartItemDto>> _carts = new();

    public Task<IReadOnlyDictionary<Guid, CartItemDto>> GetCartAsync(Guid userId, CancellationToken ct)
    {
        var cart = _carts.TryGetValue(userId, out var existing) ? existing : new Dictionary<Guid, CartItemDto>();
        return Task.FromResult<IReadOnlyDictionary<Guid, CartItemDto>>(cart);
    }

    public Task UpsertItemAsync(Guid userId, CartItemDto item, CancellationToken ct)
    {
        var cart = GetOrCreateCart(userId);
        cart[item.ProductId] = item;
        return Task.CompletedTask;
    }

    public Task RemoveItemAsync(Guid userId, Guid productId, CancellationToken ct)
    {
        if (_carts.TryGetValue(userId, out var cart))
        {
            cart.Remove(productId);
        }
        return Task.CompletedTask;
    }

    public Task ClearCartAsync(Guid userId, CancellationToken ct)
    {
        _carts.Remove(userId);
        return Task.CompletedTask;
    }

    private Dictionary<Guid, CartItemDto> GetOrCreateCart(Guid userId)
    {
        if (!_carts.TryGetValue(userId, out var cart))
        {
            cart = new Dictionary<Guid, CartItemDto>();
            _carts[userId] = cart;
        }
        return cart;
    }
}
