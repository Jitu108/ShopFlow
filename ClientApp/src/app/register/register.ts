import { Component, inject } from '@angular/core';
import { ReactiveFormsModule, FormBuilder, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { Store } from '@ngrx/store';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { AuthActions } from '../core/auth/store/auth.actions';
import { selectAuthError, selectAuthStatus } from '../core/auth/store/auth.selectors';

// Mirrors Identity.Application.Validators.RegisterUserCommandValidator so the
// user sees the same rule client-side instead of only after a round trip.
const PASSWORD_COMPLEXITY_PATTERN = /^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[^a-zA-Z0-9]).{8,}$/;

@Component({
  selector: 'app-register',
  imports: [
    ReactiveFormsModule,
    RouterLink,
    MatFormFieldModule,
    MatInputModule,
    MatButtonModule,
    MatCardModule,
    MatProgressSpinnerModule,
  ],
  templateUrl: './register.html',
  styleUrl: './register.scss',
})
export class Register {
  private readonly fb = inject(FormBuilder);
  private readonly store = inject(Store);

  readonly status = this.store.selectSignal(selectAuthStatus);
  readonly error = this.store.selectSignal(selectAuthError);

  readonly form = this.fb.nonNullable.group({
    email: ['', [Validators.required, Validators.email]],
    displayName: ['', [Validators.required, Validators.maxLength(100)]],
    password: ['', [Validators.required, Validators.pattern(PASSWORD_COMPLEXITY_PATTERN)]],
  });

  submit(): void {
    if (this.form.invalid) {
      return;
    }
    const { email, password, displayName } = this.form.getRawValue();
    this.store.dispatch(AuthActions.register({ email, password, displayName }));
  }
}
