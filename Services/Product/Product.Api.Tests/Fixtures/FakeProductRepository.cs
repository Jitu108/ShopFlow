using Product.Application.Interfaces;
using Product.Domain.Entities;

namespace Product.Api.Tests.Fixtures;

public class FakeProductRepository : IProductRepository
{
    private readonly Dictionary<Guid, ProductEntity> _store = new();

    public Task<ProductEntity?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        _store.TryGetValue(id, out var product);
        return Task.FromResult(product);
    }

    public Task<IReadOnlyList<ProductEntity>> GetAllActiveAsync(CancellationToken ct)
        => Task.FromResult<IReadOnlyList<ProductEntity>>(_store.Values.Where(p => p.IsActive).ToList());

    public Task<IReadOnlyList<ProductEntity>> GetByVendorIdAsync(Guid vendorId, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<ProductEntity>>(_store.Values.Where(p => p.VendorId == vendorId).ToList());

    public Task AddAsync(ProductEntity product, CancellationToken ct)
    {
        _store[product.Id] = product;
        return Task.CompletedTask;
    }

    public Task UpdateAsync(ProductEntity product, CancellationToken ct)
    {
        _store[product.Id] = product;
        return Task.CompletedTask;
    }

    public void Seed(ProductEntity product) => _store[product.Id] = product;
}
