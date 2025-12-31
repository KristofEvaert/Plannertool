import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '@environments/environment';
import type { RouteChangeNotificationDto } from '@models/route-change-notification.model';

@Injectable({ providedIn: 'root' })
export class RouteChangeNotificationsApiService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiBaseUrl}/api/route-change-notifications`;

  getNotifications(routeId?: number, includeAcknowledged = false): Observable<RouteChangeNotificationDto[]> {
    let params = new HttpParams().set('includeAcknowledged', includeAcknowledged.toString());
    if (routeId) {
      params = params.set('routeId', routeId.toString());
    }
    return this.http.get<RouteChangeNotificationDto[]>(this.baseUrl, { params });
  }

  acknowledge(notificationId: number): Observable<void> {
    return this.http.post<void>(`${this.baseUrl}/${notificationId}/ack`, {});
  }
}
