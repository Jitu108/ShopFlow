import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { provideMockActions } from '@ngrx/effects/testing';
import { Subject, firstValueFrom, take } from 'rxjs';
import { CartEffects } from './cart.effects';
import { CartActions } from './cart.actions';
import { AuthActions } from '../../auth/store/auth.actions';
import { AuthUser } from '../../auth/auth.models';

describe('CartEffects', () => {
  let actions$: Subject<unknown>;
  let effects: CartEffects;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    actions$ = new Subject();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        CartEffects,
        provideMockActions(() => actions$),
      ],
    });
    effects = TestBed.inject(CartEffects);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('logoutComplete dispatches a local resetState, not a real ClearCart (must not empty the saved server-side cart)', async () => {
    const emitted = firstValueFrom(effects.resetOnLogout$.pipe(take(1)));
    actions$.next(AuthActions.logoutComplete());
    expect(await emitted).toEqual(CartActions.resetState());
    httpMock.expectNone('/api/cart');
  });

  it('loadCartOnAuth$ fires for a Customer login but not for a Vendor login', async () => {
    const customer: AuthUser = {
      userId: 'u1',
      email: 'c@example.com',
      displayName: 'Cust',
      role: 'Customer',
      emailVerified: true,
    };
    const vendor: AuthUser = { ...customer, role: 'Vendor' };

    const results: unknown[] = [];
    const sub = effects.loadCartOnAuth$.subscribe((action) => results.push(action));

    actions$.next(AuthActions.loginSuccess({ user: vendor }));
    actions$.next(AuthActions.loginSuccess({ user: customer }));

    expect(results).toEqual([CartActions.loadCart()]);
    sub.unsubscribe();
  });
});
