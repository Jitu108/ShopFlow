import { Routes } from '@angular/router';
import { AdminUsers } from './users/admin-users/admin-users';
import { AdminOrders } from './orders/admin-orders/admin-orders';
import { AdminCategories } from './categories/admin-categories/admin-categories';

// authGuard + roleGuard('Admin') are applied once, on the parent 'admin'
// route in app.routes.ts. admin/orders and admin/categories extend beyond
// the spec's illustrative admin/users-only tree — see Documentations/Phases/
// Phase7-Plan.md — to cover real routes (GET /api/admin/orders,
// POST /api/categories) that need a home somewhere.
export const ADMIN_ROUTES: Routes = [
  { path: '', redirectTo: 'users', pathMatch: 'full' },
  { path: 'users', component: AdminUsers },
  { path: 'orders', component: AdminOrders },
  { path: 'categories', component: AdminCategories },
];
