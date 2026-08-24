import { Component, inject, signal } from '@angular/core';
import { ReactiveFormsModule, FormBuilder, Validators } from '@angular/forms';
import { finalize } from 'rxjs/operators';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatListModule } from '@angular/material/list';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { CategoryService } from '../../../core/services/category';
import { Category } from '../../../core/services/category.models';
import { extractErrorMessage } from '../../../core/http-error.util';

@Component({
  selector: 'app-admin-categories',
  imports: [
    ReactiveFormsModule,
    MatFormFieldModule,
    MatInputModule,
    MatButtonModule,
    MatListModule,
    MatProgressSpinnerModule,
  ],
  templateUrl: './admin-categories.html',
  styleUrl: './admin-categories.scss',
})
export class AdminCategories {
  private readonly categoryService = inject(CategoryService);
  private readonly fb = inject(FormBuilder);

  readonly loading = signal(true);
  readonly saving = signal(false);
  readonly error = signal<string | null>(null);
  readonly categories = signal<Category[]>([]);

  readonly nameControl = this.fb.nonNullable.control('', [Validators.required, Validators.maxLength(100)]);

  constructor() {
    this.load();
  }

  private load(): void {
    this.categoryService
      .getAll()
      .pipe(finalize(() => this.loading.set(false)))
      .subscribe({
        next: (categories) => this.categories.set(categories),
        error: (err) => this.error.set(extractErrorMessage(err)),
      });
  }

  create(): void {
    if (this.nameControl.invalid) {
      return;
    }
    this.saving.set(true);
    this.error.set(null);
    this.categoryService.create({ name: this.nameControl.value }).subscribe({
      next: (category) => {
        this.categories.set([...this.categories(), category]);
        this.nameControl.reset('');
        this.saving.set(false);
      },
      error: (err) => {
        this.error.set(extractErrorMessage(err));
        this.saving.set(false);
      },
    });
  }
}
