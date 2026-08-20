using Cart.Application.DTOs;

namespace Cart.Application.Interfaces;

public interface ICartRepository
{
    Task<IReadOnlyDictionary<Guid, CartItemDto>> GetCartAsync(Guid userId, CancellationToken ct);
    Task UpsertItemAsync(Guid userId, CartItemDto item, CancellationToken ct);
    Task RemoveItemAsync(Guid userId, Guid productId, CancellationToken ct);
    Task ClearCartAsync(Guid userId, CancellationToken ct);
}
