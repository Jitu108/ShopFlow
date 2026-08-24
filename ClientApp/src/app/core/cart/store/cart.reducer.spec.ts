import { CartActions } from './cart.actions';
import { cartReducer, cartAdapter, initialCartState } from './cart.reducer';
import { CartItem } from '../cart.models';

const widget: CartItem = { productId: 'p1', productName: 'Widget', unitPrice: 9.99, quantity: 1 };
const gadget: CartItem = { productId: 'p2', productName: 'Gadget', unitPrice: 19.99, quantity: 2 };

describe('cartReducer', () => {
  it('returns the initial (empty) state for an unknown action', () => {
    const state = cartReducer(undefined, { type: 'noop' });
    expect(cartAdapter.getSelectors().selectAll(state)).toEqual([]);
  });

  it('setAll replaces the entity collection on loadCartSuccess', () => {
    const state = cartReducer(initialCartState, CartActions.loadCartSuccess({ items: [widget, gadget] }));
    expect(cartAdapter.getSelectors().selectAll(state)).toEqual([widget, gadget]);
    expect(state.loading).toBe(false);
  });

  it('upserts a new item on addItemSuccess without disturbing existing ones', () => {
    const withWidget = cartReducer(initialCartState, CartActions.loadCartSuccess({ items: [widget] }));
    const state = cartReducer(withWidget, CartActions.addItemSuccess({ item: gadget }));
    expect(cartAdapter.getSelectors().selectAll(state)).toEqual([widget, gadget]);
  });

  it('upserts (replaces) an existing item on addItemSuccess — e.g. server-merged quantity', () => {
    const withWidget = cartReducer(initialCartState, CartActions.loadCartSuccess({ items: [widget] }));
    const merged = { ...widget, quantity: 3 };
    const state = cartReducer(withWidget, CartActions.addItemSuccess({ item: merged }));
    expect(cartAdapter.getSelectors().selectAll(state)).toEqual([merged]);
  });

  it('removes exactly the targeted item on removeItemSuccess', () => {
    const withBoth = cartReducer(initialCartState, CartActions.loadCartSuccess({ items: [widget, gadget] }));
    const state = cartReducer(withBoth, CartActions.removeItemSuccess({ productId: widget.productId }));
    expect(cartAdapter.getSelectors().selectAll(state)).toEqual([gadget]);
  });

  it('empties the collection on clearCartSuccess', () => {
    const withBoth = cartReducer(initialCartState, CartActions.loadCartSuccess({ items: [widget, gadget] }));
    const state = cartReducer(withBoth, CartActions.clearCartSuccess());
    expect(cartAdapter.getSelectors().selectAll(state)).toEqual([]);
  });

  it('records the error message on a failure action without touching entities', () => {
    const withWidget = cartReducer(initialCartState, CartActions.loadCartSuccess({ items: [widget] }));
    const state = cartReducer(withWidget, CartActions.addItemFailure({ error: 'boom' }));
    expect(state.error).toBe('boom');
    expect(cartAdapter.getSelectors().selectAll(state)).toEqual([widget]);
  });

  it('resetState clears everything locally (used on logout, not a server call)', () => {
    const withBoth = cartReducer(initialCartState, CartActions.loadCartSuccess({ items: [widget, gadget] }));
    const state = cartReducer(withBoth, CartActions.resetState());
    expect(state).toEqual(initialCartState);
  });
});
