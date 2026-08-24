import { createFeatureSelector, createSelector } from '@ngrx/store';
import { cartAdapter } from './cart.reducer';
import { CartState } from './cart.state';

export const selectCartState = createFeatureSelector<CartState>('cart');

const { selectAll } = cartAdapter.getSelectors();

export const selectCartItems = createSelector(selectCartState, selectAll);

export const selectCartItemCount = createSelector(selectCartItems, (items) =>
  items.reduce((sum, item) => sum + item.quantity, 0),
);

export const selectCartTotal = createSelector(selectCartItems, (items) =>
  items.reduce((sum, item) => sum + item.unitPrice * item.quantity, 0),
);

export const selectCartLoading = createSelector(selectCartState, (state) => state.loading);
export const selectCartError = createSelector(selectCartState, (state) => state.error);
