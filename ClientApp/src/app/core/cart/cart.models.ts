// Matches Cart.Application.DTOs.CartItemDto (camelCase over the wire).
// Cart.Api has no SQL/domain entity — the client must supply productName and
// unitPrice on add, since the Redis-backed cart doesn't look products up itself.
export interface CartItem {
  productId: string;
  productName: string;
  unitPrice: number;
  quantity: number;
}

export interface AddCartItemRequest {
  productId: string;
  productName: string;
  unitPrice: number;
  quantity: number;
}
