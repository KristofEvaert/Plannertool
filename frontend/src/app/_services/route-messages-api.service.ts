import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '@environments/environment';
import type { CreateRouteMessageRequest, RouteMessageDto } from '@models/route-message.model';

@Injectable({ providedIn: 'root' })
export class RouteMessagesApiService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiBaseUrl}/api/route-messages`;

  getMessages(ownerId: number, status?: string, routeId?: number): Observable<RouteMessageDto[]> {
    let params = new HttpParams().set('ownerId', ownerId.toString());
    if (status) {
      params = params.set('status', status);
    }
    if (routeId) {
      params = params.set('routeId', routeId.toString());
    }
    return this.http.get<RouteMessageDto[]>(this.baseUrl, { params });
  }

  getDriverMessages(routeId: number): Observable<RouteMessageDto[]> {
    const params = new HttpParams().set('routeId', routeId.toString());
    return this.http.get<RouteMessageDto[]>(`${this.baseUrl}/driver`, { params });
  }

  createMessage(request: CreateRouteMessageRequest): Observable<RouteMessageDto> {
    return this.http.post<RouteMessageDto>(this.baseUrl, request);
  }

  markRead(messageId: number): Observable<void> {
    return this.http.post<void>(`${this.baseUrl}/${messageId}/read`, {});
  }

  markResolved(messageId: number): Observable<void> {
    return this.http.post<void>(`${this.baseUrl}/${messageId}/resolve`, {});
  }
}
