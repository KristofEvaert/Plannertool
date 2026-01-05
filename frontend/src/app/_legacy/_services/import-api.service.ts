import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '@environments/environment';
import type { ImportPolesResultDto } from '@models/import.model';

@Injectable({ providedIn: 'root' })
export class ImportApiService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiBaseUrl}/api/import`;

  importPoles(days = 14): Observable<ImportPolesResultDto> {
    const params = new HttpParams().set('days', days.toString());
    return this.http.post<ImportPolesResultDto>(`${this.baseUrl}/poles`, {}, { params });
  }
}
