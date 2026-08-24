import { Component, inject, signal } from '@angular/core';
import { CurrencyPipe, DatePipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { finalize } from 'rxjs/operators';
import { MatCardModule } from '@angular/material/card';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { OrderService } from '../../../core/services/order';
import { Order } from '../../../core/services/order.models';
import { extractErrorMessage } from '../../../core/http-error.util';

@Component({
  selector: 'app-order-history',
  imports: [RouterLink, CurrencyPipe, DatePipe, MatCardModule, MatProgressSpinnerModule],
  templateUrl: './order-history.html',
  styleUrl: './order-history.scss',
})
export class OrderHistory {
  private readonly orderService = inject(OrderService);

  readonly loading = signal(true);
  readonly error = signal<string | null>(null);
  readonly orders = signal<Order[]>([]);

  constructor() {
    this.orderService
      .getMyOrders()
      .pipe(finalize(() => this.loading.set(false)))
      .subscribe({
        next: (orders) => this.orders.set(orders),
        error: (err) => this.error.set(extractErrorMessage(err)),
      });
  }
}
