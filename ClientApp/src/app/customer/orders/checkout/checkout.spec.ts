import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideStore, Store } from '@ngrx/store';
import { HttpErrorResponse } from '@angular/common/http';
import { throwError, of, Observable } from 'rxjs';
import { Checkout } from './checkout';
import { OrderService } from '../../../core/services/order';
import { authReducer } from '../../../core/auth/store/auth.reducer';
import { cartReducer } from '../../../core/cart/store/cart.reducer';
import { CartActions } from '../../../core/cart/store/cart.actions';
import { CartItem } from '../../../core/cart/cart.models';
import { Order } from '../../../core/services/order.models';

const items: CartItem[] = [{ productId: 'p1', productName: 'Widget', unitPrice: 9.99, quantity: 2 }];

const placedOrder: Order = {
  id: 'order-1',
  customerId: 'u1',
  customerEmail: 'a@example.com',
  status: 'Pending',
  totalAmount: 19.98,
  createdAt: '2026-01-01T00:00:00Z',
  updatedAt: '2026-01-01T00:00:00Z',
  orderItems: [],
};

@Component({ template: '' })
class OrderDetailStub {}

describe('Checkout', () => {
  function createComponent(placeOrder: () => Observable<Order>) {
    TestBed.configureTestingModule({
      providers: [
        // Matches app.routes.ts's real destination — without it,
        // router.navigate(['/customer/orders', id]) in the success path
        // rejects with NG04002 (same hazard found in 7.1's interceptor spec).
        provideRouter([{ path: 'customer/orders/:id', component: OrderDetailStub }]),
        provideStore({ auth: authReducer, cart: cartReducer }),
        { provide: OrderService, useValue: { placeOrder } },
      ],
    });
    const store = TestBed.inject(Store);
    store.dispatch(CartActions.loadCartSuccess({ items }));

    const fixture = TestBed.createComponent(Checkout);
    fixture.detectChanges();
    return fixture.componentInstance;
  }

  it('shows the verify-email prompt, not a raw error, on a 403 (RouteClaimsRequirement)', () => {
    const component = createComponent(() => throwError(() => new HttpErrorResponse({ status: 403 })));

    component.placeOrder();

    expect(component.needsVerification()).toBe(true);
    expect(component.error()).toBeNull();
  });

  it('shows a generic error message for a non-403 failure', () => {
    const component = createComponent(() =>
      throwError(() => new HttpErrorResponse({ status: 500, error: { message: 'boom' } })),
    );

    component.placeOrder();

    expect(component.needsVerification()).toBe(false);
    expect(component.error()).toBe('boom');
  });

  it('stops the placing spinner once the order is placed successfully', () => {
    const component = createComponent(() => of(placedOrder));

    component.placeOrder();

    expect(component.placing()).toBe(false);
    expect(component.error()).toBeNull();
    expect(component.needsVerification()).toBe(false);
  });

  it('does nothing when the cart is empty', () => {
    let called = false;
    const component = createComponent(() => {
      called = true;
      return of(placedOrder);
    });
    // Overwrite the seeded cart back to empty for this one case.
    TestBed.inject(Store).dispatch(CartActions.clearCartSuccess());

    component.placeOrder();

    expect(called).toBe(false);
  });
});
