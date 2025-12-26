import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '@environments/environment';
import type {
  DriverAvailabilityDto,
  UpsertAvailabilityRequest,
} from '@models/driver.model';

@Injectable({ providedIn: 'root' })
export class DriverAvailabilityApiService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiBaseUrl}/api/drivers`;

  getAvailability(
    toolId: string,
    fromYmd: string,
    toYmd: string
  ): Observable<DriverAvailabilityDto[]> {
    let params = new HttpParams()
      .set('from', fromYmd)
      .set('to', toYmd);
    return this.http.get<DriverAvailabilityDto[]>(
      `${this.baseUrl}/${toolId}/availability`,
      { params }
    );
  }

  upsertAvailability(
    toolId: string,
    dateYmd: string,
    request: UpsertAvailabilityRequest
  ): Observable<DriverAvailabilityDto> {
    return this.http.put<DriverAvailabilityDto>(
      `${this.baseUrl}/${toolId}/availability/${dateYmd}`,
      request
    );
  }

  deleteAvailability(toolId: string, dateYmd: string): Observable<void> {
    return this.http.delete<void>(
      `${this.baseUrl}/${toolId}/availability/${dateYmd}`
    );
  }
}

