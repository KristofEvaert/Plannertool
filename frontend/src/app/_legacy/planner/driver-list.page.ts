import { Component, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TableModule } from 'primeng/table';
import { ButtonModule } from 'primeng/button';
import { MessageModule } from 'primeng/message';
import { MessageService } from 'primeng/api';
import { catchError, of } from 'rxjs';
import { DriversApiService } from '@services/drivers-api.service';
import type { DriverDto } from '@models/driver.model';
import { todayYmd } from '@utils/date.utils';

@Component({
  selector: 'app-driver-list',
  imports: [RouterLink, TableModule, ButtonModule, MessageModule],
  providers: [MessageService],
  templateUrl: './driver-list.page.html',
  styleUrl: './driver-list.page.css',
  standalone: true,
})
export class DriverListPage {
  private readonly driversApi = inject(DriversApiService);
  private readonly messageService = inject(MessageService);

  drivers = signal<DriverDto[]>([]);
  loading = signal(false);
  error = signal<string | null>(null);
  today = todayYmd();

  constructor() {
    this.loadDrivers();
  }

  loadDrivers(): void {
    this.loading.set(true);
    this.error.set(null);

    this.driversApi
      .getDrivers()
      .pipe(
        catchError((err) => {
          this.error.set(err.title || err.message || 'Failed to load drivers');
          this.messageService.add({
            severity: 'error',
            summary: 'Error',
            detail: err.detail || err.title || err.message || 'Failed to load drivers',
          });
          return of([]);
        }),
      )
      .subscribe((data) => {
        this.drivers.set(data);
        this.loading.set(false);
      });
  }
}
