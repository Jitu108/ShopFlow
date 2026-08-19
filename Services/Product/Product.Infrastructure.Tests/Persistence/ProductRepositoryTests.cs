using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Product.Domain.Entities;
using Product.Infrastructure.Persistence;
using Product.Infrastructure.Persistence.Repositories;
using Testcontainers.MsSql;

namespace Product.Infrastructure.Tests.Persistence;

public class ProductRepositoryTests : IAsyncLifetime
{
    private readonly MsSqlContainer _sql = new MsSqlBuilder().Build();

    public async Task InitializeAsync() => await _sql.StartAsync();
    public async Task DisposeAsync() => await _sql.DisposeAsync();

    private AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(_sql.GetConnectionString())
            .Options;
        var ctx = new AppDbContext(options);
        ctx.Database.EnsureCreated();
        return ctx;
    }

    private static async Task<Guid> SeedCategoryAsync(AppDbContext ctx)
    {
        var category = Category.Create("Electronics");
        await ctx.Categories.AddAsync(category);
        await ctx.SaveChangesAsync();
        return category.Id;
    }

    [Fact]
    public async Task AddAsync_ThenGetById_ShouldReturnProduct()
    {
        var ctx = CreateContext();
        var categoryId = await SeedCategoryAsync(ctx);
        var repo = new ProductRepository(ctx);
        var product = ProductEntity.Create(Guid.NewGuid(), "Widget", "desc", 9.99m, 10, categoryId);

        await repo.AddAsync(product, default);
        var found = await repo.GetByIdAsync(product.Id, default);

        found.Should().NotBeNull();
        found!.Name.Should().Be("Widget");
    }

    [Fact]
    public async Task GetByIdAsync_WithUnknownId_ShouldReturnNull()
    {
        var repo = new ProductRepository(CreateContext());

        var found = await repo.GetByIdAsync(Guid.NewGuid(), default);

        found.Should().BeNull();
    }

    [Fact]
    public async Task GetAllActiveAsync_ShouldExclude_DeactivatedProducts()
    {
        var ctx = CreateContext();
        var categoryId = await SeedCategoryAsync(ctx);
        var repo = new ProductRepository(ctx);
        var active = ProductEntity.Create(Guid.NewGuid(), "Widget", "desc", 9.99m, 10, categoryId);
        var inactive = ProductEntity.Create(Guid.NewGuid(), "Gadget", "desc", 19.99m, 5, categoryId);
        inactive.Deactivate();

        await repo.AddAsync(active, default);
        await repo.AddAsync(inactive, default);

        var result = await repo.GetAllActiveAsync(default);

        result.Should().ContainSingle(x => x.Id == active.Id);
    }

    [Fact]
    public async Task GetByVendorIdAsync_ShouldReturn_OnlyThatVendorsProducts()
    {
        var ctx = CreateContext();
        var categoryId = await SeedCategoryAsync(ctx);
        var repo = new ProductRepository(ctx);
        var vendorId = Guid.NewGuid();
        var ownProduct = ProductEntity.Create(vendorId, "Widget", "desc", 9.99m, 10, categoryId);
        var otherProduct = ProductEntity.Create(Guid.NewGuid(), "Gadget", "desc", 19.99m, 5, categoryId);

        await repo.AddAsync(ownProduct, default);
        await repo.AddAsync(otherProduct, default);

        var result = await repo.GetByVendorIdAsync(vendorId, default);

        result.Should().ContainSingle(x => x.Id == ownProduct.Id);
    }

    [Fact]
    public async Task UpdateAsync_ShouldPersist_Changes()
    {
        var ctx = CreateContext();
        var categoryId = await SeedCategoryAsync(ctx);
        var repo = new ProductRepository(ctx);
        var product = ProductEntity.Create(Guid.NewGuid(), "Widget", "desc", 9.99m, 10, categoryId);
        await repo.AddAsync(product, default);

        product.Update("Gadget", "new desc", 19.99m, 5, categoryId);
        await repo.UpdateAsync(product, default);

        var found = await repo.GetByIdAsync(product.Id, default);
        found!.Name.Should().Be("Gadget");
        found.Price.Should().Be(19.99m);
    }
}
