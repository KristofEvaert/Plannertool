import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '@environments/environment';
import { toYmd } from '@utils/date.utils';

export interface RouteStopDto {
  id: number;
  sequence: number;
  serviceLocationId?: number;
  serviceLocationToolId?: string; // Use ToolId (Guid as string) for matching
  name?: string;
  latitude: number;
  longitude: number;
  serviceMinutes: number;
  actualServiceMinutes?: number;
  travelKmFromPrev: number;
  travelMinutesFromPrev: number;
  status?: string;
  arrivedAtUtc?: string;
  completedAtUtc?: string;
  note?: string;
  driverInstruction?: string;
  remark?: string;
}

export interface RouteGeometryPointDto {
  lat: number;
  lng: number;
}

export interface RouteDto {
  id: number;
  date: string;
  ownerId: number;
  serviceTypeId: number;
  driverId: number;
  driverName: string;
  driverStartLatitude?: number;
  driverStartLongitude?: number;
  totalMinutes: number;
  totalKm: number;
  status: string;
  stops: RouteStopDto[];
  geometry?: RouteGeometryPointDto[];
}

export interface CreateRouteStopRequest {
  sequence: number;
  serviceLocationToolId?: string; // Use ToolId (Guid as string) instead of serviceLocationId
  latitude: number;
  longitude: number;
  serviceMinutes: number;
  travelKmFromPrev: number;
  travelMinutesFromPrev: number;
}

export interface CreateRouteRequest {
  date: string; // ISO date string
  ownerId: number;
  // serviceTypeId removed - routes are identified by date, driver, owner only
  driverToolId: string; // Use ToolId (Guid as string) instead of driverId
  totalMinutes: number;
  totalKm: number;
  stops: CreateRouteStopRequest[];
}

export interface UpdateRouteStopRequest {
  arrivedAtUtc?: string;
  completedAtUtc?: string;
  actualServiceMinutes?: number;
  note?: string;
  status?: string;
}

export interface AutoGenerateAllResponse {
  routes: RouteDto[];
  skippedDrivers: string[];
}

@Injectable({ providedIn: 'root' })
export class RoutesApiService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiBaseUrl}/api/routes`;

  getRoutes(
    date: Date,
    driverToolId: string,
    ownerId: number
  ): Observable<RouteDto[]> {
    const params = new HttpParams()
      // IMPORTANT: use local yyyy-MM-dd (not UTC via toISOString) to avoid off-by-one-day bugs
      .set('date', toYmd(date))
      .set('driverToolId', driverToolId)
      .set('ownerId', ownerId.toString());
    return this.http.get<RouteDto[]>(this.baseUrl, { params });
  }

  getDriverDayRoute(
    date: Date,
    driverToolId: string,
    ownerId: number,
    includeGeometry = true
  ): Observable<RouteDto | null> {
    const params = new HttpParams()
      .set('date', toYmd(date))
      .set('driverToolId', driverToolId)
      .set('ownerId', ownerId.toString())
      .set('includeGeometry', includeGeometry ? 'true' : 'false');
    return this.http.get<RouteDto | null>(`${this.baseUrl}/driver-day`, { params });
  }

  updateRouteStop(routeStopId: number, request: UpdateRouteStopRequest): Observable<RouteStopDto> {
    return this.http.patch<RouteStopDto>(`${this.baseUrl}/stops/${routeStopId}`, request);
  }

  upsertRoute(request: CreateRouteRequest): Observable<RouteDto> {
    return this.http.post<RouteDto>(this.baseUrl, request);
  }

  fixRoute(routeId: number): Observable<RouteDto> {
    return this.http.post<RouteDto>(`${this.baseUrl}/${routeId}/fix`, {});
  }

  fixDay(date: Date, ownerId: number): Observable<RouteDto[]> {
    const params = new HttpParams()
      // IMPORTANT: use local yyyy-MM-dd (not UTC via toISOString) to avoid off-by-one-day bugs
      .set('date', toYmd(date))
      .set('ownerId', ownerId.toString());
    return this.http.post<RouteDto[]>(`${this.baseUrl}/fix-day`, {}, { params });
  }

  autoGenerateRoute(date: Date, driverToolId: string, ownerId: number, serviceLocationToolIds: string[]): Observable<RouteDto> {
    const body = {
      date: toYmd(date),
      driverToolId,
      ownerId,
      serviceLocationToolIds,
    };
    return this.http.post<RouteDto>(`${this.baseUrl}/auto-generate`, body);
  }

  autoGenerateRoutesForAll(date: Date, ownerId: number, serviceLocationToolIds: string[]): Observable<AutoGenerateAllResponse> {
    const body = {
      date: toYmd(date),
      ownerId,
      serviceLocationToolIds,
    };
    return this.http.post<AutoGenerateAllResponse>(`${this.baseUrl}/auto-generate/all`, body);
  }
}
