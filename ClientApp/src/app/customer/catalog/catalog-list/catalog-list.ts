import { Component, computed, effect, inject, input, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { CurrencyPipe } from '@angular/common';
import { Store } from '@ngrx/store';
import { forkJoin } from 'rxjs';
import { finalize } from 'rxjs/operators';
import { ProductService } from '../../../core/services/product';
import { CategoryService } from '../../../core/services/category';
import { VendorService } from '../../../core/services/vendor';
import { Product } from '../../../core/services/product.models';
import { Category } from '../../../core/services/category.models';
import { extractErrorMessage } from '../../../core/http-error.util';
import { selectAuthUser } from '../../../core/auth/store/auth.selectors';
import { CartActions } from '../../../core/cart/store/cart.actions';

type SortOrder = 'relevance' | 'newest' | 'price-asc' | 'price-desc';

const FAVORITES_KEY = 'shopflow.favoriteProductIds';
const PAGE_SIZE = 8;
const NEW_WINDOW_MS = 7 * 24 * 60 * 60 * 1000;

function loadFavorites(): Set<string> {
  try {
    const raw = localStorage.getItem(FAVORITES_KEY);
    return raw ? new Set(JSON.parse(raw)) : new Set();
  } catch {
    return new Set();
  }
}

@Component({
  selector: 'app-catalog-list',
  imports: [RouterLink, CurrencyPipe],
  templateUrl: './catalog-list.html',
  styleUrl: './catalog-list.scss',
})
export class CatalogList {
  private readonly productService = inject(ProductService);
  private readonly categoryService = inject(CategoryService);
  private readonly vendorService = inject(VendorService);
  private readonly store = inject(Store);

  // Bound from ?q=/?categoryId= via withComponentInputBinding() — set by the
  // navbar's search box. GET /api/products has no server-side search or
  // category filter, so these filter the already-loaded list client-side.
  readonly q = input('');
  readonly categoryId = input('');

  readonly user = this.store.selectSignal(selectAuthUser);

  readonly loading = signal(true);
  readonly error = signal<string | null>(null);
  readonly products = signal<Product[]>([]);
  readonly categories = signal<Category[]>([]);
  readonly vendorNamesById = signal<Map<string, string>>(new Map());
  readonly selectedCategoryId = signal('');
  readonly categorySearch = signal('');

  readonly sortOrder = signal<SortOrder>('relevance');
  readonly inStockOnly = signal(false);
  readonly minPrice = signal<number | null>(null);
  readonly maxPrice = signal<number | null>(null);
  readonly currentPage = signal(1);
  readonly favoriteIds = signal<Set<string>>(loadFavorites());

  readonly pageSize = PAGE_SIZE;

  readonly filteredProducts = computed(() => {
    const categoryId = this.selectedCategoryId();
    return categoryId ? this.products().filter((p) => p.categoryId === categoryId) : this.products();
  });

  readonly categoryNamesById = computed(() => new Map(this.categories().map((c) => [c.id, c.name] as const)));

  readonly categoryCounts = computed(() => {
    const counts = new Map<string, number>();
    for (const product of this.products()) {
      counts.set(product.categoryId, (counts.get(product.categoryId) ?? 0) + 1);
    }
    return counts;
  });

  private readonly topCategories = computed(() =>
    [...this.categories()]
      .sort((a, b) => (this.categoryCounts().get(b.id) ?? 0) - (this.categoryCounts().get(a.id) ?? 0))
      .slice(0, 5),
  );

  readonly displayedCategories = computed(() => {
    const query = this.categorySearch().trim().toLowerCase();
    if (!query) {
      return this.topCategories();
    }
    return this.categories().filter((c) => c.name.toLowerCase().includes(query));
  });

  readonly visibleProducts = computed(() => {
    const min = this.minPrice();
    const max = this.maxPrice();
    const inStockOnly = this.inStockOnly();
    const nameQuery = (this.q() ?? '').trim().toLowerCase();

    let result = this.filteredProducts().filter((p) => {
      if (inStockOnly && p.stockQuantity <= 0) return false;
      if (min !== null && p.price < min) return false;
      if (max !== null && p.price > max) return false;
      if (nameQuery && !p.name.toLowerCase().includes(nameQuery)) return false;
      return true;
    });

    const sortOrder = this.sortOrder();
    if (sortOrder === 'price-asc') {
      result = [...result].sort((a, b) => a.price - b.price);
    } else if (sortOrder === 'price-desc') {
      result = [...result].sort((a, b) => b.price - a.price);
    } else if (sortOrder === 'newest') {
      result = [...result].sort((a, b) => Date.parse(b.createdAt) - Date.parse(a.createdAt));
    }

    return result;
  });

  readonly totalPages = computed(() => Math.max(1, Math.ceil(this.visibleProducts().length / this.pageSize)));

  readonly pagedProducts = computed(() => {
    const page = Math.min(this.currentPage(), this.totalPages());
    const start = (page - 1) * this.pageSize;
    return this.visibleProducts().slice(start, start + this.pageSize);
  });

  readonly pageNumbers = computed(() => Array.from({ length: this.totalPages() }, (_, i) => i + 1));

  constructor() {
    effect(() => {
      const id = this.categoryId();
      this.selectedCategoryId.set(id ?? '');
    });

    forkJoin({
      products: this.productService.getAll(),
      categories: this.categoryService.getAll(),
    })
      .pipe(finalize(() => this.loading.set(false)))
      .subscribe({
        next: ({ products, categories }) => {
          this.products.set(products);
          this.categories.set(categories);

          const vendorIds = [...new Set(products.map((p) => p.vendorId))];
          this.vendorService.getNames(vendorIds).subscribe((vendors) => {
            this.vendorNamesById.set(new Map(vendors.map((v) => [v.id, v.displayName] as const)));
          });
        },
        error: (err) => this.error.set(extractErrorMessage(err)),
      });
  }

  selectCategory(categoryId: string): void {
    this.selectedCategoryId.set(categoryId);
    this.currentPage.set(1);
  }

  categoryNameFor(categoryId: string): string {
    return this.categoryNamesById().get(categoryId) ?? 'Uncategorized';
  }

  vendorNameFor(vendorId: string): string {
    return this.vendorNamesById().get(vendorId) ?? 'Independent vendor';
  }

  setCategorySearch(value: string): void {
    this.categorySearch.set(value);
  }

  clearCategorySearch(): void {
    this.categorySearch.set('');
  }

  setSortOrder(value: string): void {
    this.sortOrder.set(value as SortOrder);
    this.currentPage.set(1);
  }

  toggleInStockOnly(): void {
    this.inStockOnly.set(!this.inStockOnly());
    this.currentPage.set(1);
  }

  setMinPrice(value: string): void {
    this.minPrice.set(value === '' ? null : Number(value));
    this.currentPage.set(1);
  }

  setMaxPrice(value: string): void {
    this.maxPrice.set(value === '' ? null : Number(value));
    this.currentPage.set(1);
  }

  goToPage(page: number): void {
    this.currentPage.set(page);
  }

  isNew(product: Product): boolean {
    return Date.now() - Date.parse(product.createdAt) < NEW_WINDOW_MS;
  }

  isFavorite(productId: string): boolean {
    return this.favoriteIds().has(productId);
  }

  toggleFavorite(event: Event, productId: string): void {
    event.stopPropagation();
    event.preventDefault();
    const next = new Set(this.favoriteIds());
    if (next.has(productId)) {
      next.delete(productId);
    } else {
      next.add(productId);
    }
    this.favoriteIds.set(next);
    localStorage.setItem(FAVORITES_KEY, JSON.stringify([...next]));
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
