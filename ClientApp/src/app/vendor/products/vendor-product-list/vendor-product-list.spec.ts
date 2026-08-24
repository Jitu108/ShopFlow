import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideStore, Store } from '@ngrx/store';
import { of } from 'rxjs';
import { VendorProductList } from './vendor-product-list';
import { ProductService } from '../../../core/services/product';
import { CategoryService } from '../../../core/services/category';
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

const widget: Product = {
  id: 'p1',
  vendorId: 'vendor-1',
  name: 'Widget',
  description: '',
  price: 9.99,
  stockQuantity: 5,
  isActive: true,
  categoryId: 'c1',
  createdAt: '2026-01-01T00:00:00Z',
  updatedAt: '2026-01-01T00:00:00Z',
};

describe('VendorProductList', () => {
  function createComponent(deleteFn: () => ReturnType<ProductService['delete']>) {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        provideStore({ auth: authReducer, cart: cartReducer }),
        {
          provide: ProductService,
          useValue: { getByVendorId: () => of([widget]), delete: deleteFn },
        },
        { provide: CategoryService, useValue: { getAll: () => of([{ id: 'c1', name: 'Cat' }]) } },
      ],
    });
    TestBed.inject(Store).dispatch(AuthActions.loginSuccess({ user: vendor }));
    const fixture = TestBed.createComponent(VendorProductList);
    fixture.detectChanges();
    return fixture.componentInstance;
  }

  it('marks the product inactive locally after a successful deactivate call, without refetching', () => {
    const component = createComponent(() => of(undefined));

    component.deactivate(widget);

    expect(component.products()).toEqual([{ ...widget, isActive: false }]);
  });

  it('resolves a category name for a known id', () => {
    const component = createComponent(() => of(undefined));
    expect(component.categoryNameFor('c1')).toBe('Cat');
  });
});
