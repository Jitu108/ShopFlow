import { Component, inject } from '@angular/core';
import { CurrencyPipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { Store } from '@ngrx/store';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { CartActions } from '../../../core/cart/store/cart.actions';
import {
  selectCartError,
  selectCartItems,
  selectCartLoading,
  selectCartTotal,
} from '../../../core/cart/store/cart.selectors';

@Component({
  selector: 'app-cart-page',
  imports: [CurrencyPipe, RouterLink, MatCardModule, MatButtonModule, MatIconModule, MatProgressSpinnerModule],
  templateUrl: './cart-page.html',
  styleUrl: './cart-page.scss',
})
export class CartPage {
  private readonly store = inject(Store);

  readonly items = this.store.selectSignal(selectCartItems);
  readonly loading = this.store.selectSignal(selectCartLoading);
  readonly error = this.store.selectSignal(selectCartError);
  readonly total = this.store.selectSignal(selectCartTotal);

  constructor() {
    this.store.dispatch(CartActions.loadCart());
  }

  // The API rejects quantity < 1 (UpdateCartItemCommandValidator) — below
  // 1, remove the item instead of sending an invalid update.
  updateQuantity(productId: string, quantity: number): void {
    if (quantity < 1) {
      this.removeItem(productId);
      return;
    }
    this.store.dispatch(CartActions.updateQuantity({ productId, quantity }));
  }

  removeItem(productId: string): void {
    this.store.dispatch(CartActions.removeItem({ productId }));
  }

  clearCart(): void {
    this.store.dispatch(CartActions.clearCart());
  }
}
