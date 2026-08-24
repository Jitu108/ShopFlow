import { createActionGroup, emptyProps, props } from '@ngrx/store';
import { CartItem } from '../cart.models';

export const CartActions = createActionGroup({
  source: 'Cart',
  events: {
    'Load Cart': emptyProps(),
    'Load Cart Success': props<{ items: CartItem[] }>(),
    'Load Cart Failure': props<{ error: string }>(),

    'Add Item': props<{ productId: string; productName: string; unitPrice: number; quantity: number }>(),
    'Add Item Success': props<{ item: CartItem }>(),
    'Add Item Failure': props<{ error: string }>(),

    'Update Quantity': props<{ productId: string; quantity: number }>(),
    'Update Quantity Success': props<{ item: CartItem }>(),
    'Update Quantity Failure': props<{ error: string }>(),

    'Remove Item': props<{ productId: string }>(),
    'Remove Item Success': props<{ productId: string }>(),
    'Remove Item Failure': props<{ error: string }>(),

    'Clear Cart': emptyProps(),
    'Clear Cart Success': emptyProps(),
    'Clear Cart Failure': props<{ error: string }>(),

    // Local-only reset (no HTTP call) — used on logout. Dispatching
    // ClearCart there would call DELETE /api/cart and actually empty the
    // user's saved cart just because they logged out.
    'Reset State': emptyProps(),
  },
});
