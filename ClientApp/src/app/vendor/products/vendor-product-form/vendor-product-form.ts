import { Component, effect, inject, input, signal } from '@angular/core';
import { ReactiveFormsModule, FormBuilder, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { ProductService } from '../../../core/services/product';
import { CategoryService } from '../../../core/services/category';
import { Category } from '../../../core/services/category.models';
import { extractErrorMessage } from '../../../core/http-error.util';

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
export class VendorProductForm {
  private readonly fb = inject(FormBuilder);
  private readonly productService = inject(ProductService);
  private readonly categoryService = inject(CategoryService);
  private readonly router = inject(Router);

  // Present on the edit route (products/:id/edit), absent on products/new.
  readonly id = input<string>();

  readonly categories = signal<Category[]>([]);
  readonly loading = signal(false);
  readonly saving = signal(false);
  readonly error = signal<string | null>(null);

  readonly form = this.fb.nonNullable.group({
    name: ['', [Validators.required, Validators.maxLength(200)]],
    description: [''],
    price: [0, [Validators.required, Validators.min(0)]],
    stockQuantity: [0, [Validators.required, Validators.min(0)]],
    categoryId: ['', Validators.required],
  });

  constructor() {
    this.categoryService.getAll().subscribe((categories) => this.categories.set(categories));

    effect(() => {
      const id = this.id();
      if (!id) {
        return;
      }
      this.loading.set(true);
      this.productService.getById(id).subscribe({
        next: (product) => {
          this.form.patchValue({
            name: product.name,
            description: product.description,
            price: product.price,
            stockQuantity: product.stockQuantity,
            categoryId: product.categoryId,
          });
          this.loading.set(false);
        },
        error: (err) => {
          this.error.set(extractErrorMessage(err));
          this.loading.set(false);
        },
      });
    });
  }

  save(): void {
    if (this.form.invalid) {
      return;
    }
    this.saving.set(true);
    this.error.set(null);

    const value = this.form.getRawValue();
    const id = this.id();
    const request = id ? this.productService.update(id, value) : this.productService.create(value);

    request.subscribe({
      next: () => {
        this.saving.set(false);
        this.router.navigateByUrl('/vendor/products');
      },
      error: (err) => {
        this.saving.set(false);
        this.error.set(extractErrorMessage(err));
      },
    });
  }
}
