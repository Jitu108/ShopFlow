// Matches Product.Application.DTOs.ProductDto (camelCase over the wire).
export interface Product {
  id: string;
  vendorId: string;
  name: string;
  description: string;
  price: number;
  stockQuantity: number;
  isActive: boolean;
  categoryId: string;
  createdAt: string;
  updatedAt: string;
}

export interface CreateProductRequest {
  name: string;
  description: string;
  price: number;
  stockQuantity: number;
  categoryId: string;
}

export type UpdateProductRequest = CreateProductRequest;
