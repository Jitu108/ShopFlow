import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, of } from 'rxjs';
import { environment } from '../../../environments/environment';
import { VendorSummary } from './vendor.models';

@Injectable({
  providedIn: 'root',
})
export class VendorService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiBaseUrl}/api/vendors`;

  // Public, unauthenticated endpoint — returns only { id, displayName } for
  // Vendor-role users. Silently drops ids that don't resolve to a vendor.
  getNames(ids: readonly string[]): Observable<VendorSummary[]> {
    if (ids.length === 0) {
      return of([]);
    }
    return this.http.get<VendorSummary[]>(this.baseUrl, { params: { ids: ids.join(',') } });
  }
}
