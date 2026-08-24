import { HttpErrorResponse } from '@angular/common/http';

// Every ShopFlow service (Identity, Product, Cart, Order) uses the same
// ExceptionHandlingMiddleware shape:
// FluentValidation -> { errors: [{ propertyName, errorMessage }] }
// everything else  -> { message }
export function extractErrorMessage(error: unknown): string {
  if (error instanceof HttpErrorResponse) {
    const body = error.error as { errors?: { errorMessage: string }[]; message?: string } | null;
    if (body?.errors?.length) {
      return body.errors.map((e) => e.errorMessage).join(' ');
    }
    if (body?.message) {
      return body.message;
    }
  }
  return 'Something went wrong. Please try again.';
}
