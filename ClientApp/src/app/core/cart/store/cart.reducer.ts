import { createEntityAdapter } from '@ngrx/entity';
import { createReducer, on } from '@ngrx/store';
import { CartItem } from '../cart.models';
import { CartActions } from './cart.actions';
import { CartState } from './cart.state';

export const cartAdapter = createEntityAdapter<CartItem>({
  selectId: (item) => item.productId,
});

export const initialCartState: CartState = cartAdapter.getInitialState({
  loading: false,
  error: null,
});

export const cartReducer = createReducer(
  initialCartState,
  on(
    CartActions.loadCart,
    CartActions.addItem,
    CartActions.updateQuantity,
    CartActions.removeItem,
    CartActions.clearCart,
    (state): CartState => ({ ...state, loading: true, error: null }),
  ),
  on(CartActions.loadCartSuccess, (state, { items }): CartState =>
    cartAdapter.setAll(items, { ...state, loading: false }),
  ),
  on(CartActions.addItemSuccess, CartActions.updateQuantitySuccess, (state, { item }): CartState =>
    cartAdapter.upsertOne(item, { ...state, loading: false }),
  ),
  on(CartActions.removeItemSuccess, (state, { productId }): CartState =>
    cartAdapter.removeOne(productId, { ...state, loading: false }),
  ),
  on(CartActions.clearCartSuccess, (state): CartState => cartAdapter.removeAll({ ...state, loading: false })),
  on(
    CartActions.loadCartFailure,
    CartActions.addItemFailure,
    CartActions.updateQuantityFailure,
    CartActions.removeItemFailure,
    CartActions.clearCartFailure,
    (state, { error }): CartState => ({ ...state, loading: false, error }),
  ),
  on(CartActions.resetState, (): CartState => initialCartState),
);
