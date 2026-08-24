import { Component, inject } from '@angular/core';
import { RouterLink, RouterLinkActive } from '@angular/router';
import { Store } from '@ngrx/store';
import { MatToolbarModule } from '@angular/material/toolbar';
import { MatButtonModule } from '@angular/material/button';
import { MatBadgeModule } from '@angular/material/badge';
import { AuthActions } from '../../../core/auth/store/auth.actions';
import { selectAuthUser } from '../../../core/auth/store/auth.selectors';
import { selectCartItemCount } from '../../../core/cart/store/cart.selectors';

@Component({
  selector: 'app-navbar',
  imports: [RouterLink, RouterLinkActive, MatToolbarModule, MatButtonModule, MatBadgeModule],
  templateUrl: './navbar.html',
  styleUrl: './navbar.scss',
})
export class Navbar {
  private readonly store = inject(Store);

  readonly user = this.store.selectSignal(selectAuthUser);
  readonly cartCount = this.store.selectSignal(selectCartItemCount);

  logout(): void {
    this.store.dispatch(AuthActions.logout());
  }
}
