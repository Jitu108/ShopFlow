import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideStore } from '@ngrx/store';
import { of } from 'rxjs';
import { CatalogList } from './catalog-list';
import { ProductService } from '../../../core/services/product';
import { CategoryService } from '../../../core/services/category';
import { Product } from '../../../core/services/product.models';
import { Category } from '../../../core/services/category.models';
import { authReducer } from '../../../core/auth/store/auth.reducer';
import { cartReducer } from '../../../core/cart/store/cart.reducer';

const categories: Category[] = [
  { id: 'cat-1', name: 'Widgets' },
  { id: 'cat-2', name: 'Gadgets' },
];

const products: Product[] = [
  {
    id: 'p1',
    vendorId: 'v1',
    name: 'Widget',
    description: 'A widget',
    price: 9.99,
    stockQuantity: 5,
    isActive: true,
    categoryId: 'cat-1',
    createdAt: '2026-01-01T00:00:00Z',
    updatedAt: '2026-01-01T00:00:00Z',
  },
  {
    id: 'p2',
    vendorId: 'v1',
    name: 'Gadget',
    description: 'A gadget',
    price: 19.99,
    stockQuantity: 3,
    isActive: true,
    categoryId: 'cat-2',
    createdAt: '2026-01-01T00:00:00Z',
    updatedAt: '2026-01-01T00:00:00Z',
  },
];

describe('CatalogList', () => {
  function createComponent() {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        provideStore({ auth: authReducer, cart: cartReducer }),
        { provide: ProductService, useValue: { getAll: () => of(products) } },
        { provide: CategoryService, useValue: { getAll: () => of(categories) } },
      ],
    });
    const fixture = TestBed.createComponent(CatalogList);
    fixture.detectChanges();
    return fixture.componentInstance;
  }

  it('loads all products and categories on init', () => {
    const component = createComponent();
    expect(component.loading()).toBe(false);
    expect(component.products()).toEqual(products);
    expect(component.filteredProducts()).toEqual(products);
  });

  it('filters products by the selected category client-side (the API has no server-side filter)', () => {
    const component = createComponent();
    component.selectCategory('cat-2');
    expect(component.filteredProducts()).toEqual([products[1]]);
  });

  it('shows all products again once the category filter is cleared', () => {
    const component = createComponent();
    component.selectCategory('cat-1');
    component.selectCategory('');
    expect(component.filteredProducts()).toEqual(products);
  });

  it('resolves a category name for a known id and falls back for an unknown one', () => {
    const component = createComponent();
    expect(component.categoryNameFor('cat-1')).toBe('Widgets');
    expect(component.categoryNameFor('missing')).toBe('Uncategorized');
  });
});
