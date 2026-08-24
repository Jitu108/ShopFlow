using FluentAssertions;
using Product.Domain.Entities;
using Product.Domain.Exceptions;

namespace Product.Domain.Tests.Entities;

public class ProductTests
{
    private static readonly Guid VendorId = Guid.NewGuid();
    private static readonly Guid CategoryId = Guid.NewGuid();

    [Fact]
    public void Create_WithValidData_ShouldSetProperties()
    {
        var product = ProductEntity.Create(VendorId, "Widget", "A useful widget", 9.99m, 10, CategoryId);

        product.VendorId.Should().Be(VendorId);
        product.Name.Should().Be("Widget");
        product.Description.Should().Be("A useful widget");
        product.Price.Should().Be(9.99m);
        product.StockQuantity.Should().Be(10);
        product.CategoryId.Should().Be(CategoryId);
        product.IsActive.Should().BeTrue();
    }

    [Fact]
    public void Create_ShouldSet_CreatedAtAndUpdatedAtToNow()
    {
        var before = DateTime.UtcNow;

        var product = ProductEntity.Create(VendorId, "Widget", "A useful widget", 9.99m, 10, CategoryId);

        product.CreatedAt.Should().BeOnOrAfter(before).And.BeOnOrBefore(DateTime.UtcNow);
        product.UpdatedAt.Should().Be(product.CreatedAt);
    }

    [Fact]
    public void Create_WithBlankName_ShouldThrowDomainException()
    {
        var act = () => ProductEntity.Create(VendorId, "  ", "desc", 9.99m, 10, CategoryId);

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Create_WithNegativePrice_ShouldThrowDomainException()
    {
        var act = () => ProductEntity.Create(VendorId, "Widget", "desc", -1m, 10, CategoryId);

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Create_WithNegativeStockQuantity_ShouldThrowDomainException()
    {
        var act = () => ProductEntity.Create(VendorId, "Widget", "desc", 9.99m, -1, CategoryId);

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Update_WithValidData_ShouldChangeProperties_AndBumpUpdatedAt()
    {
        var product = ProductEntity.Create(VendorId, "Widget", "desc", 9.99m, 10, CategoryId);
        var newCategoryId = Guid.NewGuid();

        product.Update("Gadget", "New desc", 19.99m, 5, newCategoryId);

        product.Name.Should().Be("Gadget");
        product.Description.Should().Be("New desc");
        product.Price.Should().Be(19.99m);
        product.StockQuantity.Should().Be(5);
        product.CategoryId.Should().Be(newCategoryId);
        product.UpdatedAt.Should().BeOnOrAfter(product.CreatedAt);
    }

    [Fact]
    public void Update_WithNegativePrice_ShouldThrowDomainException()
    {
        var product = ProductEntity.Create(VendorId, "Widget", "desc", 9.99m, 10, CategoryId);

        var act = () => product.Update("Widget", "desc", -5m, 10, CategoryId);

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Deactivate_ShouldSetIsActiveFalse_AndBumpUpdatedAt()
    {
        var product = ProductEntity.Create(VendorId, "Widget", "desc", 9.99m, 10, CategoryId);

        product.Deactivate();

        product.IsActive.Should().BeFalse();
        product.UpdatedAt.Should().BeOnOrAfter(product.CreatedAt);
    }

    [Fact]
    public void DecrementStock_WithValidQuantity_ShouldReduceStock_AndBumpUpdatedAt()
    {
        var product = ProductEntity.Create(VendorId, "Widget", "desc", 9.99m, 10, CategoryId);

        product.DecrementStock(4);

        product.StockQuantity.Should().Be(6);
        product.UpdatedAt.Should().BeOnOrAfter(product.CreatedAt);
    }

    [Fact]
    public void DecrementStock_WhenQuantityExceedsStock_ShouldFloorAtZero()
    {
        var product = ProductEntity.Create(VendorId, "Widget", "desc", 9.99m, 3, CategoryId);

        product.DecrementStock(10);

        product.StockQuantity.Should().Be(0);
    }

    [Fact]
    public void DecrementStock_WithZeroOrNegativeQuantity_ShouldThrowDomainException()
    {
        var product = ProductEntity.Create(VendorId, "Widget", "desc", 9.99m, 10, CategoryId);

        var act = () => product.DecrementStock(0);

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void IncrementStock_WithValidQuantity_ShouldIncreaseStock_AndBumpUpdatedAt()
    {
        var product = ProductEntity.Create(VendorId, "Widget", "desc", 9.99m, 10, CategoryId);

        product.IncrementStock(5);

        product.StockQuantity.Should().Be(15);
        product.UpdatedAt.Should().BeOnOrAfter(product.CreatedAt);
    }

    [Fact]
    public void IncrementStock_WithZeroOrNegativeQuantity_ShouldThrowDomainException()
    {
        var product = ProductEntity.Create(VendorId, "Widget", "desc", 9.99m, 10, CategoryId);

        var act = () => product.IncrementStock(0);

        act.Should().Throw<DomainException>();
    }
}
