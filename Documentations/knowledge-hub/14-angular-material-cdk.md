# Angular Material & CDK

## Abstract

`ClientApp` is built on Angular 21 with standalone components and signals, styled with Angular Material 21 (Google's Material Design 3 component library) and its underlying CDK (Component Dev Kit). This document covers what each piece is, why ShopFlow uses them instead of hand-rolled UI, and how a real component wires up its Material imports, the app-wide Material 3 theme, and a dialog built on Material's `MatDialog`.

## What it is

**Angular** is a component-based single-page-application framework. ShopFlow's `ClientApp` uses Angular's modern idioms throughout: standalone components (no `NgModule` declarations — a component lists its own `imports: [...]`), signals for local reactive state (`signal()`, `input()`, `effect()`), and functional guards/interceptors rather than class-based ones. See [ClientApp/package.json](../../ClientApp/package.json), which pins `@angular/core` to `^21.2.0`.

**Angular Material** is Google's official Material Design component library for Angular — buttons, form fields, cards, dialogs, toolbars, tables, and so on, each shipped as its own standalone module/component pair (`MatButtonModule`, `MatDialogModule`, ...). **CDK (Component Dev Kit)** is the lower-level behavior library Material is built on: overlay positioning, focus trapping/`a11y`, drag-drop, layout observers — primitives without any visual opinion, which Material's own components consume internally (`MatDialog` is implemented on top of the CDK's overlay and focus-trap machinery, for example). Both ship as separate packages, `@angular/material` and `@angular/cdk`, both pinned to `^21.2.14` in [ClientApp/package.json](../../ClientApp/package.json).

## Why ShopFlow uses them

1. **Consistent Material 3 theming across three very different UIs.** The same customer/vendor/admin Angular app renders a public catalog, vendor CRUD forms, and admin management screens — all need to look like one product, not three. A single `mat.theme()` call in [ClientApp/src/styles.scss](../../ClientApp/src/styles.scss) generates the CSS custom properties (`--mat-sys-*`) every Material component reads, so `mat-card`, `mat-button`, and `mat-toolbar` are visually consistent everywhere they're used without each feature area maintaining its own CSS.
2. **Accessible components out of the box.** A hand-rolled dropdown or dialog has to reimplement focus trapping, `aria-*` attributes, and keyboard navigation correctly; `MatSelect` and `MatDialog` already do this (via CDK underneath). ShopFlow's admin role-change flow, for instance, is a real destructive action gated behind a dialog (see [confirm-dialog.ts](../../ClientApp/src/app/shared/components/confirm-dialog/confirm-dialog.ts)) — getting focus-trap and Escape-to-cancel right for free is the actual payoff, not just visual polish.
3. **Per-component imports keep bundles lean.** Because Material ships each component as its own standalone import rather than one big module, a component like the catalog list never pays for `MatDialogModule` or `MatChipsModule` it doesn't use — see the next section.

## How it's used

### Material 3 theming — set once, in `styles.scss`

[ClientApp/src/styles.scss](../../ClientApp/src/styles.scss) is the *only* place `mat.theme()` is called:

```scss
@use '@angular/material' as mat;

html {
  height: 100%;
  @include mat.theme(
    (
      color: (
        primary: mat.$azure-palette,
        tertiary: mat.$blue-palette,
      ),
      typography: Roboto,
      density: 0,
    )
  );
}

body {
  color-scheme: light;
  background-color: var(--mat-sys-surface);
  color: var(--mat-sys-on-surface);
  font: var(--mat-sys-body-medium);
  margin: 0;
  height: 100%;
}
```

`mat.theme()` generates the Material 3 system-level CSS variables (`--mat-sys-surface`, `--mat-sys-on-surface`, `--mat-sys-body-medium`, ...) from the `azure`/`blue` palettes. Every Material component reads these variables internally, and `body` itself is styled from the same tokens rather than a hardcoded hex value — the house convention (documented in the `angular-material-conventions` skill) is that no other file calls `mat.theme()` again, and component styles reference `var(--mat-sys-*)` rather than duplicating a color.

### No shared `MaterialModule` — each component imports exactly what it uses

There is no barrel module. [vendor-product-form.ts](../../ClientApp/src/app/vendor/products/vendor-product-form/vendor-product-form.ts) is a form, so it imports the form-related set:

```ts
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';

@Component({
  selector: 'app-vendor-product-form',
  imports: [
    ReactiveFormsModule,
    RouterLink,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
    MatButtonModule,
    MatCardModule,
    MatProgressSpinnerModule,
  ],
  templateUrl: './vendor-product-form.html',
  styleUrl: './vendor-product-form.scss',
})
export class VendorProductForm { ... }
```

By contrast, the small confirmation dialog below imports only two modules — `MatDialogModule` and `MatButtonModule` — because that is all it renders. This per-component list is deliberate: importing a broad convenience module would pull in code every consumer pays for even when it renders three components' worth of Material.

Also visible in `VendorProductForm`'s constructor is the house pattern for "load data when a route param changes" — a route param exposed as an `input<string>()` signal, combined with `effect()`, instead of subscribing to `ActivatedRoute.paramMap`:

```ts
readonly id = input<string>();
...
constructor() {
  effect(() => {
    const id = this.id();
    if (!id) return;
    this.loading.set(true);
    this.productService.getById(id).subscribe({ ... });
  });
}
```

### A dialog built on `MatDialog`

[confirm-dialog.ts](../../ClientApp/src/app/shared/components/confirm-dialog/confirm-dialog.ts) is its own small standalone component. Data flows in via `MAT_DIALOG_DATA`, typed to an exported interface, and each button closes the dialog with a typed result via `[mat-dialog-close]`:

```ts
import { MatDialogModule, MAT_DIALOG_DATA } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';

export interface ConfirmDialogData {
  title: string;
  message: string;
  confirmText?: string;
  cancelText?: string;
}

@Component({
  selector: 'app-confirm-dialog',
  imports: [MatDialogModule, MatButtonModule],
  template: `
    <h2 mat-dialog-title>{{ data.title }}</h2>
    <mat-dialog-content>{{ data.message }}</mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button [mat-dialog-close]="false">{{ data.cancelText ?? 'Cancel' }}</button>
      <button mat-flat-button color="primary" [mat-dialog-close]="true">
        {{ data.confirmText ?? 'Confirm' }}
      </button>
    </mat-dialog-actions>
  `,
})
export class ConfirmDialogComponent {
  readonly data = inject<ConfirmDialogData>(MAT_DIALOG_DATA);
}
```

The caller opens it and reads the result from the closed observable rather than the dialog reaching back into app state itself — [admin-users.ts](../../ClientApp/src/app/admin/users/admin-users/admin-users.ts) uses this for a real destructive confirmation (changing a user's role):

```ts
private readonly dialog = inject(MatDialog);
...
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
    if (confirmed) this.assignRole(user, role);
  });
```

`MatDialog.open()` is itself the CDK usage that matters most in this codebase in practice: internally it opens a `CdkDialog`/`Overlay` and manages focus-trap and keyboard handling (Escape to close, focus returned to the triggering element on close) — none of which `admin-users.ts` or `confirm-dialog.ts` implement themselves.

### Component-local state stays in signals, not the store

Page/form-local state — `loading`, `saving`, `error`, in-flight form data — is plain `signal()`s written to directly from `subscribe` callbacks, as seen throughout `admin-users.ts` and `vendor-product-form.ts`:

```ts
readonly loading = signal(false);
readonly error = signal<string | null>(null);
...
this.productService.getById(id).subscribe({
  next: (product) => { this.form.patchValue(...); this.loading.set(false); },
  error: (err) => { this.error.set(extractErrorMessage(err)); this.loading.set(false); },
});
```

This is a deliberate split from NgRx (covered in [15-ngrx-state-management.md](./15-ngrx-state-management.md)): state that's read and written from exactly one component doesn't need a reducer/selector/effect triple around it.

## Gotchas & deviations

- **`@angular/cdk` is a direct dependency but not directly imported anywhere in app code today.** A repo-wide search for `@angular/cdk/...` imports and `cdk*` template attributes across `ClientApp/src/app` (excluding specs) turns up nothing — every CDK behavior currently in use (overlay positioning, focus trapping) comes in indirectly through `MatDialogModule`. If a future component needs a bare CDK primitive directly (e.g. `Overlay`, `FocusTrap`, `cdkDropList`), it would be the first direct `@angular/cdk` import in the codebase — don't assume an existing pattern to copy.
- **No shared Material barrel module exists on purpose.** Don't add a `MaterialModule` that re-exports commonly used pieces — the house convention is one Material import list per component, matched to what that component actually renders.
- Reactive forms always use `this.fb.nonNullable.group({...})` (never the plain, nullable `FormBuilder.group`), and `save()`/submit handlers guard with `if (this.form.invalid) return;` before calling the API — see `vendor-product-form.ts` and `admin-users.ts`'s `submitResetPassword()`.
