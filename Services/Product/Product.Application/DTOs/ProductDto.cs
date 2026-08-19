namespace Product.Application.DTOs;

public record ProductDto(
    Guid Id,
    Guid VendorId,
    string Name,
    string Description,
    decimal Price,
    int StockQuantity,
    bool IsActive,
    Guid CategoryId,
    DateTime CreatedAt,
    DateTime UpdatedAt
);
