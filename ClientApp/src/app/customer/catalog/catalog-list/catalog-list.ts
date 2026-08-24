import { Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { CurrencyPipe } from '@angular/common';
import { Store } from '@ngrx/store';
import { forkJoin } from 'rxjs';
import { finalize } from 'rxjs/operators';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatSelectModule } from '@angular/material/select';
import { MatButtonModule } from '@angular/material/button';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { ProductService } from '../../../core/services/product';
import { CategoryService } from '../../../core/services/category';
import { Product } from '../../../core/services/product.models';
import { Category } from '../../../core/services/category.models';
import { extractErrorMessage } from '../../../core/http-error.util';
import { selectAuthUser } from '../../../core/auth/store/auth.selectors';
import { CartActions } from '../../../core/cart/store/cart.actions';

@Component({
  selector: 'app-catalog-list',
  imports: [
    RouterLink,
    CurrencyPipe,
    MatCardModule,
    MatFormFieldModule,
    MatSelectModule,
    MatButtonModule,
    MatProgressSpinnerModule,
  ],
  templateUrl: './catalog-list.html',
  styleUrl: './catalog-list.scss',
})
export class CatalogList {
  private readonly productService = inject(ProductService);
  private readonly categoryService = inject(CategoryService);
  private readonly store = inject(Store);

  readonly user = this.store.selectSignal(selectAuthUser);

  readonly loading = signal(true);
  readonly error = signal<string | null>(null);
  readonly products = signal<Product[]>([]);
  readonly categories = signal<Category[]>([]);
  readonly selectedCategoryId = signal('');

  readonly filteredProducts = computed(() => {
    const categoryId = this.selectedCategoryId();
    return categoryId ? this.products().filter((p) => p.categoryId === categoryId) : this.products();
  });

  private readonly categoryNamesById = computed(
    () => new Map(this.categories().map((c) => [c.id, c.name] as const)),
  );

  constructor() {
    forkJoin({
      products: this.productService.getAll(),
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

  selectCategory(categoryId: string): void {
    this.selectedCategoryId.set(categoryId);
  }

  categoryNameFor(categoryId: string): string {
    return this.categoryNamesById().get(categoryId) ?? 'Uncategorized';
  }

  addToCart(event: Event, product: Product): void {
    event.stopPropagation();
    event.preventDefault();
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
