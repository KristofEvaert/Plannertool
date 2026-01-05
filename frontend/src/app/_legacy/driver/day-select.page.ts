import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import type { DriverDto } from '@models/driver.model';
import { DriversApiService } from '@services/drivers-api.service';
import { toYmd } from '@utils/date.utils';
import { MessageService } from 'primeng/api';
import { ButtonModule } from 'primeng/button';
import { CalendarModule } from 'primeng/calendar';
import { MessageModule } from 'primeng/message';
import { catchError, of } from 'rxjs';

@Component({
  selector: 'app-driver-day-select',
  imports: [CalendarModule, SelectModule, ButtonModule, MessageModule, FormsModule],
  providers: [MessageService],
  templateUrl: './day-select.page.html',
  styleUrl: './day-select.page.css',
  standalone: true,
})
export class DriverDaySelectPage {
  private readonly driversApi = inject(DriversApiService);
  private readonly messageService = inject(MessageService);
  private readonly router = inject(Router);

  drivers = signal<DriverDto[]>([]);
  selectedDriver = signal<DriverDto | null>(null);
  selectedDate = signal<Date | null>(null);
  loading = signal(false);
  error = signal<string | null>(null);

  constructor() {
    this.selectedDate.set(new Date());
    this.loadDrivers();
  }

  loadDrivers(): void {
    this.loading.set(true);
    this.driversApi
      .getDrivers()
      .pipe(
        catchError((err) => {
          this.error.set(err.title || err.message || 'Failed to load drivers');
          return of([]);
        }),
      )
      .subscribe((data) => {
        this.drivers.set(data);
        this.loading.set(false);
      });
  }

  navigateToDay(): void {
    const driver = this.selectedDriver();
    const date = this.selectedDate();
    if (driver && date) {
      const ymd = toYmd(date);
      this.router.navigate(['/driver/day', ymd, 'driver', driver.id]);
    }
  }
}
