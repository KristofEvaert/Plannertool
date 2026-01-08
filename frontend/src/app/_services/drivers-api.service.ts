import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '@environments/environment';
import type { CreateDriverRequest, DriverDto, UpdateDriverRequest } from '@models';

@Injectable({ providedIn: 'root' })
export class DriversApiService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiBaseUrl}/api/drivers`;

  getDrivers(includeInactive = false): Observable<DriverDto[]> {
    let params = new HttpParams();
    if (includeInactive) {
      params = params.set('includeInactive', 'true');
    }
    return this.http.get<DriverDto[]>(this.baseUrl, { params });
  }

  getDriver(toolId: string): Observable<DriverDto> {
    return this.http.get<DriverDto>(`${this.baseUrl}/${toolId}`);
  }

  createDriver(request: CreateDriverRequest): Observable<DriverDto> {
    return this.http.post<DriverDto>(this.baseUrl, request);
  }

  updateDriver(toolId: string, request: UpdateDriverRequest): Observable<DriverDto> {
    return this.http.put<DriverDto>(`${this.baseUrl}/${toolId}`, request);
  }

  deleteDriver(toolId: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/${toolId}`);
  }
}
