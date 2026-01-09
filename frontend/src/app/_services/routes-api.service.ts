import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '@environments/environment';
import { toYmd } from '@utils/date.utils';
import {
  AutoGenerateAllResponse,
  CreateRouteRequest,
  RouteDto,
  RouteStopDto,
  UpdateRouteStopRequest,
} from '@models';

@Injectable({ providedIn: 'root' })
export class RoutesApiService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiBaseUrl}/api/routes`;
  private readonly routeStopsUrl = `${environment.apiBaseUrl}/api/routeStops`;

  getRoutes(date: Date, driverToolId: string, ownerId: number): Observable<RouteDto[]> {
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
    includeGeometry = true,
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

  arriveStop(routeStopId: number, arrivedAtUtc?: string): Observable<RouteStopDto> {
    return this.http.post<RouteStopDto>(
      `${environment.apiBaseUrl}/api/routeStops/${routeStopId}/arrive`,
      {
        arrivedAtUtc,
      },
    );
  }

  departStop(routeStopId: number, departedAtUtc?: string): Observable<RouteStopDto> {
    return this.http.post<RouteStopDto>(
      `${environment.apiBaseUrl}/api/routeStops/${routeStopId}/depart`,
      {
        departedAtUtc,
      },
    );
  }

  getRouteStopProofPhoto(routeStopId: number): Observable<Blob> {
    return this.http.get(`${this.routeStopsUrl}/${routeStopId}/proof/photo`, {
      responseType: 'blob',
    });
  }

  getRouteStopProofSignature(routeStopId: number): Observable<Blob> {
    return this.http.get(`${this.routeStopsUrl}/${routeStopId}/proof/signature`, {
      responseType: 'blob',
    });
  }

  uploadRouteStopProofPhoto(routeStopId: number, file: File): Observable<RouteStopDto> {
    const formData = new FormData();
    formData.append('file', file);
    return this.http.post<RouteStopDto>(
      `${this.routeStopsUrl}/${routeStopId}/proof/photo`,
      formData,
    );
  }

  uploadRouteStopProofSignature(routeStopId: number, file: File): Observable<RouteStopDto> {
    const formData = new FormData();
    formData.append('file', file);
    return this.http.post<RouteStopDto>(
      `${this.routeStopsUrl}/${routeStopId}/proof/signature`,
      formData,
    );
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

  deleteDriverDayRoute(date: Date, driverToolId: string, ownerId: number): Observable<void> {
    const params = new HttpParams()
      .set('date', toYmd(date))
      .set('driverToolId', driverToolId)
      .set('ownerId', ownerId.toString());
    return this.http.delete<void>(`${this.baseUrl}/driver-day`, { params });
  }

  deleteDayRoutes(
    date: Date,
    ownerId: number,
  ): Observable<{ deletedRoutes: number; skippedFixed: number }> {
    const params = new HttpParams().set('date', toYmd(date)).set('ownerId', ownerId.toString());
    return this.http.delete<{ deletedRoutes: number; skippedFixed: number }>(
      `${this.baseUrl}/day`,
      { params },
    );
  }

  autoGenerateRoute(
    date: Date,
    driverToolId: string,
    ownerId: number,
    serviceLocationToolIds: string[],
    weights?: { time: number; distance: number; date: number; cost: number; overtime: number },
    solverCaps?: {
      dueCostCapPercent: number;
      detourCostCapPercent: number;
      detourRefKmPercent: number;
      lateRefMinutesPercent: number;
    },
    requireServiceTypeMatch?: boolean,
    normalizeWeights?: boolean,
    weightTemplateId?: number,
  ): Observable<RouteDto> {
    const body = {
      date: toYmd(date),
      driverToolId,
      ownerId,
      serviceLocationToolIds,
      weightTime: weights?.time,
      weightDistance: weights?.distance,
      weightDate: weights?.date,
      weightCost: weights?.cost,
      weightOvertime: weights?.overtime,
      dueCostCapPercent: solverCaps?.dueCostCapPercent,
      detourCostCapPercent: solverCaps?.detourCostCapPercent,
      detourRefKmPercent: solverCaps?.detourRefKmPercent,
      lateRefMinutesPercent: solverCaps?.lateRefMinutesPercent,
      weightTemplateId,
      requireServiceTypeMatch,
      normalizeWeights,
    };
    return this.http.post<RouteDto>(`${this.baseUrl}/auto-generate`, body);
  }

  autoGenerateRoutesForAll(
    date: Date,
    ownerId: number,
    serviceLocationToolIds: string[],
    weights?: { time: number; distance: number; date: number; cost: number; overtime: number },
    solverCaps?: {
      dueCostCapPercent: number;
      detourCostCapPercent: number;
      detourRefKmPercent: number;
      lateRefMinutesPercent: number;
    },
    requireServiceTypeMatch?: boolean,
    normalizeWeights?: boolean,
    weightTemplateId?: number,
  ): Observable<AutoGenerateAllResponse> {
    const body = {
      date: toYmd(date),
      ownerId,
      serviceLocationToolIds,
      weightTime: weights?.time,
      weightDistance: weights?.distance,
      weightDate: weights?.date,
      weightCost: weights?.cost,
      weightOvertime: weights?.overtime,
      dueCostCapPercent: solverCaps?.dueCostCapPercent,
      detourCostCapPercent: solverCaps?.detourCostCapPercent,
      detourRefKmPercent: solverCaps?.detourRefKmPercent,
      lateRefMinutesPercent: solverCaps?.lateRefMinutesPercent,
      weightTemplateId,
      requireServiceTypeMatch,
      normalizeWeights,
    };
    return this.http.post<AutoGenerateAllResponse>(`${this.baseUrl}/auto-generate/all`, body);
  }
}
