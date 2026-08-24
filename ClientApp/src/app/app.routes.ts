import { Routes } from '@angular/router';
import { authGuard } from './core/auth/auth-guard';
import { roleGuard } from './core/auth/role-guard';

export const routes: Routes = [
  { path: '', redirectTo: 'customer/catalog', pathMatch: 'full' },
  { path: 'login', loadComponent: () => import('./login/login').then((m) => m.Login) },
  { path: 'register', loadComponent: () => import('./register/register').then((m) => m.Register) },
  {
    path: 'customer',
    loadChildren: () => import('./customer/customer.routes').then((m) => m.CUSTOMER_ROUTES),
  },
  {
    path: 'vendor',
    canActivate: [authGuard, roleGuard('Vendor')],
    loadChildren: () => import('./vendor/vendor.routes').then((m) => m.VENDOR_ROUTES),
  },
  {
    path: 'admin',
    canActivate: [authGuard, roleGuard('Admin')],
    loadChildren: () => import('./admin/admin.routes').then((m) => m.ADMIN_ROUTES),
  },
];
