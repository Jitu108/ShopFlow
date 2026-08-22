namespace Order.Application.DTOs;

public record OrderItemRequestDto(
    Guid ProductId,
    string ProductName,
    decimal UnitPrice,
    int Quantity
);
