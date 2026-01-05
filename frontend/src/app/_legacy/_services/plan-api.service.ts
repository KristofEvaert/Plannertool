import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '@environments/environment';
import type {
  DayOverviewDto,
  DriverDayDto,
  GeneratePlanRequest,
  GeneratePlanResultDto,
  PlanDaySettingsDto,
  SetExtraWorkMinutesRequest,
} from '@models/plan.model';

@Injectable({ providedIn: 'root' })
export class PlanApiService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiBaseUrl}/api/plan`;

  getDay(date: string, horizonDays = 14): Observable<DayOverviewDto> {
    return this.http.get<DayOverviewDto>(`${this.baseUrl}/day/${date}`, {
      params: { horizonDays: horizonDays.toString() },
    });
  }

  getDriverDay(date: string, driverId: number): Observable<DriverDayDto> {
    return this.http.get<DriverDayDto>(`${this.baseUrl}/day/${date}/drivers/${driverId}`);
  }

  generate(req: GeneratePlanRequest): Observable<GeneratePlanResultDto> {
    return this.http.post<GeneratePlanResultDto>(`${this.baseUrl}/generate`, req);
  }

  generateDay(date: string): Observable<GeneratePlanResultDto> {
    return this.http.post<GeneratePlanResultDto>(`${this.baseUrl}/generate/day/${date}`, {});
  }

  lockDay(date: string): Observable<void> {
    return this.http.post<void>(`${this.baseUrl}/day/${date}/lock`, {});
  }

  unlockDay(date: string): Observable<void> {
    return this.http.post<void>(`${this.baseUrl}/day/${date}/unlock`, {});
  }

  getDaySettings(date: string): Observable<PlanDaySettingsDto> {
    return this.http.get<PlanDaySettingsDto>(`${this.baseUrl}/day/${date}/settings`);
  }

  setDaySettings(date: string, req: SetExtraWorkMinutesRequest): Observable<PlanDaySettingsDto> {
    return this.http.post<PlanDaySettingsDto>(`${this.baseUrl}/day/${date}/settings`, req);
  }
}
