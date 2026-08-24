import { Component, inject, signal } from '@angular/core';
import { CurrencyPipe } from '@angular/common';
import { Router, RouterLink } from '@angular/router';
import { Store } from '@ngrx/store';
import { HttpErrorResponse } from '@angular/common/http';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { OrderService } from '../../../core/services/order';
import { extractErrorMessage } from '../../../core/http-error.util';
import { selectCartItems, selectCartTotal } from '../../../core/cart/store/cart.selectors';
import { AuthActions } from '../../../core/auth/store/auth.actions';

@Component({
  selector: 'app-checkout',
  imports: [CurrencyPipe, RouterLink, MatCardModule, MatButtonModule, MatProgressSpinnerModule],
  templateUrl: './checkout.html',
  styleUrl: './checkout.scss',
})
export class Checkout {
  private readonly store = inject(Store);
  private readonly orderService = inject(OrderService);
  private readonly router = inject(Router);

  readonly items = this.store.selectSignal(selectCartItems);
  readonly total = this.store.selectSignal(selectCartTotal);

  readonly placing = signal(false);
  readonly error = signal<string | null>(null);
  readonly needsVerification = signal(false);

  placeOrder(): void {
    const items = this.items();
    if (items.length === 0) {
      return;
    }

    this.placing.set(true);
    this.error.set(null);
    this.needsVerification.set(false);

    this.orderService
      .placeOrder({
        items: items.map(({ productId, productName, unitPrice, quantity }) => ({
          productId,
          productName,
          unitPrice,
          quantity,
        })),
      })
      .subscribe({
        next: (order) => {
          this.placing.set(false);
          this.router.navigate(['/customer/orders', order.id]);
        },
        error: (err: unknown) => {
          this.placing.set(false);
          // The Gateway enforces RouteClaimsRequirement { emailVerified: "true" }
          // on this exact route (Phase 6) — any 403 here means exactly one
          // thing: show the verification prompt, not a raw error.
          if (err instanceof HttpErrorResponse && err.status === 403) {
            this.needsVerification.set(true);
          } else {
            this.error.set(extractErrorMessage(err));
          }
        },
      });
  }

  resendVerification(): void {
    this.store.dispatch(AuthActions.verifyEmailRequested());
  }
}
