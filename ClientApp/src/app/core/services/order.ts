import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { Order, PlaceOrderRequest } from './order.models';

@Injectable({
  providedIn: 'root',
})
export class OrderService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiBaseUrl}/api/orders`;

  // Order.Api's PlaceOrder only creates the order in Pending status — it does
  // NOT publish OrderPlacedEvent (that only happens on confirm()), so the
  // cart is not cleared and no confirmation email is sent until confirm.
  placeOrder(request: PlaceOrderRequest): Observable<Order> {
    return this.http.post<Order>(this.baseUrl, request);
  }

  getMyOrders(): Observable<Order[]> {
    return this.http.get<Order[]>(this.baseUrl);
  }

  getById(id: string): Observable<Order> {
    return this.http.get<Order>(`${this.baseUrl}/${id}`);
  }

  confirm(id: string): Observable<Order> {
    return this.http.put<Order>(`${this.baseUrl}/${id}/confirm`, {});
  }

  // Admin-only, cross-customer.
  getAllOrders(): Observable<Order[]> {
    return this.http.get<Order[]>(`${environment.apiBaseUrl}/api/admin/orders`);
  }
}
