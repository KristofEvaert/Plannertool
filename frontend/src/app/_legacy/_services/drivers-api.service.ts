import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '@environments/environment';
import type {
  DriverAvailabilityDto,
  DriverDto,
  UpdateDriverMaxWorkMinutesRequest,
} from '@models/driver.model';

@Injectable({ providedIn: 'root' })
export class DriversApiService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiBaseUrl}/api/drivers`;

  getDrivers(): Observable<DriverDto[]> {
    return this.http.get<DriverDto[]>(this.baseUrl);
  }

  getAvailability(
    driverId: number,
    from?: string,
    to?: string,
  ): Observable<DriverAvailabilityDto[]> {
    let params = new HttpParams();

    if (from) {
      params = params.set('from', from);
    }
    if (to) {
      params = params.set('to', to);
    }

    return this.http.get<DriverAvailabilityDto[]>(`${this.baseUrl}/${driverId}/availability`, {
      params,
    });
  }

  updateMaxWorkMinutes(driverId: number, minutes: number): Observable<DriverDto> {
    const request: UpdateDriverMaxWorkMinutesRequest = { maxWorkMinutesPerDay: minutes };
    return this.http.put<DriverDto>(`${this.baseUrl}/${driverId}/max-work-minutes`, request);
  }
}
