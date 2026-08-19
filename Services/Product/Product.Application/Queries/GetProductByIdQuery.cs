using MediatR;
using Product.Application.DTOs;

namespace Product.Application.Queries;

public record GetProductByIdQuery(Guid Id) : IRequest<ProductDto>;
