import { Component, inject, signal } from '@angular/core';
import { ReactiveFormsModule, FormBuilder, FormControl, Validators } from '@angular/forms';
import { finalize } from 'rxjs/operators';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatChipsModule } from '@angular/material/chips';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { UserService } from '../../../core/services/user';
import { UserProfile } from '../../../core/services/user.models';
import { UserRole } from '../../../core/auth/auth.models';
import { extractErrorMessage } from '../../../core/http-error.util';

const ROLES: UserRole[] = ['Customer', 'Vendor', 'Admin'];

@Component({
  selector: 'app-admin-users',
  imports: [
    ReactiveFormsModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
    MatButtonModule,
    MatCardModule,
    MatChipsModule,
    MatProgressSpinnerModule,
  ],
  templateUrl: './admin-users.html',
  styleUrl: './admin-users.scss',
})
export class AdminUsers {
  private readonly userService = inject(UserService);
  private readonly fb = inject(FormBuilder);

  readonly roles = ROLES;
  readonly searchControl = this.fb.nonNullable.control('');

  readonly loading = signal(true);
  readonly error = signal<string | null>(null);
  readonly users = signal<UserProfile[]>([]);

  // userId currently showing its inline reset-password mini-form, if any.
  readonly resettingUserId = signal<string | null>(null);
  readonly newPasswordControl = new FormControl('', {
    nonNullable: true,
    validators: [Validators.required, Validators.minLength(8)],
  });
  readonly resetSaving = signal(false);
  readonly resetMessage = signal<string | null>(null);

  constructor() {
    this.search();
  }

  search(): void {
    this.loading.set(true);
    this.error.set(null);
    this.userService
      .searchUsers(this.searchControl.value || undefined)
      .pipe(finalize(() => this.loading.set(false)))
      .subscribe({
        next: (users) => this.users.set(users),
        error: (err) => this.error.set(extractErrorMessage(err)),
      });
  }

  assignRole(user: UserProfile, role: string): void {
    this.userService.assignRole(user.id, role).subscribe({
      next: () => {
        this.users.set(this.users().map((u) => (u.id === user.id ? { ...u, role: role as UserRole } : u)));
      },
      error: (err) => this.error.set(extractErrorMessage(err)),
    });
  }

  startResetPassword(userId: string): void {
    this.resettingUserId.set(userId);
    this.newPasswordControl.reset('');
    this.resetMessage.set(null);
  }

  cancelResetPassword(): void {
    this.resettingUserId.set(null);
  }

  submitResetPassword(): void {
    const userId = this.resettingUserId();
    if (!userId || this.newPasswordControl.invalid) {
      return;
    }
    this.resetSaving.set(true);
    this.userService.resetPassword(userId, this.newPasswordControl.value).subscribe({
      next: () => {
        this.resetSaving.set(false);
        this.resettingUserId.set(null);
        this.resetMessage.set('Password reset.');
      },
      error: (err) => {
        this.resetSaving.set(false);
        this.error.set(extractErrorMessage(err));
      },
    });
  }
}
