import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import type { DriverDto } from '@models/driver.model';
import type { DriverDayDto } from '@models/plan.model';
import { DriversApiService } from '@services/drivers-api.service';
import { PlanApiService } from '@services/plan-api.service';
import { todayYmd } from '@utils/date.utils';
import { MessageService } from 'primeng/api';
import { ButtonModule } from 'primeng/button';
import { MessageModule } from 'primeng/message';
import { catchError, of } from 'rxjs';

@Component({
  selector: 'app-driver-today',
  imports: [SelectModule, ButtonModule, MessageModule, FormsModule],
  providers: [MessageService],
  templateUrl: './today.page.html',
  styleUrl: './today.page.css',
  standalone: true,
})
export class DriverTodayPage {
  private readonly driversApi = inject(DriversApiService);
  private readonly planApi = inject(PlanApiService);
  private readonly messageService = inject(MessageService);
  private readonly router = inject(Router);

  drivers = signal<DriverDto[]>([]);
  selectedDriver = signal<DriverDto | null>(null);
  driverDay = signal<DriverDayDto | null>(null);
  loading = signal(false);
  error = signal<string | null>(null);
  today = todayYmd();

  constructor() {
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

  onDriverSelect(driver: DriverDto | null): void {
    this.selectedDriver.set(driver);
    if (driver) {
      this.loadDriverDay(driver.id);
    } else {
      this.driverDay.set(null);
    }
  }

  loadDriverDay(driverId: number): void {
    this.loading.set(true);
    this.error.set(null);

    this.planApi
      .getDriverDay(this.today, driverId)
      .pipe(
        catchError((err) => {
          this.error.set(err.title || err.message || 'Failed to load driver day');
          this.messageService.add({
            severity: 'error',
            summary: 'Error',
            detail: err.detail || err.title || err.message || 'Failed to load driver day',
          });
          return of(null);
        }),
      )
      .subscribe((data) => {
        this.driverDay.set(data);
        this.loading.set(false);
      });
  }

  goToRunMode(): void {
    const driver = this.selectedDriver();
    if (driver) {
      this.router.navigate(['/driver/day', this.today, 'driver', driver.id]);
    }
  }
}
