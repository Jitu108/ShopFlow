import { EntityState } from '@ngrx/entity';
import { CartItem } from '../cart.models';

export interface CartState extends EntityState<CartItem> {
  loading: boolean;
  error: string | null;
}
