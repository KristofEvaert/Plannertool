import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '@environments/environment';

export interface BulkUpsertDriversRequest {
  drivers: BulkDriverDto[];
  availabilities: BulkAvailabilityDto[];
}

export interface BulkDriverDto {
  toolId?: string; // Guid as string
  erpId: number;
  name: string;
  startLocationLabel?: string;
  startAddress?: string;
  startLatitude: number | null;
  startLongitude: number | null;
  defaultServiceMinutes?: number;
  maxWorkMinutesPerDay?: number;
  isActive?: boolean;
}

export interface BulkAvailabilityDto {
  driverToolId?: string; // Guid as string
  driverErpId?: number;
  date: string; // yyyy-MM-dd
  startMinuteOfDay: number;
  endMinuteOfDay: number;
}

export interface BulkUpsertResultDto {
  driversCreated: number;
  driversUpdated: number;
  availabilitiesUpserted: number;
  errors: BulkErrorDto[];
}

export interface DriverServiceTypesBulkItem {
  email?: string;
  driverToolId?: string;
  driverErpId?: number;
  serviceTypeIds?: string;
}

export interface DriverServiceTypesBulkRequest {
  drivers: DriverServiceTypesBulkItem[];
}

export interface DriverServiceTypesBulkFailedItem {
  email?: string;
  serviceTypeIds?: string;
  rowRef?: string;
  message?: string;
}

export interface DriverServiceTypesBulkResult {
  updated: number;
  errors: BulkErrorDto[];
  failedItems: DriverServiceTypesBulkFailedItem[];
}

export interface DriverServiceTypesBulkExportResponse {
  generatedAtUtc: string;
  drivers: DriverServiceTypesBulkItem[];
}

export interface AvailabilityBulkUpsertResultDto {
  inserted: number;
  updated: number;
  deleted: number;
  errors: BulkErrorDto[];
  conflicts: AvailabilityBulkConflictDto[];
}

export interface AvailabilityBulkConflictDto {
  email?: string;
  driverName?: string;
  date?: string;
  existingStartMinuteOfDay?: number;
  existingEndMinuteOfDay?: number;
  newStartMinuteOfDay?: number;
  newEndMinuteOfDay?: number;
  rowRef?: string;
  reason?: string;
}

export interface BulkErrorDto {
  scope: string; // "Driver" | "Availability"
  rowRef: string;
  message: string;
}

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
