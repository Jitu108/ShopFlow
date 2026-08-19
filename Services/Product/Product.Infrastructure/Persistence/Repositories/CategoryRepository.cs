using Microsoft.EntityFrameworkCore;
using Product.Application.Interfaces;
using Product.Domain.Entities;

namespace Product.Infrastructure.Persistence.Repositories;

public class CategoryRepository : ICategoryRepository
{
    private readonly AppDbContext _context;

    public CategoryRepository(AppDbContext context) => _context = context;

    public async Task<IReadOnlyList<Category>> GetAllAsync(CancellationToken ct)
        => await _context.Categories
            .OrderBy(x => x.Name)
            .ToListAsync(ct);

    public Task<bool> ExistsByNameAsync(string name, CancellationToken ct)
        => _context.Categories.AnyAsync(x => x.Name == name.Trim(), ct);

    public async Task AddAsync(Category category, CancellationToken ct)
    {
        await _context.Categories.AddAsync(category, ct);
        await _context.SaveChangesAsync(ct);
    }
}
