// Matches Order.Application.DTOs.OrderDto/OrderItemDto (camelCase over the wire).
export type OrderStatus = 'Pending' | 'Confirmed' | 'Shipped' | 'Delivered' | 'Cancelled';

export interface OrderItem {
  id: string;
  productId: string;
  productName: string;
  unitPrice: number;
  quantity: number;
}

export interface Order {
  id: string;
  customerId: string;
  customerEmail: string;
  status: OrderStatus;
  totalAmount: number;
  createdAt: string;
  updatedAt: string;
  orderItems: OrderItem[];
}

export interface OrderItemRequest {
  productId: string;
  productName: string;
  unitPrice: number;
  quantity: number;
}

// PlaceOrderCommand takes the item list straight from the request body — it
// does NOT read the server-side cart itself. The caller must build this from
// the client's own cart state (see checkout.ts).
export interface PlaceOrderRequest {
  items: OrderItemRequest[];
}
