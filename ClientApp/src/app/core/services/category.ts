import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { Category, CreateCategoryRequest } from './category.models';

@Injectable({
  providedIn: 'root',
})
export class CategoryService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiBaseUrl}/api/categories`;

  getAll(): Observable<Category[]> {
    return this.http.get<Category[]>(this.baseUrl);
  }

  // Admin-only; used starting in Sub-phase 7.6.
  create(request: CreateCategoryRequest): Observable<Category> {
    return this.http.post<Category>(this.baseUrl, request);
  }
}
