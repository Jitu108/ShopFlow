---
name: vitest-component-testing
description: How ClientApp unit tests are written with Vitest + Angular TestBed — isolation via provideStore/provideRouter/service stubs, HttpTestingController for interceptors, and signal-based assertions. Use when writing or changing a .spec.ts file under ClientApp/src/app.
---

# Vitest Component/Service Testing

ClientApp uses **Vitest** (`@angular/build:unit-test`) as its test runner, not Karma/Jasmine — `describe`/`it`/`expect` come from Vitest, and there is no `karma.conf.js` to look for. Every `.ts` file with meaningful logic has a co-located `.spec.ts` — this is the same TDD-by-convention rule as the backend (see [[dotnet-backend-conventions]]).

## Isolating a component under test

Build the component through `TestBed.configureTestingModule` with **narrow, explicit providers** — real NgRx reducers via `provideStore({...})` (so selectors resolve correctly) alongside `useValue` stubs for services, rather than a full app-level TestBed setup:

```ts
TestBed.configureTestingModule({
  providers: [
    provideRouter([]),
    provideStore({ auth: authReducer, cart: cartReducer }),
    { provide: ProductService, useValue: { getAll: () => of(products) } },
    { provide: CategoryService, useValue: { getAll: () => of(categories) } },
  ],
});
const fixture = TestBed.createComponent(CatalogList);
fixture.detectChanges();
```

Wrap this in a local `createComponent()` helper inside the `describe` block when more than one test needs it — don't duplicate the TestBed setup per test.

## Asserting on signal-based component state

Component-local state is exposed as signals (see [[angular-material-conventions]]); read it by calling the signal, not by inspecting a plain property: `expect(component.loading()).toBe(false)`, `expect(component.filteredProducts()).toEqual([...])`.

## Testing HTTP interceptors and services

Use `provideHttpClient(withInterceptors([...]))` + `provideHttpClientTesting()`, inject `HttpTestingController`, and always call `httpMock.verify()` in `afterEach` to catch unflushed/unexpected requests. Drive the request with `firstValueFrom(http.get(...))`, assert on `httpMock.expectOne(url).request`, then `req.flush(...)` before awaiting the promise — don't assert before flushing, the request won't have resolved yet.

## Table-driven cases

Use `it.each([...])('description %s', async (value) => {...})` for the same assertion repeated over multiple inputs (e.g. every anonymous auth endpoint that must never get a bearer header) instead of copy-pasting near-identical `it` blocks.

## What NOT to do

- Don't spin up the full routed app (`bootstrapApplication`) to test one component — configure only the providers that component actually needs.
- Don't assert against a store's internal state directly; go through the same selectors the component uses.
- Don't leave an HTTP interceptor/service test without `httpMock.verify()` — a missed `expectOne` fails silently otherwise.
