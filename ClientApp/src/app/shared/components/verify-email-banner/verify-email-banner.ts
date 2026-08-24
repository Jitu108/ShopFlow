import { Component, computed, inject } from '@angular/core';
import { Store } from '@ngrx/store';
import { MatButtonModule } from '@angular/material/button';
import { AuthActions } from '../../../core/auth/store/auth.actions';
import { selectAuthUser } from '../../../core/auth/store/auth.selectors';

// The backend has no real send-a-link flow: POST /api/auth/verify-email
// marks the account verified immediately for whoever is calling it — so this
// is a one-click "verify now" action, not a "resend the email" one.
@Component({
  selector: 'app-verify-email-banner',
  imports: [MatButtonModule],
  template: `
    @if (show()) {
      <div class="banner">
        <span>Verify your email to place orders.</span>
        <button mat-button (click)="verify()">Verify email now</button>
      </div>
    }
  `,
  styles: [
    `
      .banner {
        background: var(--mat-sys-tertiary-container);
        color: var(--mat-sys-on-tertiary-container);
        padding: 0.5rem 1rem;
        display: flex;
        justify-content: space-between;
        align-items: center;
      }
    `,
  ],
})
export class VerifyEmailBanner {
  private readonly store = inject(Store);
  private readonly user = this.store.selectSignal(selectAuthUser);

  readonly show = computed(() => this.user()?.emailVerified === false);

  verify(): void {
    this.store.dispatch(AuthActions.verifyEmailRequested());
  }
}
