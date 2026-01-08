import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '@environments/environment';
import type {
  SystemCostSettingsDto,
  SystemCostSettingsOverviewDto,
} from '@models';

@Injectable({ providedIn: 'root' })
export class SystemCostSettingsApiService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiBaseUrl}/api/system-cost-settings`;

  get(ownerId?: number | null): Observable<SystemCostSettingsDto> {
    let params = new HttpParams();
    if (ownerId !== null && ownerId !== undefined) {
      params = params.set('ownerId', ownerId.toString());
    }
    return this.http.get<SystemCostSettingsDto>(this.baseUrl, { params });
  }

  update(
    request: SystemCostSettingsDto,
    ownerId?: number | null,
  ): Observable<SystemCostSettingsDto> {
    let params = new HttpParams();
    if (ownerId !== null && ownerId !== undefined) {
      params = params.set('ownerId', ownerId.toString());
    }
    return this.http.put<SystemCostSettingsDto>(this.baseUrl, request, { params });
  }
  getOverview(includeInactive = true): Observable<SystemCostSettingsOverviewDto[]> {
    const params = new HttpParams().set('includeInactive', includeInactive.toString());
    return this.http.get<SystemCostSettingsOverviewDto[]>(`${this.baseUrl}/overview`, { params });
  }
}
