using MediatR;
using Product.Application.DTOs;

namespace Product.Application.Queries;

public record GetProductListQuery : IRequest<IReadOnlyList<ProductDto>>;
