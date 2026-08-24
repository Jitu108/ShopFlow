import { Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { CurrencyPipe } from '@angular/common';
import { Store } from '@ngrx/store';
import { forkJoin } from 'rxjs';
import { finalize } from 'rxjs/operators';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatChipsModule } from '@angular/material/chips';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { ProductService } from '../../../core/services/product';
import { CategoryService } from '../../../core/services/category';
import { Product } from '../../../core/services/product.models';
import { Category } from '../../../core/services/category.models';
import { extractErrorMessage } from '../../../core/http-error.util';
import { selectAuthUser } from '../../../core/auth/store/auth.selectors';

@Component({
  selector: 'app-vendor-product-list',
  imports: [RouterLink, CurrencyPipe, MatCardModule, MatButtonModule, MatChipsModule, MatProgressSpinnerModule],
  templateUrl: './vendor-product-list.html',
  styleUrl: './vendor-product-list.scss',
})
export class VendorProductList {
  private readonly productService = inject(ProductService);
  private readonly categoryService = inject(CategoryService);
  private readonly store = inject(Store);

  readonly loading = signal(true);
  readonly error = signal<string | null>(null);
  readonly products = signal<Product[]>([]);
  private readonly categories = signal<Category[]>([]);

  private readonly categoryNamesById = computed(
    () => new Map(this.categories().map((c) => [c.id, c.name] as const)),
  );

  constructor() {
    const vendorId = this.store.selectSignal(selectAuthUser)()?.userId;
    if (!vendorId) {
      this.loading.set(false);
      return;
    }

    forkJoin({
      products: this.productService.getByVendorId(vendorId),
      categories: this.categoryService.getAll(),
    })
      .pipe(finalize(() => this.loading.set(false)))
      .subscribe({
        next: ({ products, categories }) => {
          this.products.set(products);
          this.categories.set(categories);
        },
        error: (err) => this.error.set(extractErrorMessage(err)),
      });
  }

  categoryNameFor(categoryId: string): string {
    return this.categoryNamesById().get(categoryId) ?? 'Uncategorized';
  }

  // Soft delete — the API has no reactivate endpoint, so this is one-way.
  deactivate(product: Product): void {
    this.productService.delete(product.id).subscribe({
      next: () => {
        this.products.set(
          this.products().map((p) => (p.id === product.id ? { ...p, isActive: false } : p)),
        );
      },
      error: (err) => this.error.set(extractErrorMessage(err)),
    });
  }
}
