import { TestBed } from '@angular/core/testing';
import { provideStore, Store } from '@ngrx/store';
import { of } from 'rxjs';
import { VendorDashboard } from './vendor-dashboard';
import { ProductService } from '../../../core/services/product';
import { Product } from '../../../core/services/product.models';
import { authReducer } from '../../../core/auth/store/auth.reducer';
import { cartReducer } from '../../../core/cart/store/cart.reducer';
import { AuthActions } from '../../../core/auth/store/auth.actions';
import { AuthUser } from '../../../core/auth/auth.models';

const vendor: AuthUser = {
  userId: 'vendor-1',
  email: 'v@example.com',
  displayName: 'Vendor',
  role: 'Vendor',
  emailVerified: true,
};

function product(overrides: Partial<Product>): Product {
  return {
    id: 'p',
    vendorId: 'vendor-1',
    name: 'Product',
    description: '',
    price: 10,
    stockQuantity: 20,
    isActive: true,
    categoryId: 'c1',
    createdAt: '2026-01-01T00:00:00Z',
    updatedAt: '2026-01-01T00:00:00Z',
    ...overrides,
  };
}

describe('VendorDashboard', () => {
  function createComponent(products: Product[]) {
    TestBed.configureTestingModule({
      providers: [
        provideStore({ auth: authReducer, cart: cartReducer }),
        { provide: ProductService, useValue: { getByVendorId: () => of(products) } },
      ],
    });
    TestBed.inject(Store).dispatch(AuthActions.loginSuccess({ user: vendor }));
    const fixture = TestBed.createComponent(VendorDashboard);
    fixture.detectChanges();
    return fixture.componentInstance;
  }

  it('counts active and inactive listings separately', () => {
    const component = createComponent([
      product({ id: 'p1', isActive: true }),
      product({ id: 'p2', isActive: true }),
      product({ id: 'p3', isActive: false }),
    ]);
    expect(component.activeCount()).toBe(2);
    expect(component.inactiveCount()).toBe(1);
  });

  it('sums stock value from active listings only, excluding inactive ones', () => {
    const component = createComponent([
      product({ id: 'p1', isActive: true, price: 10, stockQuantity: 5 }),
      product({ id: 'p2', isActive: false, price: 1000, stockQuantity: 1000 }),
    ]);
    expect(component.totalStockValue()).toBe(50);
  });

  it('flags low-stock active listings under the 10-unit threshold', () => {
    const component = createComponent([
      product({ id: 'p1', isActive: true, stockQuantity: 9 }),
      product({ id: 'p2', isActive: true, stockQuantity: 10 }),
      product({ id: 'p3', isActive: false, stockQuantity: 1 }),
    ]);
    expect(component.lowStockCount()).toBe(1);
  });
});
