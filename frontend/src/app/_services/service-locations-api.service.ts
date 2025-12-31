import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '@environments/environment';
import type {
  ServiceLocationDto,
  CreateServiceLocationRequest,
  UpdateServiceLocationRequest,
  SetPriorityDateRequest,
  PagedResult,
  ServiceLocationListParams,
  BulkInsertServiceLocationsRequest,
  BulkInsertResultDto,
  ServiceLocationOpeningHoursDto,
  ServiceLocationExceptionDto,
  ServiceLocationConstraintDto,
} from '@models/service-location.model';

@Injectable({ providedIn: 'root' })
export class ServiceLocationsApiService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiBaseUrl}/api/service-locations`;

  getList(params: ServiceLocationListParams = {}): Observable<PagedResult<ServiceLocationDto>> {
    let httpParams = new HttpParams();
    
    if (params.status) {
      httpParams = httpParams.set('status', params.status);
    }
    if (params.search) {
      httpParams = httpParams.set('search', params.search);
    }
    if (params.fromDue) {
      httpParams = httpParams.set('fromDue', params.fromDue);
    }
    if (params.toDue) {
      httpParams = httpParams.set('toDue', params.toDue);
    }
    if (params.serviceTypeId) {
      httpParams = httpParams.set('serviceTypeId', params.serviceTypeId.toString());
    }
    if (params.ownerId) {
      httpParams = httpParams.set('ownerId', params.ownerId.toString());
    }
    if (params.page) {
      httpParams = httpParams.set('page', params.page.toString());
    }
    if (params.pageSize) {
      httpParams = httpParams.set('pageSize', params.pageSize.toString());
    }
    if (params.order) {
      httpParams = httpParams.set('order', params.order);
    }

    return this.http.get<PagedResult<ServiceLocationDto>>(this.baseUrl, { params: httpParams });
  }

  getById(toolId: string): Observable<ServiceLocationDto> {
    return this.http.get<ServiceLocationDto>(`${this.baseUrl}/${toolId}`);
  }

  create(request: CreateServiceLocationRequest): Observable<ServiceLocationDto> {
    return this.http.post<ServiceLocationDto>(this.baseUrl, request);
  }

  update(toolId: string, request: UpdateServiceLocationRequest): Observable<ServiceLocationDto> {
    return this.http.put<ServiceLocationDto>(`${this.baseUrl}/${toolId}`, request);
  }

  setPriorityDate(toolId: string, priorityDate: string | null): Observable<ServiceLocationDto> {
    const request: SetPriorityDateRequest = {
      priorityDate: priorityDate || undefined,
    };
    return this.http.post<ServiceLocationDto>(`${this.baseUrl}/${toolId}/set-priority-date`, request);
  }

  markDone(toolId: string): Observable<ServiceLocationDto> {
    return this.http.post<ServiceLocationDto>(`${this.baseUrl}/${toolId}/mark-done`, {});
  }

  markOpen(toolId: string): Observable<ServiceLocationDto> {
    return this.http.post<ServiceLocationDto>(`${this.baseUrl}/${toolId}/mark-open`, {});
  }

  markCancelled(toolId: string, remark: string): Observable<ServiceLocationDto> {
    return this.http.post<ServiceLocationDto>(`${this.baseUrl}/${toolId}/mark-cancelled`, { remark });
  }

  markPlanned(toolId: string): Observable<ServiceLocationDto> {
    return this.http.post<ServiceLocationDto>(`${this.baseUrl}/${toolId}/mark-planned`, {});
  }

  // Bulk operations
  bulkInsert(request: BulkInsertServiceLocationsRequest): Observable<BulkInsertResultDto> {
    return this.http.post<BulkInsertResultDto>(`${this.baseUrl}/bulk`, request);
  }

  downloadTemplate(serviceTypeId: number, ownerId: number): Observable<Blob> {
    const params = new HttpParams()
      .set('serviceTypeId', serviceTypeId.toString())
      .set('ownerId', ownerId.toString());
    return this.http.get(`${this.baseUrl}/bulk/template`, {
      params,
      responseType: 'blob',
    });
  }

  uploadExcel(file: File, serviceTypeId: number, ownerId: number): Observable<BulkInsertResultDto> {
    const formData = new FormData();
    formData.append('file', file);
    const params = new HttpParams()
      .set('serviceTypeId', serviceTypeId.toString())
      .set('ownerId', ownerId.toString());
    return this.http.post<BulkInsertResultDto>(`${this.baseUrl}/bulk/excel`, formData, { params });
  }

  getOpeningHours(toolId: string): Observable<ServiceLocationOpeningHoursDto[]> {
    return this.http.get<ServiceLocationOpeningHoursDto[]>(`${this.baseUrl}/${toolId}/opening-hours`);
  }

  saveOpeningHours(toolId: string, items: ServiceLocationOpeningHoursDto[]): Observable<ServiceLocationOpeningHoursDto[]> {
    return this.http.put<ServiceLocationOpeningHoursDto[]>(`${this.baseUrl}/${toolId}/opening-hours`, {
      items,
    });
  }

  getExceptions(toolId: string): Observable<ServiceLocationExceptionDto[]> {
    return this.http.get<ServiceLocationExceptionDto[]>(`${this.baseUrl}/${toolId}/exceptions`);
  }

  saveExceptions(toolId: string, items: ServiceLocationExceptionDto[]): Observable<ServiceLocationExceptionDto[]> {
    return this.http.put<ServiceLocationExceptionDto[]>(`${this.baseUrl}/${toolId}/exceptions`, {
      items,
    });
  }

  getConstraints(toolId: string): Observable<ServiceLocationConstraintDto> {
    return this.http.get<ServiceLocationConstraintDto>(`${this.baseUrl}/${toolId}/constraints`);
  }

  saveConstraints(toolId: string, request: ServiceLocationConstraintDto): Observable<ServiceLocationConstraintDto> {
    return this.http.put<ServiceLocationConstraintDto>(`${this.baseUrl}/${toolId}/constraints`, request);
  }
}

