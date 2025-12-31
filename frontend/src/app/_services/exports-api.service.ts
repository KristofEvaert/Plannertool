import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '@environments/environment';
import { toYmd } from '@utils/date.utils';

@Injectable({ providedIn: 'root' })
export class ExportsApiService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiBaseUrl}/api/exports`;

  exportRoutes(
    from: Date,
    to: Date,
    ownerId: number,
    serviceTypeId?: number
  ): Observable<Blob> {
    let params = new HttpParams()
      .set('from', toYmd(from))
      .set('to', toYmd(to))
      .set('ownerId', ownerId.toString());
    if (serviceTypeId) {
      params = params.set('serviceTypeId', serviceTypeId.toString());
    }
    return this.http.get(`${this.baseUrl}/routes`, { params, responseType: 'blob' });
  }
}
