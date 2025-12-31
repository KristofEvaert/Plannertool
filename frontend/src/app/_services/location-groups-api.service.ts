import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '@environments/environment';
import type { LocationGroupDto, SaveLocationGroupRequest } from '@models/location-group.model';

@Injectable({ providedIn: 'root' })
export class LocationGroupsApiService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiBaseUrl}/api/location-groups`;

  getAll(ownerId?: number): Observable<LocationGroupDto[]> {
    let params = new HttpParams();
    if (ownerId) {
      params = params.set('ownerId', ownerId.toString());
    }
    return this.http.get<LocationGroupDto[]>(this.baseUrl, { params });
  }

  getById(id: number): Observable<LocationGroupDto> {
    return this.http.get<LocationGroupDto>(`${this.baseUrl}/${id}`);
  }

  create(request: SaveLocationGroupRequest): Observable<LocationGroupDto> {
    return this.http.post<LocationGroupDto>(this.baseUrl, request);
  }

  update(id: number, request: SaveLocationGroupRequest): Observable<LocationGroupDto> {
    return this.http.put<LocationGroupDto>(`${this.baseUrl}/${id}`, request);
  }

  delete(id: number): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/${id}`);
  }
}
