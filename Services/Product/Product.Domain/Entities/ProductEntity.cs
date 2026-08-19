using Product.Domain.Exceptions;

namespace Product.Domain.Entities;

public class ProductEntity
{
    public Guid Id { get; private set; }
    public Guid VendorId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public decimal Price { get; private set; }
    public int StockQuantity { get; private set; }
    public bool IsActive { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    public Guid CategoryId { get; private set; }
    public Category? Category { get; private set; }

    private ProductEntity() { }

    public static ProductEntity Create(Guid vendorId, string name, string description, decimal price, int stockQuantity, Guid categoryId)
    {
        Validate(name, price, stockQuantity);

        var now = DateTime.UtcNow;

        return new ProductEntity
        {
            Id = Guid.NewGuid(),
            VendorId = vendorId,
            Name = name,
            Description = description,
            Price = price,
            StockQuantity = stockQuantity,
            CategoryId = categoryId,
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    public void Update(string name, string description, decimal price, int stockQuantity, Guid categoryId)
    {
        Validate(name, price, stockQuantity);

        Name = name;
        Description = description;
        Price = price;
        StockQuantity = stockQuantity;
        CategoryId = categoryId;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Deactivate()
    {
        IsActive = false;
        UpdatedAt = DateTime.UtcNow;
    }

    private static void Validate(string name, decimal price, int stockQuantity)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("Product name is required.");
        }

        if (price < 0)
        {
            throw new DomainException("Product price cannot be negative.");
        }

        if (stockQuantity < 0)
        {
            throw new DomainException("Product stock quantity cannot be negative.");
        }
    }
}
