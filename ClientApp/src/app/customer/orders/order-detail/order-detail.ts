import { Component, effect, inject, input, signal } from '@angular/core';
import { CurrencyPipe, DatePipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { Store } from '@ngrx/store';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { OrderService } from '../../../core/services/order';
import { Order } from '../../../core/services/order.models';
import { extractErrorMessage } from '../../../core/http-error.util';
import { CartActions } from '../../../core/cart/store/cart.actions';

@Component({
  selector: 'app-order-detail',
  imports: [RouterLink, CurrencyPipe, DatePipe, MatCardModule, MatButtonModule, MatProgressSpinnerModule],
  templateUrl: './order-detail.html',
  styleUrl: './order-detail.scss',
})
export class OrderDetail {
  private readonly orderService = inject(OrderService);
  private readonly store = inject(Store);

  // Bound from the route's :id segment via withComponentInputBinding().
  readonly id = input.required<string>();

  readonly loading = signal(true);
  readonly error = signal<string | null>(null);
  readonly order = signal<Order | null>(null);
  readonly confirming = signal(false);

  constructor() {
    effect(() => {
      const id = this.id();
      this.loading.set(true);
      this.error.set(null);
      this.orderService.getById(id).subscribe({
        next: (order) => {
          this.order.set(order);
          this.loading.set(false);
        },
        error: (err) => {
          this.error.set(extractErrorMessage(err));
          this.loading.set(false);
        },
      });
    });
  }

  confirmOrder(): void {
    const order = this.order();
    if (!order) {
      return;
    }
    this.confirming.set(true);
    this.orderService.confirm(order.id).subscribe({
      next: (updated) => {
        this.order.set(updated);
        this.confirming.set(false);
        // Confirming publishes OrderPlacedEvent server-side, which Cart's
        // consumer uses to clear the cart asynchronously — reset local
        // state now to match, rather than waiting on a re-fetch.
        this.store.dispatch(CartActions.resetState());
      },
      error: (err) => {
        this.error.set(extractErrorMessage(err));
        this.confirming.set(false);
      },
    });
  }
}
