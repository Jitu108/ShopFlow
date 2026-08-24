import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { AddCartItemRequest, CartItem } from './cart.models';

@Injectable({
  providedIn: 'root',
})
export class CartService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiBaseUrl}/api/cart`;

  getCart(): Observable<CartItem[]> {
    return this.http.get<CartItem[]>(this.baseUrl);
  }

  // Adding an already-present product increments its quantity server-side
  // (AddCartItemCommandHandler), it does not error or replace.
  addItem(request: AddCartItemRequest): Observable<CartItem> {
    return this.http.post<CartItem>(`${this.baseUrl}/items`, request);
  }

  // UpdateCartItemCommandValidator requires quantity >= 1 — to remove an
  // item, call removeItem instead of updating to 0.
  updateItemQuantity(productId: string, quantity: number): Observable<CartItem> {
    return this.http.put<CartItem>(`${this.baseUrl}/items/${productId}`, { quantity });
  }

  removeItem(productId: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/items/${productId}`);
  }

  clearCart(): Observable<void> {
    return this.http.delete<void>(this.baseUrl);
  }
}
