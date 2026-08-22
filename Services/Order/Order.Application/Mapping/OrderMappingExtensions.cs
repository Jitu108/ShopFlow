using Order.Application.DTOs;
using Order.Domain.Entities;

namespace Order.Application.Mapping;

public static class OrderMappingExtensions
{
    public static OrderDto ToDto(this OrderEntity order) => new(
        order.Id,
        order.CustomerId,
        order.CustomerEmail,
        order.Status.ToString(),
        order.TotalAmount,
        order.CreatedAt,
        order.UpdatedAt,
        order.OrderItems.Select(i => i.ToDto()).ToList()
    );

    public static OrderItemDto ToDto(this OrderItemEntity item) => new(
        item.Id,
        item.ProductId,
        item.ProductName,
        item.UnitPrice,
        item.Quantity
    );
}
