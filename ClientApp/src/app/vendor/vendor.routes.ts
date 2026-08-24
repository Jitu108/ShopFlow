import { Routes } from '@angular/router';
import { VendorDashboard } from './dashboard/vendor-dashboard/vendor-dashboard';
import { VendorProductList } from './products/vendor-product-list/vendor-product-list';
import { VendorProductForm } from './products/vendor-product-form/vendor-product-form';

// authGuard + roleGuard('Vendor') are applied once, on the parent 'vendor'
// route in app.routes.ts — every child here is already vendor-only.
export const VENDOR_ROUTES: Routes = [
  { path: '', redirectTo: 'dashboard', pathMatch: 'full' },
  { path: 'dashboard', component: VendorDashboard },
  { path: 'products', component: VendorProductList },
  { path: 'products/new', component: VendorProductForm },
  { path: 'products/:id/edit', component: VendorProductForm },
];
