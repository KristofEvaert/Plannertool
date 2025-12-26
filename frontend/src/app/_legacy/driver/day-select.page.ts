import { Component, inject, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { CalendarModule } from 'primeng/calendar';
import { DropdownModule } from 'primeng/dropdown';
import { ButtonModule } from 'primeng/button';
import { MessageModule } from 'primeng/message';
import { MessageService } from 'primeng/api';
import { FormsModule } from '@angular/forms';
import { catchError, of } from 'rxjs';
import { DriversApiService } from '@services/drivers-api.service';
import type { DriverDto } from '@models/driver.model';
import { toYmd } from '@utils/date.utils';

@Component({
  selector: 'app-driver-day-select',
  imports: [CalendarModule, DropdownModule, ButtonModule, MessageModule, FormsModule],
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
        })
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

