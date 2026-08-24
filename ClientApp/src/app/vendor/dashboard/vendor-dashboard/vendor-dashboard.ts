import { Component, computed, inject, signal } from '@angular/core';
import { CurrencyPipe } from '@angular/common';
import { Store } from '@ngrx/store';
import { finalize } from 'rxjs/operators';
import { MatCardModule } from '@angular/material/card';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { ProductService } from '../../../core/services/product';
import { Product } from '../../../core/services/product.models';
import { extractErrorMessage } from '../../../core/http-error.util';
import { selectAuthUser } from '../../../core/auth/store/auth.selectors';

// Client-computed stub — no backend route exposes vendor-scoped order/revenue
// data (only GET /api/vendors/{id}/products is vendor-scoped), and adding one
// would be backend scope creep in a UI-only phase. See Documentations/Phases/
// Phase7-Plan.md, Decision resolved with the user during planning. This
// intentionally shows listing health, not sales.
const LOW_STOCK_THRESHOLD = 10;

@Component({
  selector: 'app-vendor-dashboard',
  imports: [CurrencyPipe, MatCardModule, MatProgressSpinnerModule],
  templateUrl: './vendor-dashboard.html',
  styleUrl: './vendor-dashboard.scss',
})
export class VendorDashboard {
  private readonly productService = inject(ProductService);
  private readonly store = inject(Store);

  readonly loading = signal(true);
  readonly error = signal<string | null>(null);
  private readonly products = signal<Product[]>([]);

  private readonly activeProducts = computed(() => this.products().filter((p) => p.isActive));
  readonly activeCount = computed(() => this.activeProducts().length);
  readonly inactiveCount = computed(() => this.products().length - this.activeCount());
  readonly totalStockValue = computed(() =>
    this.activeProducts().reduce((sum, p) => sum + p.price * p.stockQuantity, 0),
  );
  readonly lowStockCount = computed(
    () => this.activeProducts().filter((p) => p.stockQuantity < LOW_STOCK_THRESHOLD).length,
  );

  constructor() {
    const vendorId = this.store.selectSignal(selectAuthUser)()?.userId;
    if (!vendorId) {
      this.loading.set(false);
      return;
    }

    this.productService
      .getByVendorId(vendorId)
      .pipe(finalize(() => this.loading.set(false)))
      .subscribe({
        next: (products) => this.products.set(products),
        error: (err) => this.error.set(extractErrorMessage(err)),
      });
  }
}
