import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { CreateProductRequest, Product, UpdateProductRequest } from './product.models';

@Injectable({
  providedIn: 'root',
})
export class ProductService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiBaseUrl}/api/products`;

  // GET /api/products has no server-side filter — it returns every active
  // product. Category filtering happens client-side (see catalog.ts).
  getAll(): Observable<Product[]> {
    return this.http.get<Product[]>(this.baseUrl);
  }

  getById(id: string): Observable<Product> {
    return this.http.get<Product>(`${this.baseUrl}/${id}`);
  }

  // Vendor-only, own products only — GetVendorProductsQueryHandler does NOT
  // filter by isActive, unlike getAll(), so deactivated products still show
  // up here (with isActive: false) even though they've vanished from the
  // public catalog.
  getByVendorId(vendorId: string): Observable<Product[]> {
    return this.http.get<Product[]>(`${environment.apiBaseUrl}/api/vendors/${vendorId}/products`);
  }

  create(request: CreateProductRequest): Observable<Product> {
    return this.http.post<Product>(this.baseUrl, request);
  }

  update(id: string, request: UpdateProductRequest): Observable<Product> {
    return this.http.put<Product>(`${this.baseUrl}/${id}`, request);
  }

  // Despite the verb, this is a SOFT delete (DeleteProductCommandHandler
  // calls product.Deactivate(), not a real delete) — there is no reactivate
  // endpoint, so this is a one-way action from the UI's perspective.
  delete(id: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/${id}`);
  }
}
