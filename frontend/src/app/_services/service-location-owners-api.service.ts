import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '@environments/environment';

export interface ServiceLocationOwnerDto {
  id: number;
  code: string;
  name: string;
  isActive: boolean;
}

export interface UpsertServiceLocationOwnerRequest {
  code: string;
  name: string;
  isActive: boolean;
}

@Injectable({ providedIn: 'root' })
export class ServiceLocationOwnersApiService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiBaseUrl}/api/service-location-owners`;

  getAll(includeInactive = false): Observable<ServiceLocationOwnerDto[]> {
    const params = new HttpParams().set('includeInactive', includeInactive.toString());
    return this.http.get<ServiceLocationOwnerDto[]>(this.baseUrl, { params });
  }

  create(request: UpsertServiceLocationOwnerRequest): Observable<ServiceLocationOwnerDto> {
    return this.http.post<ServiceLocationOwnerDto>(this.baseUrl, request);
  }

  update(
    id: number,
    request: UpsertServiceLocationOwnerRequest,
  ): Observable<ServiceLocationOwnerDto> {
    return this.http.put<ServiceLocationOwnerDto>(`${this.baseUrl}/${id}`, request);
  }

  delete(id: number): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/${id}`);
  }
}
