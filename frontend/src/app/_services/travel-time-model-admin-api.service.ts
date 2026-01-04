import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '@environments/environment';
import type { TravelTimeModelLearnedStatDto, TravelTimeModelStatus } from '@models/travel-time-model-admin.model';

@Injectable({ providedIn: 'root' })
export class TravelTimeModelAdminApiService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiBaseUrl}/api/admin/travelTimeModel`;

  getLearned(): Observable<TravelTimeModelLearnedStatDto[]> {
    return this.http.get<TravelTimeModelLearnedStatDto[]>(`${this.baseUrl}/learned`);
  }

  updateStatus(id: number, status: TravelTimeModelStatus, note?: string | null): Observable<void> {
    return this.http.patch<void>(`${this.baseUrl}/learned/${id}/status`, {
      status,
      note: note ?? null,
    });
  }

  resetBucket(id: number): Observable<void> {
    return this.http.post<void>(`${this.baseUrl}/learned/${id}/reset`, {});
  }
}
