// Matches Product.Application.DTOs.CategoryDto (camelCase over the wire).
export interface Category {
  id: string;
  name: string;
}

export interface CreateCategoryRequest {
  name: string;
}
