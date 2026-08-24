import { Component, effect, inject, input, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { CurrencyPipe } from '@angular/common';
import { Store } from '@ngrx/store';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { ProductService } from '../../../core/services/product';
import { Product } from '../../../core/services/product.models';
import { extractErrorMessage } from '../../../core/http-error.util';
import { selectAuthUser } from '../../../core/auth/store/auth.selectors';
import { CartActions } from '../../../core/cart/store/cart.actions';

@Component({
  selector: 'app-catalog-detail',
  imports: [RouterLink, CurrencyPipe, MatCardModule, MatButtonModule, MatProgressSpinnerModule],
  templateUrl: './catalog-detail.html',
  styleUrl: './catalog-detail.scss',
})
export class CatalogDetail {
  private readonly productService = inject(ProductService);
  private readonly store = inject(Store);

  readonly user = this.store.selectSignal(selectAuthUser);

  // Bound from the route's :id segment via withComponentInputBinding().
  readonly id = input.required<string>();

  readonly loading = signal(true);
  readonly error = signal<string | null>(null);
  readonly product = signal<Product | null>(null);

  constructor() {
    effect(() => {
      const id = this.id();
      this.loading.set(true);
      this.error.set(null);
      this.productService.getById(id).subscribe({
        next: (product) => {
          this.product.set(product);
          this.loading.set(false);
        },
        error: (err) => {
          this.error.set(extractErrorMessage(err));
          this.loading.set(false);
        },
      });
    });
  }

  addToCart(product: Product): void {
    this.store.dispatch(
      CartActions.addItem({
        productId: product.id,
        productName: product.name,
        unitPrice: product.price,
        quantity: 1,
      }),
    );
  }
}
