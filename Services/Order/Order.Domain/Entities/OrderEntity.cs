using Order.Domain.Enums;
using Order.Domain.Exceptions;

namespace Order.Domain.Entities;

public class OrderEntity
{
    public Guid Id { get; private set; }
    public Guid CustomerId { get; private set; }
    public string CustomerEmail { get; private set; } = string.Empty;
    public OrderStatus Status { get; private set; }
    public decimal TotalAmount { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    private readonly List<OrderItemEntity> _orderItems = new();
    public IReadOnlyList<OrderItemEntity> OrderItems => _orderItems.AsReadOnly();

    private OrderEntity() { }

    public static OrderEntity Create(Guid customerId, string customerEmail, List<OrderItemEntity> items)
    {
        if (customerId == Guid.Empty)
        {
            throw new DomainException("CustomerId is required.");
        }

        if (string.IsNullOrWhiteSpace(customerEmail))
        {
            throw new DomainException("CustomerEmail is required.");
        }

        if (items.Count == 0)
        {
            throw new DomainException("Order must contain at least one item.");
        }

        var now = DateTime.UtcNow;

        var order = new OrderEntity
        {
            Id = Guid.NewGuid(),
            CustomerId = customerId,
            CustomerEmail = customerEmail,
            Status = OrderStatus.Pending,
            CreatedAt = now,
            UpdatedAt = now
        };

        order._orderItems.AddRange(items);
        order.TotalAmount = items.Sum(i => i.UnitPrice * i.Quantity);

        return order;
    }

    public void Confirm()
    {
        if (Status != OrderStatus.Pending)
        {
            throw new DomainException($"Cannot confirm an order in '{Status}' status.");
        }

        Status = OrderStatus.Confirmed;
        UpdatedAt = DateTime.UtcNow;
    }
}
