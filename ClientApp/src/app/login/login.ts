import { Component, inject } from '@angular/core';
import { ReactiveFormsModule, FormBuilder, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { Store } from '@ngrx/store';
import { AuthActions } from '../core/auth/store/auth.actions';
import { selectAuthError, selectAuthStatus } from '../core/auth/store/auth.selectors';

const REMEMBERED_EMAIL_KEY = 'shopflow.rememberedEmail';

@Component({
  selector: 'app-login',
  imports: [ReactiveFormsModule, RouterLink],
  templateUrl: './login.html',
  styleUrl: './login.scss',
})
export class Login {
  private readonly fb = inject(FormBuilder);
  private readonly store = inject(Store);

  readonly status = this.store.selectSignal(selectAuthStatus);
  readonly error = this.store.selectSignal(selectAuthError);

  private readonly rememberedEmail = localStorage.getItem(REMEMBERED_EMAIL_KEY);

  readonly form = this.fb.nonNullable.group({
    email: [this.rememberedEmail ?? '', [Validators.required, Validators.email]],
    password: ['', [Validators.required]],
    rememberMe: [!!this.rememberedEmail],
  });

  submit(): void {
    if (this.form.invalid) {
      return;
    }
    const { email, password, rememberMe } = this.form.getRawValue();

    if (rememberMe) {
      localStorage.setItem(REMEMBERED_EMAIL_KEY, email);
    } else {
      localStorage.removeItem(REMEMBERED_EMAIL_KEY);
    }

    this.store.dispatch(AuthActions.login({ email, password }));
  }
}
