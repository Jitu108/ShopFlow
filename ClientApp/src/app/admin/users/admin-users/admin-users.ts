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
import { MatDialog } from '@angular/material/dialog';
import { UserService } from '../../../core/services/user';
import { UserProfile } from '../../../core/services/user.models';
import { UserRole } from '../../../core/auth/auth.models';
import { extractErrorMessage } from '../../../core/http-error.util';
import { ConfirmDialogComponent } from '../../../shared/components/confirm-dialog/confirm-dialog';

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
  private readonly dialog = inject(MatDialog);

  readonly roles = ROLES;
  readonly searchControl = this.fb.nonNullable.control('');

  readonly loading = signal(true);
  readonly error = signal<string | null>(null);
  readonly users = signal<UserProfile[]>([]);

  // Role picked in the dropdown but not yet confirmed/saved, keyed by user id.
  readonly pendingRoles = signal<Record<string, UserRole>>({});
  readonly savingRoleUserId = signal<string | null>(null);

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

  roleFor(user: UserProfile): UserRole {
    return this.pendingRoles()[user.id] ?? user.role;
  }

  hasPendingRoleChange(user: UserProfile): boolean {
    const pending = this.pendingRoles()[user.id];
    return pending !== undefined && pending !== user.role;
  }

  selectRole(user: UserProfile, role: UserRole): void {
    this.pendingRoles.update((pending) => ({ ...pending, [user.id]: role }));
  }

  saveRole(user: UserProfile): void {
    const role = this.pendingRoles()[user.id];
    if (!role || role === user.role) {
      return;
    }
    this.dialog
      .open(ConfirmDialogComponent, {
        data: {
          title: 'Change role',
          message: `Change ${user.displayName}'s role from ${user.role} to ${role}?`,
          confirmText: 'Change role',
        },
      })
      .afterClosed()
      .subscribe((confirmed) => {
        if (confirmed) {
          this.assignRole(user, role);
        }
      });
  }

  private assignRole(user: UserProfile, role: UserRole): void {
    this.savingRoleUserId.set(user.id);
    this.userService
      .assignRole(user.id, role)
      .pipe(finalize(() => this.savingRoleUserId.set(null)))
      .subscribe({
        next: () => {
          this.users.set(this.users().map((u) => (u.id === user.id ? { ...u, role } : u)));
          this.pendingRoles.update(({ [user.id]: _removed, ...rest }) => rest);
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
