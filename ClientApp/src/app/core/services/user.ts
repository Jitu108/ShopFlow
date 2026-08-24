import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { UserProfile } from './user.models';

@Injectable({
  providedIn: 'root',
})
export class UserService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiBaseUrl}/api/admin/users`;

  searchUsers(name?: string): Observable<UserProfile[]> {
    const params = name ? new HttpParams().set('name', name) : undefined;
    return this.http.get<UserProfile[]>(this.baseUrl, { params });
  }

  assignRole(userId: string, role: string): Observable<void> {
    return this.http.post<void>(`${this.baseUrl}/${userId}/assign-role`, { role });
  }

  resetPassword(userId: string, newPassword: string): Observable<void> {
    return this.http.post<void>(`${this.baseUrl}/${userId}/reset-password`, { newPassword });
  }
}
