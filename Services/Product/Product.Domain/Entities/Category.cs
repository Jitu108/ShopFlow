using Product.Domain.Exceptions;

namespace Product.Domain.Entities;

public class Category
{
    public Guid Id { get; private set; }
    public string Name { get; private set; } = string.Empty;

    public ICollection<ProductEntity> Products { get; private set; } = new List<ProductEntity>();

    private Category() { }

    public static Category Create(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("Category name is required.");
        }

        return new Category
        {
            Id = Guid.NewGuid(),
            Name = name
        };
    }
}
