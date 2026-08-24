import { Component, inject, signal } from '@angular/core';
import { CurrencyPipe, DatePipe } from '@angular/common';
import { finalize } from 'rxjs/operators';
import { MatCardModule } from '@angular/material/card';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { OrderService } from '../../../core/services/order';
import { Order } from '../../../core/services/order.models';
import { extractErrorMessage } from '../../../core/http-error.util';

@Component({
  selector: 'app-admin-orders',
  imports: [CurrencyPipe, DatePipe, MatCardModule, MatProgressSpinnerModule],
  templateUrl: './admin-orders.html',
  styleUrl: './admin-orders.scss',
})
export class AdminOrders {
  private readonly orderService = inject(OrderService);

  readonly loading = signal(true);
  readonly error = signal<string | null>(null);
  readonly orders = signal<Order[]>([]);

  constructor() {
    this.orderService
      .getAllOrders()
      .pipe(finalize(() => this.loading.set(false)))
      .subscribe({
        next: (orders) => this.orders.set(orders),
        error: (err) => this.error.set(extractErrorMessage(err)),
      });
  }
}
