import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '@environments/environment';
import {
  AvailabilityBulkUpsertResultDto,
  BulkUpsertDriversRequest,
  BulkUpsertResultDto,
  DriverServiceTypesBulkExportResponse,
  DriverServiceTypesBulkRequest,
  DriverServiceTypesBulkResult,
} from '@models';

@Injectable({ providedIn: 'root' })
export class DriversBulkApiService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiBaseUrl}/api/drivers`;

  bulkUpsert(request: BulkUpsertDriversRequest): Observable<BulkUpsertResultDto> {
    return this.http.post<BulkUpsertResultDto>(this.baseUrl, request);
  }

  downloadAvailabilityTemplateExcel(): Observable<Blob> {
    return this.http.get(`${this.baseUrl}/bulk/excel`, {
      responseType: 'blob',
    });
  }

  downloadServiceTypesTemplateJson(): Observable<DriverServiceTypesBulkExportResponse> {
    return this.http.get<DriverServiceTypesBulkExportResponse>(
      `${this.baseUrl}/service-types/bulk/json`,
    );
  }

  downloadServiceTypesTemplateExcel(): Observable<Blob> {
    return this.http.get(`${this.baseUrl}/service-types/bulk/excel`, {
      responseType: 'blob',
    });
  }

  bulkUpsertServiceTypes(
    request: DriverServiceTypesBulkRequest,
  ): Observable<DriverServiceTypesBulkResult> {
    return this.http.post<DriverServiceTypesBulkResult>(
      `${this.baseUrl}/service-types/bulk/json`,
      request,
    );
  }

  uploadServiceTypesExcel(file: File): Observable<DriverServiceTypesBulkResult> {
    const formData = new FormData();
    formData.append('file', file);
    return this.http.post<DriverServiceTypesBulkResult>(
      `${this.baseUrl}/service-types/bulk/excel`,
      formData,
    );
  }

  uploadAvailabilityExcel(file: File): Observable<AvailabilityBulkUpsertResultDto> {
    const formData = new FormData();
    formData.append('file', file);
    return this.http.post<AvailabilityBulkUpsertResultDto>(`${this.baseUrl}/bulk/excel`, formData);
  }
}
