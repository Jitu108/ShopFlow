using Product.Application.Interfaces;
using Product.Domain.Entities;

namespace Product.Api.Tests.Fixtures;

public class FakeCategoryRepository : ICategoryRepository
{
    private readonly Dictionary<Guid, Category> _store = new();

    public Task<IReadOnlyList<Category>> GetAllAsync(CancellationToken ct)
        => Task.FromResult<IReadOnlyList<Category>>(_store.Values.OrderBy(c => c.Name).ToList());

    public Task<bool> ExistsByNameAsync(string name, CancellationToken ct)
        => Task.FromResult(_store.Values.Any(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase)));

    public Task AddAsync(Category category, CancellationToken ct)
    {
        _store[category.Id] = category;
        return Task.CompletedTask;
    }

    public void Seed(Category category) => _store[category.Id] = category;
}
