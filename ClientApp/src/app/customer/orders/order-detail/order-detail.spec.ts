import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideStore, Store } from '@ngrx/store';
import { of } from 'rxjs';
import { OrderDetail } from './order-detail';
import { OrderService } from '../../../core/services/order';
import { authReducer } from '../../../core/auth/store/auth.reducer';
import { cartReducer } from '../../../core/cart/store/cart.reducer';
import { CartActions } from '../../../core/cart/store/cart.actions';
import { selectCartItems } from '../../../core/cart/store/cart.selectors';
import { Order } from '../../../core/services/order.models';

const pendingOrder: Order = {
  id: 'order-1',
  customerId: 'u1',
  customerEmail: 'a@example.com',
  status: 'Pending',
  totalAmount: 9.99,
  createdAt: '2026-01-01T00:00:00Z',
  updatedAt: '2026-01-01T00:00:00Z',
  orderItems: [],
};

const confirmedOrder: Order = { ...pendingOrder, status: 'Confirmed' };

describe('OrderDetail', () => {
  function createComponent() {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        provideStore({ auth: authReducer, cart: cartReducer }),
        {
          provide: OrderService,
          useValue: { getById: () => of(pendingOrder), confirm: () => of(confirmedOrder) },
        },
      ],
    });
    const store = TestBed.inject(Store);
    store.dispatch(CartActions.loadCartSuccess({ items: [{ productId: 'p1', productName: 'W', unitPrice: 1, quantity: 1 }] }));

    const fixture = TestBed.createComponent(OrderDetail);
    fixture.componentRef.setInput('id', pendingOrder.id);
    fixture.detectChanges();
    return { component: fixture.componentInstance, store };
  }

  it('loads the order by id', () => {
    const { component } = createComponent();
    expect(component.order()).toEqual(pendingOrder);
  });

  it('confirming updates the order and resets the local cart state (server clears it via OrderPlacedEvent asynchronously)', () => {
    const { component, store } = createComponent();

    component.confirmOrder();

    expect(component.order()).toEqual(confirmedOrder);
    expect(component.confirming()).toBe(false);
    expect(store.selectSignal(selectCartItems)()).toEqual([]);
  });
});
