import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '@environments/environment';
import type { PagedResultDto } from '@models/paging.model';
import type { PoleListItemDto, SetFixedDateRequest } from '@models/pole.model';

@Injectable({ providedIn: 'root' })
export class PolesApiService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiBaseUrl}/api/poles`;

  getPoles(filters: {
    from?: string;
    to?: string;
    status?: string;
    hasFixedDate?: boolean;
    search?: string;
    page?: number;
    pageSize?: number;
  }): Observable<PagedResultDto<PoleListItemDto>> {
    let params = new HttpParams();

    if (filters.from) {
      params = params.set('from', filters.from);
    }
    if (filters.to) {
      params = params.set('to', filters.to);
    }
    if (filters.status) {
      params = params.set('status', filters.status);
    }
    if (filters.hasFixedDate !== undefined) {
      params = params.set('hasFixedDate', filters.hasFixedDate.toString());
    }
    if (filters.search) {
      params = params.set('search', filters.search);
    }
    if (filters.page !== undefined) {
      params = params.set('page', filters.page.toString());
    }
    if (filters.pageSize !== undefined) {
      params = params.set('pageSize', filters.pageSize.toString());
    }

    return this.http.get<PagedResultDto<PoleListItemDto>>(this.baseUrl, { params });
  }

  fixDate(poleId: number, req: SetFixedDateRequest): Observable<void> {
    return this.http.post<void>(`${this.baseUrl}/${poleId}/fixdate`, req);
  }

  unfixDate(poleId: number): Observable<void> {
    return this.http.post<void>(`${this.baseUrl}/${poleId}/unfixdate`, {});
  }
}
