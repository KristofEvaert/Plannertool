import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '@environments/environment';
import type { AuditTrailEntryDto, AuditTrailQueryParams, PagedResult } from '@models/audit-trail.model';

@Injectable({ providedIn: 'root' })
export class AuditTrailApiService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiBaseUrl}/api/audit-trail`;

  getList(params: AuditTrailQueryParams = {}): Observable<PagedResult<AuditTrailEntryDto>> {
    let httpParams = new HttpParams();

    if (params.fromUtc) {
      httpParams = httpParams.set('fromUtc', params.fromUtc);
    }
    if (params.toUtc) {
      httpParams = httpParams.set('toUtc', params.toUtc);
    }
    if (params.method) {
      httpParams = httpParams.set('method', params.method);
    }
    if (params.pathContains) {
      httpParams = httpParams.set('pathContains', params.pathContains);
    }
    if (params.userEmailContains) {
      httpParams = httpParams.set('userEmailContains', params.userEmailContains);
    }
    if (params.userId) {
      httpParams = httpParams.set('userId', params.userId);
    }
    if (params.statusCode != null) {
      httpParams = httpParams.set('statusCode', params.statusCode.toString());
    }
    if (params.ownerId != null) {
      httpParams = httpParams.set('ownerId', params.ownerId.toString());
    }
    if (params.search) {
      httpParams = httpParams.set('search', params.search);
    }
    if (params.page) {
      httpParams = httpParams.set('page', params.page.toString());
    }
    if (params.pageSize) {
      httpParams = httpParams.set('pageSize', params.pageSize.toString());
    }

    return this.http.get<PagedResult<AuditTrailEntryDto>>(this.baseUrl, { params: httpParams });
  }
}
