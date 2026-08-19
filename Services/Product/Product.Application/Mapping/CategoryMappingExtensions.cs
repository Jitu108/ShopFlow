using Product.Application.DTOs;
using Product.Domain.Entities;

namespace Product.Application.Mapping;

public static class CategoryMappingExtensions
{
    public static CategoryDto ToDto(this Category category) => new(category.Id, category.Name);
}
