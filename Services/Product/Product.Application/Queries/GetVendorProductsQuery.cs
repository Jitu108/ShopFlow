using MediatR;
using Product.Application.DTOs;

namespace Product.Application.Queries;

public record GetVendorProductsQuery(Guid VendorId) : IRequest<IReadOnlyList<ProductDto>>;
