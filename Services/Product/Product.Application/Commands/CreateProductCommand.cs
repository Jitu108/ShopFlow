using MediatR;
using Product.Application.DTOs;

namespace Product.Application.Commands;

public record CreateProductCommand(
    Guid VendorId,
    string Name,
    string Description,
    decimal Price,
    int StockQuantity,
    Guid CategoryId
) : IRequest<ProductDto>;
