using MediatR;
using Product.Application.DTOs;

namespace Product.Application.Queries;

public record GetCategoryListQuery : IRequest<IReadOnlyList<CategoryDto>>;
