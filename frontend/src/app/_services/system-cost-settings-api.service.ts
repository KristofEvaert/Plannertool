import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '@environments/environment';
import type { SystemCostSettingsDto } from '@models/system-cost-settings.model';

@Injectable({ providedIn: 'root' })
export class SystemCostSettingsApiService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiBaseUrl}/api/system-cost-settings`;

  get(): Observable<SystemCostSettingsDto> {
    return this.http.get<SystemCostSettingsDto>(this.baseUrl);
  }

  update(request: SystemCostSettingsDto): Observable<SystemCostSettingsDto> {
    return this.http.put<SystemCostSettingsDto>(this.baseUrl, request);
  }
}
