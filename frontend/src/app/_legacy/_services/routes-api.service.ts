import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '@environments/environment';
import type {
  AddStopNoteRequest,
  RouteActionResultDto,
  StopActionResultDto,
} from '@models/routes.model';

@Injectable({ providedIn: 'root' })
export class RoutesApiService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiBaseUrl}/api/routes`;

  startRoute(routeId: number): Observable<RouteActionResultDto> {
    return this.http.post<RouteActionResultDto>(`${this.baseUrl}/${routeId}/start`, {});
  }

  arriveStop(routeId: number, stopId: number): Observable<StopActionResultDto> {
    return this.http.post<StopActionResultDto>(
      `${this.baseUrl}/${routeId}/stops/${stopId}/arrive`,
      {},
    );
  }

  completeStop(routeId: number, stopId: number): Observable<StopActionResultDto> {
    return this.http.post<StopActionResultDto>(
      `${this.baseUrl}/${routeId}/stops/${stopId}/complete`,
      {},
    );
  }

  addStopNote(
    routeId: number,
    stopId: number,
    req: AddStopNoteRequest,
  ): Observable<StopActionResultDto> {
    return this.http.post<StopActionResultDto>(
      `${this.baseUrl}/${routeId}/stops/${stopId}/note`,
      req,
    );
  }
}
