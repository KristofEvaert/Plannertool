import { Component, computed, effect, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { ButtonModule } from 'primeng/button';
import { TableModule } from 'primeng/table';
import { CardModule } from 'primeng/card';
import { DialogModule } from 'primeng/dialog';
import { InputTextModule } from 'primeng/inputtext';
import { InputNumberModule } from 'primeng/inputnumber';
import { CalendarModule } from 'primeng/calendar';
import { DropdownModule } from 'primeng/dropdown';
import { MultiSelectModule } from 'primeng/multiselect';
import { TooltipModule } from 'primeng/tooltip';
import { ConfirmDialogModule } from 'primeng/confirmdialog';
import { ToastModule } from 'primeng/toast';
import { MessageService, ConfirmationService } from 'primeng/api';
import { catchError, of } from 'rxjs';
import { DriversApiService } from '@services/drivers-api.service';
import { DriverAvailabilityApiService } from '@services/driver-availability-api.service';
import { ServiceLocationOwnersApiService, ServiceLocationOwnerDto } from '@services/service-location-owners-api.service';
import { ServiceTypesApiService } from '@services/service-types-api.service';
import type { ServiceTypeDto } from '@models/service-type.model';
import type {
  DriverDto,
  CreateDriverRequest,
  UpdateDriverRequest,
  DriverAvailabilityDto,
  UpsertAvailabilityRequest,
} from '@models/driver.model';
import { toYmd, parseYmd } from '@utils/date.utils';

@Component({
  selector: 'app-drivers',
  imports: [
    CommonModule,
    FormsModule,
    RouterLink,
    ButtonModule,
    TableModule,
    CardModule,
    DialogModule,
    InputTextModule,
    InputNumberModule,
    CalendarModule,
    DropdownModule,
    MultiSelectModule,
    TooltipModule,
    ConfirmDialogModule,
    ToastModule,
  ],
  providers: [MessageService, ConfirmationService],
  templateUrl: './drivers.page.html',
  standalone: true,
})
export class DriversPage {
  private readonly driversApi = inject(DriversApiService);
  private readonly availabilityApi = inject(DriverAvailabilityApiService);
  private readonly ownersApi = inject(ServiceLocationOwnersApiService);
  private readonly serviceTypesApi = inject(ServiceTypesApiService);
  private readonly messageService = inject(MessageService);
  private readonly confirmationService = inject(ConfirmationService);

  drivers = signal<DriverDto[]>([]);
  owners = signal<ServiceLocationOwnerDto[]>([]);
  serviceTypes = signal<ServiceTypeDto[]>([]);
  loading = signal(false);
  selectedDriver = signal<DriverDto | null>(null);
  selectedDate = signal<Date | null>(null);
  currentMonth = signal<Date>(new Date());

  // Filters
  ownerFilterId = signal<number | null>(null); // null = all
  
  // Inline editing
  editingOwnerId = signal<string | null>(null); // toolId of driver being edited

  // Driver dialog
  showDriverDialog = signal(false);
  isEditMode = signal(false);
  driverForm = signal<CreateDriverRequest>({
    erpId: 0,
    name: '',
    startAddress: '',
    startLatitude: 0,
    startLongitude: 0,
    defaultServiceMinutes: 20,
    maxWorkMinutesPerDay: 480,
    ownerId: 0, // Will be set when owners are loaded
    isActive: true,
    serviceTypeIds: [],
  });

  // Availability dialog
  showAvailabilityDialog = signal(false);
  availabilityForm = signal<UpsertAvailabilityRequest>({
    startMinuteOfDay: 480, // 08:00
    endMinuteOfDay: 960,   // 16:00
  });
  selectedAvailability = signal<DriverAvailabilityDto | null>(null);

  // Computed properties for dialog visibility (PrimeNG needs regular properties)
  get showDriverDialogValue(): boolean {
    return this.showDriverDialog();
  }
  set showDriverDialogValue(value: boolean) {
    this.showDriverDialog.set(value);
  }

  get showAvailabilityDialogValue(): boolean {
    return this.showAvailabilityDialog();
  }
  set showAvailabilityDialogValue(value: boolean) {
    this.showAvailabilityDialog.set(value);
  }

  // Availability data for current month
  availabilities = signal<DriverAvailabilityDto[]>([]);
  availabilityMap = computed(() => {
    const map = new Map<string, DriverAvailabilityDto>();
    for (const av of this.availabilities()) {
      map.set(av.date, av);
    }
    return map;
  });

  // Time options for dropdowns (30-min steps)
  timeOptions = computed(() => {
    const options: { label: string; value: number }[] = [];
    for (let hour = 0; hour < 24; hour++) {
      for (let minute = 0; minute < 60; minute += 30) {
        const minutes = hour * 60 + minute;
        const label = `${String(hour).padStart(2, '0')}:${String(minute).padStart(2, '0')}`;
        options.push({ label, value: minutes });
      }
    }
    return options;
  });

  filteredDrivers = computed(() => {
    const ownerId = this.ownerFilterId();
    const drivers = this.drivers();
    if (!ownerId) return drivers;
    return drivers.filter((d) => d.ownerId === ownerId);
  });

  constructor() {
    this.loadOwners();
    this.loadServiceTypes();
    this.loadDrivers();

    // When selected driver changes, load availability for current month
    effect(() => {
      const driver = this.selectedDriver();
      const month = this.currentMonth();
      if (driver) {
        this.loadAvailabilityForMonth(driver.toolId, month);
      } else {
        this.availabilities.set([]);
      }
    });

    // Keep selected driver valid when filters change.
    effect(() => {
      const filtered = this.filteredDrivers();
      const selected = this.selectedDriver();

      if (selected && !filtered.some((d) => d.toolId === selected.toolId)) {
        this.selectedDriver.set(filtered[0] ?? null);
        return;
      }

      if (!selected && filtered.length > 0) {
        this.selectedDriver.set(filtered[0]);
      }
    });
  }

  loadOwners(): void {
    this.ownersApi
      .getAll(false)
      .pipe(
        catchError((err) => {
          this.messageService.add({
            severity: 'error',
            summary: 'Error',
            detail: err.detail || err.message || 'Failed to load owners',
          });
          return of([]);
        })
      )
      .subscribe((owners) => {
        this.owners.set(owners);
        // Set default owner in form if empty
        if (owners.length > 0 && this.driverForm().ownerId === 0) {
          this.driverForm.update(f => ({ ...f, ownerId: owners[0].id }));
        }
      });
  }

  loadServiceTypes(): void {
    this.serviceTypesApi
      .getAll(true)
      .pipe(
        catchError((err) => {
          this.messageService.add({
            severity: 'error',
            summary: 'Error',
            detail: err.detail || err.message || 'Failed to load service types',
          });
          return of([]);
        })
      )
      .subscribe((serviceTypes) => {
        this.serviceTypes.set(serviceTypes);
      });
  }

  getServiceTypeTooltip(driver: DriverDto): string {
    const ids = driver.serviceTypeIds ?? [];
    if (ids.length === 0) {
      return 'No service types';
    }

    const map = new Map(this.serviceTypes().map((st) => [st.id, st.name]));
    const names = ids.map((id) => map.get(id) ?? `#${id}`);
    return names.join(', ');
  }

  loadDrivers(): void {
    this.loading.set(true);
    this.driversApi
      .getDrivers()
      .pipe(
        catchError((err) => {
          this.messageService.add({
            severity: 'error',
            summary: 'Error',
            detail: err.detail || err.message || 'Failed to load drivers',
          });
          return of([]);
        })
      )
      .subscribe((drivers) => {
        this.loading.set(false);
        this.drivers.set(drivers);
        // Auto-selection is handled by the filter effect.
      });
  }

  loadAvailabilityForMonth(toolId: string, month: Date): void {
    const year = month.getFullYear();
    const monthIndex = month.getMonth();
    const firstDay = new Date(year, monthIndex, 1);
    const lastDay = new Date(year, monthIndex + 1, 0);
    const fromYmd = toYmd(firstDay);
    const toYmdStr = toYmd(lastDay);

    this.availabilityApi
      .getAvailability(toolId, fromYmd, toYmdStr)
      .pipe(
        catchError((err) => {
          this.messageService.add({
            severity: 'error',
            summary: 'Error',
            detail: err.detail || err.message || 'Failed to load availability',
          });
          return of([]);
        })
      )
      .subscribe((availabilities) => {
        this.availabilities.set(availabilities);
      });
  }

  openEditDriverDialog(driver: DriverDto): void {
    this.isEditMode.set(true);
    this.driverForm.set({
      erpId: driver.erpId,
      name: driver.name,
      startAddress: driver.startAddress || '',
      startLatitude: driver.startLatitude,
      startLongitude: driver.startLongitude,
      defaultServiceMinutes: driver.defaultServiceMinutes,
      maxWorkMinutesPerDay: driver.maxWorkMinutesPerDay,
      ownerId: driver.ownerId,
      isActive: driver.isActive,
      serviceTypeIds: driver.serviceTypeIds ? [...driver.serviceTypeIds] : [],
    });
    this.showDriverDialog.set(true);
  }

  startEditingOwner(driver: DriverDto): void {
    this.editingOwnerId.set(driver.toolId);
  }

  cancelEditingOwner(): void {
    this.editingOwnerId.set(null);
  }

  saveOwnerChange(driver: DriverDto, newOwnerId: number): void {
    if (newOwnerId === driver.ownerId) {
      this.editingOwnerId.set(null);
      return;
    }

    const updateRequest: UpdateDriverRequest = {
      erpId: driver.erpId,
      name: driver.name,
      startAddress: driver.startAddress || '',
      startLatitude: driver.startLatitude,
      startLongitude: driver.startLongitude,
      defaultServiceMinutes: driver.defaultServiceMinutes,
      maxWorkMinutesPerDay: driver.maxWorkMinutesPerDay,
      ownerId: newOwnerId,
      isActive: driver.isActive,
      serviceTypeIds: driver.serviceTypeIds ? [...driver.serviceTypeIds] : [],
    };

    this.loading.set(true);
    this.driversApi
      .updateDriver(driver.toolId, updateRequest)
      .pipe(
        catchError((err) => {
          this.loading.set(false);
          this.messageService.add({
            severity: 'error',
            summary: 'Error',
            detail: err.detail || err.message || 'Failed to update owner',
          });
          return of(null);
        })
      )
      .subscribe((updatedDriver) => {
        this.loading.set(false);
        if (updatedDriver) {
          this.editingOwnerId.set(null);
          this.messageService.add({
            severity: 'success',
            summary: 'Success',
            detail: 'Owner updated',
          });
          this.loadDrivers();
          // Update selected driver if it's the one we just updated
          if (this.selectedDriver()?.toolId === driver.toolId) {
            this.selectedDriver.set(updatedDriver);
          }
        }
      });
  }

  saveDriver(): void {
    const form = this.driverForm();
    const isEdit = this.isEditMode();
    const selected = this.selectedDriver();

    if (!form.name.trim()) {
      this.messageService.add({
        severity: 'error',
        summary: 'Validation Error',
        detail: 'Name is required',
      });
      return;
    }

    if (!form.ownerId || form.ownerId <= 0) {
      this.messageService.add({
        severity: 'error',
        summary: 'Validation Error',
        detail: 'Owner is required',
      });
      return;
    }

    this.loading.set(true);
    if (isEdit && selected) {
      const updateReq: UpdateDriverRequest = {
        erpId: form.erpId,
        name: form.name,
        startAddress: form.startAddress,
        startLatitude: form.startLatitude,
        startLongitude: form.startLongitude,
        defaultServiceMinutes: form.defaultServiceMinutes,
        maxWorkMinutesPerDay: form.maxWorkMinutesPerDay,
        ownerId: form.ownerId,
        isActive: form.isActive,
        serviceTypeIds: form.serviceTypeIds ? [...form.serviceTypeIds] : [],
      };
      this.driversApi
        .updateDriver(selected.toolId, updateReq)
        .pipe(
          catchError((err) => {
            this.loading.set(false);
            this.messageService.add({
              severity: 'error',
              summary: 'Error',
              detail: err.detail || err.message || 'Failed to save driver',
            });
            return of(null);
          })
        )
        .subscribe((driver) => {
          this.loading.set(false);
          if (driver) {
            this.showDriverDialog.set(false);
            this.messageService.add({
              severity: 'success',
              summary: 'Success',
              detail: 'Driver updated',
            });
            this.loadDrivers();
          }
        });
    }
    else {
      this.loading.set(false);
      this.messageService.add({
        severity: 'warn',
        summary: 'Not allowed',
        detail: 'Manual driver creation is disabled. Drivers are created automatically when the role is assigned.',
      });
    }
  }

  deleteDriver(driver: DriverDto): void {
    this.confirmationService.confirm({
      message: `Are you sure you want to deactivate ${driver.name}?`,
      header: 'Confirm Deactivation',
      icon: 'pi pi-exclamation-triangle',
      accept: () => {
        this.loading.set(true);
        this.driversApi
          .deleteDriver(driver.toolId)
          .pipe(
            catchError((err) => {
              this.loading.set(false);
              this.messageService.add({
                severity: 'error',
                summary: 'Error',
                detail: err.detail || err.message || 'Failed to deactivate driver',
              });
              return of(null);
            })
          )
          .subscribe(() => {
            this.loading.set(false);
            this.messageService.add({
              severity: 'success',
              summary: 'Success',
              detail: 'Driver deactivated',
            });
            if (this.selectedDriver()?.toolId === driver.toolId) {
              this.selectedDriver.set(null);
            }
            this.loadDrivers();
          });
      },
    });
  }

  onDateSelect(date: Date | null): void {
    if (!date) return;
    this.selectedDate.set(date);
    const dateYmd = toYmd(date);
    const availability = this.availabilityMap().get(dateYmd);
    this.selectedAvailability.set(availability || null);

    if (availability) {
      this.availabilityForm.set({
        startMinuteOfDay: availability.startMinuteOfDay,
        endMinuteOfDay: availability.endMinuteOfDay,
      });
    } else {
      this.availabilityForm.set({
        startMinuteOfDay: 480, // 08:00
        endMinuteOfDay: 960,   // 16:00
      });
    }

    this.showAvailabilityDialog.set(true);
  }

  saveAvailability(): void {
    const driver = this.selectedDriver();
    const date = this.selectedDate();
    const form = this.availabilityForm();

    if (!driver || !date) return;

    if (form.endMinuteOfDay <= form.startMinuteOfDay) {
      this.messageService.add({
        severity: 'error',
        summary: 'Validation Error',
        detail: 'End time must be after start time',
      });
      return;
    }

    this.loading.set(true);
    const dateYmd = toYmd(date);

    this.availabilityApi
      .upsertAvailability(driver.toolId, dateYmd, form)
      .pipe(
        catchError((err) => {
          this.loading.set(false);
          this.messageService.add({
            severity: 'error',
            summary: 'Error',
            detail: err.detail || err.message || 'Failed to save availability',
          });
          return of(null);
        })
      )
      .subscribe((availability) => {
        this.loading.set(false);
        if (availability) {
          this.showAvailabilityDialog.set(false);
          this.messageService.add({
            severity: 'success',
            summary: 'Success',
            detail: 'Availability saved',
          });
          this.loadAvailabilityForMonth(driver.toolId, this.currentMonth());
        }
      });
  }

  deleteAvailability(): void {
    const driver = this.selectedDriver();
    const date = this.selectedDate();
    const availability = this.selectedAvailability();

    if (!driver || !date || !availability) return;

    this.confirmationService.confirm({
      message: `Are you sure you want to delete availability for ${toYmd(date)}?`,
      header: 'Confirm Delete',
      icon: 'pi pi-exclamation-triangle',
      accept: () => {
        this.loading.set(true);
        const dateYmd = toYmd(date);

        this.availabilityApi
          .deleteAvailability(driver.toolId, dateYmd)
          .pipe(
            catchError((err) => {
              this.loading.set(false);
              this.messageService.add({
                severity: 'error',
                summary: 'Error',
                detail: err.detail || err.message || 'Failed to delete availability',
              });
              return of(null);
            })
          )
          .subscribe(() => {
            this.loading.set(false);
            this.messageService.add({
              severity: 'success',
              summary: 'Success',
              detail: 'Availability deleted',
            });
            this.showAvailabilityDialog.set(false);
            this.loadAvailabilityForMonth(driver.toolId, this.currentMonth());
          });
      },
    });
  }


  getAvailabilityForDate(date: Date): DriverAvailabilityDto | null {
    const dateYmd = toYmd(date);
    return this.availabilityMap().get(dateYmd) || null;
  }

  formatTime(minutes: number): string {
    const hours = Math.floor(minutes / 60);
    const mins = minutes % 60;
    return `${String(hours).padStart(2, '0')}:${String(mins).padStart(2, '0')}`;
  }
}
