import { TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
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
  function createComponent(overrides: Partial<{ assignRole: () => ReturnType<UserService['assignRole']> }> = {}) {
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

  it('updates the role locally after a successful assign-role call', () => {
    const component = createComponent();
    component.assignRole(alice, 'Vendor');
    expect(component.users()).toEqual([{ ...alice, role: 'Vendor' }]);
  });

  it('shows the inline reset-password form only for the targeted user, and hides it on cancel', () => {
    const component = createComponent();
    component.startResetPassword(alice.id);
    expect(component.resettingUserId()).toBe(alice.id);
    component.cancelResetPassword();
    expect(component.resettingUserId()).toBeNull();
  });
});
