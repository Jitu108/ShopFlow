import { TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { MatDialog } from '@angular/material/dialog';
import { AdminUsers } from './admin-users';
import { UserService } from '../../../core/services/user';
import { UserProfile } from '../../../core/services/user.models';

const alice: UserProfile = {
  id: 'u1',
  email: 'alice@example.com',
  displayName: 'Alice',
  role: 'Customer',
  isEmailVerified: true,
};

describe('AdminUsers', () => {
  function createComponent(
    overrides: Partial<{ assignRole: () => ReturnType<UserService['assignRole']>; dialogConfirmed: boolean }> = {},
  ) {
    const dialogConfirmed = overrides.dialogConfirmed ?? true;
    TestBed.configureTestingModule({
      providers: [
        {
          provide: UserService,
          useValue: {
            searchUsers: () => of([alice]),
            assignRole: overrides.assignRole ?? (() => of(undefined)),
            resetPassword: () => of(undefined),
          },
        },
        {
          provide: MatDialog,
          useValue: {
            open: () => ({ afterClosed: () => of(dialogConfirmed) }),
          },
        },
      ],
    });
    const fixture = TestBed.createComponent(AdminUsers);
    fixture.detectChanges();
    return fixture.componentInstance;
  }

  it('loads users on init', () => {
    const component = createComponent();
    expect(component.users()).toEqual([alice]);
  });

  it('does not persist the role until the selection is saved', () => {
    const component = createComponent();
    component.selectRole(alice, 'Vendor');
    expect(component.hasPendingRoleChange(alice)).toBe(true);
    expect(component.users()).toEqual([alice]);
  });

  it('updates the role locally after confirming the save dialog', () => {
    const component = createComponent();
    component.selectRole(alice, 'Vendor');
    component.saveRole(alice);
    expect(component.users()).toEqual([{ ...alice, role: 'Vendor' }]);
    expect(component.hasPendingRoleChange(alice)).toBe(false);
  });

  it('leaves the role unsaved when the confirmation dialog is cancelled', () => {
    const component = createComponent({ dialogConfirmed: false });
    component.selectRole(alice, 'Vendor');
    component.saveRole(alice);
    expect(component.users()).toEqual([alice]);
    expect(component.hasPendingRoleChange(alice)).toBe(true);
  });

  it('shows the inline reset-password form only for the targeted user, and hides it on cancel', () => {
    const component = createComponent();
    component.startResetPassword(alice.id);
    expect(component.resettingUserId()).toBe(alice.id);
    component.cancelResetPassword();
    expect(component.resettingUserId()).toBeNull();
  });
});
