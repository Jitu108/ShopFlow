using Product.Application.DTOs;
using Product.Domain.Entities;

namespace Product.Application.Mapping;

public static class ProductMappingExtensions
{
    public static ProductDto ToDto(this ProductEntity product) => new(
        product.Id,
        product.VendorId,
        product.Name,
        product.Description,
        product.Price,
        product.StockQuantity,
        product.IsActive,
        product.CategoryId,
        product.CreatedAt,
        product.UpdatedAt
    );
}
