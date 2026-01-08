import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '@environments/environment';
import type {
  ServiceTypeDto,
  CreateServiceTypeRequest,
  UpdateServiceTypeRequest,
} from '@models';

@Injectable({ providedIn: 'root' })
export class ServiceTypesApiService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiBaseUrl}/api/service-types`;

  getAll(includeInactive = false, ownerId?: number): Observable<ServiceTypeDto[]> {
    let params = new HttpParams().set('includeInactive', includeInactive.toString());
    if (ownerId && ownerId > 0) {
      params = params.set('ownerId', ownerId.toString());
    }
    return this.http.get<ServiceTypeDto[]>(this.baseUrl, { params });
  }

  getById(id: number): Observable<ServiceTypeDto> {
    return this.http.get<ServiceTypeDto>(`${this.baseUrl}/${id}`);
  }

  create(request: CreateServiceTypeRequest): Observable<ServiceTypeDto> {
    return this.http.post<ServiceTypeDto>(this.baseUrl, request);
  }

  update(id: number, request: UpdateServiceTypeRequest): Observable<ServiceTypeDto> {
    return this.http.put<ServiceTypeDto>(`${this.baseUrl}/${id}`, request);
  }

  activate(id: number): Observable<ServiceTypeDto> {
    return this.http.post<ServiceTypeDto>(`${this.baseUrl}/${id}/activate`, {});
  }

  deactivate(id: number): Observable<ServiceTypeDto> {
    return this.http.post<ServiceTypeDto>(`${this.baseUrl}/${id}/deactivate`, {});
  }
}
