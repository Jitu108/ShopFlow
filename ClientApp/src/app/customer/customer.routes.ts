import { Routes } from '@angular/router';
import { authGuard } from '../core/auth/auth-guard';
import { CatalogList } from './catalog/catalog-list/catalog-list';
import { CatalogDetail } from './catalog/catalog-detail/catalog-detail';
import { CartPage } from './cart/cart-page/cart-page';
import { Checkout } from './orders/checkout/checkout';
import { OrderHistory } from './orders/order-history/order-history';
import { OrderDetail } from './orders/order-detail/order-detail';

// Catalog is anonymous (matches GET /api/products); cart/checkout/orders
// require a valid JWT (matches the Authorization policy table in
// ShopFlow-ProjectSpec.md; checkout additionally needs RequireVerifiedEmail,
// enforced by the Gateway itself and handled reactively in Checkout).
export const CUSTOMER_ROUTES: Routes = [
  { path: '', redirectTo: 'catalog', pathMatch: 'full' },
  { path: 'catalog', component: CatalogList },
  { path: 'catalog/:id', component: CatalogDetail },
  { path: 'cart', component: CartPage, canActivate: [authGuard] },
  { path: 'checkout', component: Checkout, canActivate: [authGuard] },
  { path: 'orders', component: OrderHistory, canActivate: [authGuard] },
  { path: 'orders/:id', component: OrderDetail, canActivate: [authGuard] },
];
