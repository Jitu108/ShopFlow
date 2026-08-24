---
name: angular-material-conventions
description: How Angular Material/CDK and component-local state are used in ClientApp — per-component module imports, Material 3 theming, dialogs, and signal-based local state. Use when building or editing a standalone component, a form, or a dialog under ClientApp/src/app.
---

# Angular Material & Component Conventions

## Theming

Material 3 theming lives once, in [ClientApp/src/styles.scss](../../../ClientApp/src/styles.scss), via `@include mat.theme((color: (primary: ..., tertiary: ...), typography: Roboto, density: 0))`. Components style themselves with the resulting `--mat-sys-*` CSS variables (e.g. `var(--mat-sys-surface)`, `var(--mat-sys-on-surface)`) — never hardcode a hex color that duplicates a theme token, and don't add a second `mat.theme()` call anywhere else.

## Per-component Material imports

There is no shared `MaterialModule` barrel. Each standalone component's `imports: [...]` lists exactly the Material modules it uses, e.g. a form pulls in `MatFormFieldModule, MatInputModule, MatSelectModule, MatButtonModule, MatCardModule, MatProgressSpinnerModule` (see [vendor-product-form.ts](../../../ClientApp/src/app/vendor/products/vendor-product-form/vendor-product-form.ts)) while a small dialog pulls in only `MatDialogModule, MatButtonModule` (see [confirm-dialog.ts](../../../ClientApp/src/app/shared/components/confirm-dialog/confirm-dialog.ts)). Import the specific module for each component you use, not a broader convenience re-export.

## Dialogs

A confirmation/prompt dialog is its own small standalone component; data flows in via `MAT_DIALOG_DATA` typed to an exported interface (`ConfirmDialogData`), and each action closes the dialog with a typed result via `[mat-dialog-close]="true/false"` — the caller opens it with `MatDialog.open(ConfirmDialogComponent, { data })` and reads the result from the closed observable, rather than the dialog reaching back into app state itself.

## Component-local state: signals, not just NgRx

Not everything belongs in the NgRx store (see [[angular-ngrx-conventions]] for what does — shared/cross-page state like cart and auth). Page/form-local state — `loading`, `saving`, `error`, in-flight form data — is plain Angular `signal()`s on the component, written to directly from subscribe callbacks:

```ts
readonly loading = signal(false);
readonly error = signal<string | null>(null);
...
this.productService.getById(id).subscribe({
  next: (product) => { this.form.patchValue(...); this.loading.set(false); },
  error: (err) => { this.error.set(extractErrorMessage(err)); this.loading.set(false); },
});
```

A route param exposed as an `input<string>()` signal, combined with `effect()`, is the pattern for "load data when this input changes" (see `vendor-product-form.ts`'s constructor) — don't reach for `ActivatedRoute.paramMap` subscriptions when the component already receives the param as a signal input.

## Forms

Reactive forms use `this.fb.nonNullable.group({...})` (never the plain, nullable `FormBuilder.group`) with `Validators` inline per field. Read the submitted value with `form.getRawValue()`. Always guard `save()`/submit handlers with `if (this.form.invalid) return;` before calling the API.

## Errors from HTTP calls

Always convert an HTTP error through `extractErrorMessage(err)` (from `core/http-error.util`) before putting it in an `error` signal or NgRx `Failure` action payload — don't pass the raw `HttpErrorResponse` to the template.
