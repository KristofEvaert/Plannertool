import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '@environments/environment';
import type { SaveWeightTemplateRequest, WeightTemplateDto } from '@models/weight-template.model';

@Injectable({ providedIn: 'root' })
export class WeightTemplatesApiService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiBaseUrl}/api/weight-templates`;

  getAll(
    ownerId?: number,
    serviceTypeId?: number,
    includeInactive = false,
    includeGlobal = true,
  ): Observable<WeightTemplateDto[]> {
    let params = new HttpParams()
      .set('includeInactive', includeInactive.toString())
      .set('includeGlobal', includeGlobal.toString());
    if (ownerId) {
      params = params.set('ownerId', ownerId.toString());
    }
    if (serviceTypeId) {
      params = params.set('serviceTypeId', serviceTypeId.toString());
    }
    return this.http.get<WeightTemplateDto[]>(this.baseUrl, { params });
  }

  getById(id: number): Observable<WeightTemplateDto> {
    return this.http.get<WeightTemplateDto>(`${this.baseUrl}/${id}`);
  }

  create(request: SaveWeightTemplateRequest): Observable<WeightTemplateDto> {
    return this.http.post<WeightTemplateDto>(this.baseUrl, request);
  }

  update(id: number, request: SaveWeightTemplateRequest): Observable<WeightTemplateDto> {
    return this.http.put<WeightTemplateDto>(`${this.baseUrl}/${id}`, request);
  }

  delete(id: number): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/${id}`);
  }
}
