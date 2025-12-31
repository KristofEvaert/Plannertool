import { Component, computed, effect, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ButtonModule } from 'primeng/button';
import { DialogModule } from 'primeng/dialog';
import { CalendarModule } from 'primeng/calendar';
import { DropdownModule } from 'primeng/dropdown';
import { MultiSelectModule } from 'primeng/multiselect';
import { ToastModule } from 'primeng/toast';
import { ConfirmDialogModule } from 'primeng/confirmdialog';
import { InputTextModule } from 'primeng/inputtext';
import { InputNumberModule } from 'primeng/inputnumber';
import { TooltipModule } from 'primeng/tooltip';
import { MessageService, ConfirmationService } from 'primeng/api';
import { forkJoin, catchError, of } from 'rxjs';
import { DriversApiService } from '@services/drivers-api.service';
import { DriverAvailabilityApiService } from '@services/driver-availability-api.service';
import { ServiceLocationOwnersApiService, ServiceLocationOwnerDto } from '@services/service-location-owners-api.service';
import { ServiceTypesApiService } from '@services/service-types-api.service';
import type { ServiceTypeDto } from '@models/service-type.model';
import {
  DriversBulkApiService,
} from '@services/drivers-bulk-api.service';
import { HelpManualComponent } from '@components/help-manual/help-manual.component';
import type {
  DriverDto,
  DriverAvailabilityDto,
  UpsertAvailabilityRequest,
  CreateDriverRequest,
  UpdateDriverRequest,
} from '@models/driver.model';
import { toYmd, parseYmd } from '@utils/date.utils';
import { AuthService } from '@services/auth.service';

interface AvailabilityMap {
  [driverToolId: string]: {
    [dateYmd: string]: DriverAvailabilityDto;
  };
}

interface GridCell {
  driver: DriverDto;
  date: Date;
  availability: DriverAvailabilityDto | null;
}

@Component({
  selector: 'app-drivers-availability-grid',
  imports: [
    CommonModule,
    FormsModule,
    ButtonModule,
    DialogModule,
    CalendarModule,
    DropdownModule,
    MultiSelectModule,
    ToastModule,
    ConfirmDialogModule,
    InputTextModule,
    InputNumberModule,
    TooltipModule,
    HelpManualComponent,
  ],
  providers: [MessageService, ConfirmationService],
  templateUrl: './drivers-availability-grid.page.html',
  standalone: true,
})
export class DriversAvailabilityGridPage {
  private readonly driversApi = inject(DriversApiService);
  private readonly availabilityApi = inject(DriverAvailabilityApiService);
  private readonly bulkApi = inject(DriversBulkApiService);
  private readonly ownersApi = inject(ServiceLocationOwnersApiService);
  private readonly serviceTypesApi = inject(ServiceTypesApiService);
  private readonly messageService = inject(MessageService);
  private readonly confirmationService = inject(ConfirmationService);
  private readonly auth = inject(AuthService);

  drivers = signal<DriverDto[]>([]);
  owners = signal<ServiceLocationOwnerDto[]>([]);
  serviceTypes = signal<ServiceTypeDto[]>([]);
  loading = signal(false);
  startDate = signal<Date>(this.getMondayOfWeek(new Date()));
  visibleDays = signal<number>(14);
  availabilityMap = signal<AvailabilityMap>({});
  driverColumnWidth = signal(200);
  pendingUploadKind: 'availability' | 'serviceTypes' | null = null;

  // Filters
  ownerFilterId = signal<number | null>(null); // null = all owners
  showOnlyDriversWithAvailability = signal(false);

  // Availability dialog state
  showDialog = signal(false);
  selectedCell = signal<GridCell | null>(null);
  dialogForm = signal<UpsertAvailabilityRequest>({
    startMinuteOfDay: 480, // 08:00
    endMinuteOfDay: 960,   // 16:00
  });

  // Driver dialog state
  showDriverDialog = signal(false);
  isEditMode = signal(false);
  selectedDriverForEdit = signal<DriverDto | null>(null);
  driverForm = signal<CreateDriverRequest>({
    erpId: 0,
    name: '',
    startAddress: '',
    startLatitude: 50.8503, // Default Brussels
    startLongitude: 4.3517,
    defaultServiceMinutes: 20,
    maxWorkMinutesPerDay: 480,
    ownerId: 0, // Will be set when owners are loaded
    isActive: true,
    serviceTypeIds: [],
  });

  driverFormServiceTypes = computed(() => {
    const ownerId = this.driverForm().ownerId;
    if (!ownerId) {
      return [];
    }
    return this.serviceTypes().filter((type) => type.ownerId === ownerId);
  });

  // Computed properties for dialog visibility (PrimeNG needs regular properties)
  get showDialogValue(): boolean {
    return this.showDialog();
  }
  set showDialogValue(value: boolean) {
    this.showDialog.set(value);
  }

  get showDriverDialogValue(): boolean {
    return this.showDriverDialog();
  }
  set showDriverDialogValue(value: boolean) {
    this.showDriverDialog.set(value);
  }

  // Range options
  rangeOptions = [
    { label: '7 days', value: 7 },
    { label: '14 days', value: 14 },
    { label: '21 days', value: 21 },
  ];

  // Computed properties
  gridTemplateColumns = computed(() => `${this.driverColumnWidth()}px repeat(${this.visibleDays()}, 110px)`);

  endDate = computed(() => {
    const start = this.startDate();
    const days = this.visibleDays();
    return this.addDays(start, days - 1);
  });

  dateRange = computed(() => {
    const start = this.startDate();
    const days = this.visibleDays();
    const dates: Date[] = [];
    for (let i = 0; i < days; i++) {
      dates.push(this.addDays(start, i));
    }
    return dates;
  });

  availableDrivers = computed(() => {
    const drivers = this.drivers();
    const ownerId = this.ownerFilterId();
    const dates = this.dateRange();
    const map = this.availabilityMap();
    
    const ownerFiltered = ownerId ? drivers.filter((d) => d.ownerId === ownerId) : drivers;

    if (!this.showOnlyDriversWithAvailability()) {
      return ownerFiltered;
    }

    // Filter drivers to only show those with at least one availability with availableMinutes > 0 in the date range
    return ownerFiltered.filter((driver) => {
      const driverAvailabilities = map[driver.toolId] || {};
      // Check if driver has availability for any date in the range with availableMinutes > 0
      return dates.some((date) => {
        const dateYmd = this.dateToYmd(date);
        const availability = driverAvailabilities[dateYmd];
        return availability != null && availability.availableMinutes > 0;
      });
    });
  });

  gridCells = computed(() => {
    const drivers = this.availableDrivers();
    const dates = this.dateRange();
    const map = this.availabilityMap();
    const cells: GridCell[] = [];

    for (const driver of drivers) {
      for (const date of dates) {
        const dateYmd = this.dateToYmd(date);
        const availability = map[driver.toolId]?.[dateYmd] || null;
        cells.push({ driver, date, availability });
      }
    }
    return cells;
  });

  // Time options (30-min steps)
  timeOptions = computed(() => {
    const options: { label: string; value: number }[] = [];
    for (let hour = 0; hour < 24; hour++) {
      for (let minute = 0; minute < 60; minute += 30) {
        const minutes = hour * 60 + minute;
        const label = this.minutesToHHmm(minutes);
        options.push({ label, value: minutes });
      }
    }
    return options;
  });

  constructor() {
    const user = this.auth.currentUser();
    const ownerId = user?.ownerId ?? null;
    if (ownerId) {
      this.ownerFilterId.set(ownerId);
      this.driverForm.update((f) => ({ ...f, ownerId }));
    }

    this.loadOwners();
    this.loadDrivers();
    this.loadServiceTypes();

    // Reload availability when drivers are loaded or date range changes
    effect(() => {
      const drivers = this.drivers();
      const start = this.startDate();
      const end = this.endDate();
      if (drivers.length > 0) {
        console.log('Loading availability for', drivers.length, 'drivers from', this.dateToYmd(start), 'to', this.dateToYmd(end));
        this.loadAvailabilityForRange(start, end);
      }
    });
  }

  onDriverColumnResizeStart(event: MouseEvent): void {
    event.preventDefault();
    const startX = event.clientX;
    const startWidth = this.driverColumnWidth();
    const minWidth = 160;
    const maxWidth = 420;

    const onMove = (moveEvent: MouseEvent) => {
      const next = Math.min(maxWidth, Math.max(minWidth, startWidth + moveEvent.clientX - startX));
      this.driverColumnWidth.set(next);
    };

    const onUp = () => {
      window.removeEventListener('mousemove', onMove);
      window.removeEventListener('mouseup', onUp);
    };

    window.addEventListener('mousemove', onMove);
    window.addEventListener('mouseup', onUp);
  }

  // Utility functions
  dateToYmd(date: Date): string {
    return toYmd(date);
  }

  addDays(date: Date, n: number): Date {
    const result = new Date(date);
    result.setDate(result.getDate() + n);
    return result;
  }

  minutesToHHmm(minutes: number): string {
    const hours = Math.floor(minutes / 60);
    const mins = minutes % 60;
    return `${String(hours).padStart(2, '0')}:${String(mins).padStart(2, '0')}`;
  }

  hhmmToMinutes(hhmm: string): number {
    const [hours, minutes] = hhmm.split(':').map(Number);
    return hours * 60 + minutes;
  }

  getMondayOfWeek(date: Date): Date {
    const d = new Date(date);
    const day = d.getDay();
    const diff = d.getDate() - day + (day === 0 ? -6 : 1); // Adjust when day is Sunday
    return new Date(d.setDate(diff));
  }

  formatDateHeader(date: Date): string {
    const days = ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat'];
    const dayName = days[date.getDay()];
    const day = date.getDate();
    return `${dayName} ${day}`;
  }

  formatDateFull(date: Date): string {
    return date.toLocaleDateString('en-US', {
      year: 'numeric',
      month: 'long',
      day: 'numeric',
    });
  }

  // Data loading
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
        const userOwnerId = this.auth.currentUser()?.ownerId ?? null;
        const filtered = !this.isSuperAdmin() && userOwnerId ? owners.filter((o) => o.id === userOwnerId) : owners;
        this.owners.set(filtered);
        // Set default owner in form if empty
        const effective = filtered.length > 0 ? filtered : owners;
        if (effective.length > 0 && this.driverForm().ownerId === 0) {
          this.driverForm.update(f => ({ ...f, ownerId: effective[0].id }));
        }
      });
  }

  loadServiceTypes(): void {
    const current = this.auth.currentUser();
    const resolvedOwnerId = current?.roles.includes('SuperAdmin') ? null : current?.ownerId ?? null;
    this.serviceTypesApi
      .getAll(true, resolvedOwnerId ?? undefined)
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

  onDriverFormOwnerChange(ownerId: number | null): void {
    const nextOwnerId = ownerId ?? 0;
    const allowedIds = new Set(
      this.serviceTypes()
        .filter((type) => type.ownerId === nextOwnerId)
        .map((type) => type.id)
    );
    this.driverForm.update((current) => ({
      ...current,
      ownerId: nextOwnerId,
      serviceTypeIds: (current.serviceTypeIds ?? []).filter((id) => allowedIds.has(id)),
    }));
  }

  onDriverFormServiceTypesChange(serviceTypeIds: number[] | null): void {
    this.driverForm.update((current) => ({
      ...current,
      serviceTypeIds: serviceTypeIds ?? [],
    }));
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
      .getDrivers(false) // active only
      .pipe(
        catchError((err) => {
          this.loading.set(false);
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
        console.log('Loaded drivers:', drivers.length);
        this.drivers.set(drivers);
        // Trigger availability load after drivers are set
        if (drivers.length > 0) {
          setTimeout(() => {
            this.loadAvailabilityForRange(this.startDate(), this.endDate());
          }, 100);
        }
      });
  }

  loadAvailabilityForRange(start: Date, end: Date): void {
    const drivers = this.drivers();
    if (drivers.length === 0) {
      console.log('No drivers to load availability for');
      return;
    }

    this.loading.set(true);
    const fromYmd = this.dateToYmd(start);
    const toYmd = this.dateToYmd(end);

    console.log('Loading availability from', fromYmd, 'to', toYmd, 'for', drivers.length, 'drivers');

    // Load availability for all drivers in parallel
    const requests = drivers.map((driver) =>
      this.availabilityApi.getAvailability(driver.toolId, fromYmd, toYmd).pipe(
        catchError((err) => {
          console.error(`Failed to load availability for ${driver.name} (${driver.toolId}):`, err);
          return of([]);
        })
      )
    );

    forkJoin(requests).subscribe({
      next: (results) => {
        this.loading.set(false);
        const map: AvailabilityMap = {};
        let totalAvailabilities = 0;

        drivers.forEach((driver, index) => {
          map[driver.toolId] = {};
          const availabilities = results[index];
          totalAvailabilities += availabilities.length;
          availabilities.forEach((av) => {
            // Normalize date to yyyy-MM-dd format for consistent key lookup
            const dateKey = av.date.split('T')[0]; // Remove time part if present
            map[driver.toolId][dateKey] = av;
            console.log(`Stored availability for ${driver.name} on ${dateKey}:`, av);
          });
        });

        console.log('Loaded', totalAvailabilities, 'total availabilities');
        this.availabilityMap.set(map);
      },
      error: (err) => {
        this.loading.set(false);
        console.error('Error loading availability:', err);
        this.messageService.add({
          severity: 'error',
          summary: 'Error',
          detail: 'Failed to load availability data',
        });
      },
    });
  }

  // Navigation
  goToToday(): void {
    this.startDate.set(this.getMondayOfWeek(new Date()));
  }

  goToPrev(): void {
    const current = this.startDate();
    const days = this.visibleDays();
    this.startDate.set(this.addDays(current, -days));
  }

  goToNext(): void {
    const current = this.startDate();
    const days = this.visibleDays();
    this.startDate.set(this.addDays(current, days));
  }

  onRangeChange(): void {
    // Effect will automatically reload availability
  }

  // Cell interaction
  onCellClick(cell: GridCell): void {
    this.selectedCell.set(cell);
    if (cell.availability) {
      this.dialogForm.set({
        startMinuteOfDay: cell.availability.startMinuteOfDay,
        endMinuteOfDay: cell.availability.endMinuteOfDay,
      });
    } else {
      this.dialogForm.set({
        startMinuteOfDay: 480, // 08:00
        endMinuteOfDay: 960,   // 16:00
      });
    }
    this.showDialog.set(true);
  }

  saveAvailability(): void {
    const cell = this.selectedCell();
    if (!cell) return;

    const form = this.dialogForm();
    if (form.endMinuteOfDay <= form.startMinuteOfDay) {
      this.messageService.add({
        severity: 'error',
        summary: 'Validation Error',
        detail: 'End time must be after start time',
      });
      return;
    }

    this.loading.set(true);
    const dateYmd = this.dateToYmd(cell.date);

    this.availabilityApi
      .upsertAvailability(cell.driver.toolId, dateYmd, form)
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
          // Update map immediately - normalize date key
          const map = { ...this.availabilityMap() };
          if (!map[cell.driver.toolId]) {
            map[cell.driver.toolId] = {};
          }
          // Normalize date to yyyy-MM-dd format for consistent lookup
          const normalizedDate = availability.date.split('T')[0];
          map[cell.driver.toolId][normalizedDate] = availability;
          this.availabilityMap.set(map);

          this.showDialog.set(false);
          this.messageService.add({
            severity: 'success',
            summary: 'Success',
            detail: 'Availability saved',
          });
        }
      });
  }

  clearAvailability(): void {
    const cell = this.selectedCell();
    if (!cell || !cell.availability) return;

    this.loading.set(true);
    const dateYmd = this.dateToYmd(cell.date);

    this.availabilityApi
      .deleteAvailability(cell.driver.toolId, dateYmd)
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
        // Update map immediately
        const map = { ...this.availabilityMap() };
        if (map[cell.driver.toolId]) {
          delete map[cell.driver.toolId][dateYmd];
        }
        this.availabilityMap.set(map);

        this.showDialog.set(false);
        this.messageService.add({
          severity: 'success',
          summary: 'Success',
          detail: 'Availability cleared',
        });
      });
  }

  getCellForDriverAndDate(driver: DriverDto, date: Date): GridCell | null {
    const cells = this.gridCells();
    return cells.find((c) => c.driver.toolId === driver.toolId && this.dateToYmd(c.date) === this.dateToYmd(date)) || null;
  }

  getAvailabilityForCell(driver: DriverDto, date: Date): DriverAvailabilityDto | null {
    const map = this.availabilityMap();
    const dateYmd = this.dateToYmd(date);
    return map[driver.toolId]?.[dateYmd] || null;
  }

  // Bulk operations
  downloadTemplate(): void {
    this.bulkApi
      .downloadAvailabilityTemplateExcel()
      .pipe(
        catchError((err) => {
          this.messageService.add({
            severity: 'error',
            summary: 'Error',
            detail: err.detail || err.message || 'Failed to download availability template',
          });
          return of(null);
        })
      )
      .subscribe((blob) => {
        if (blob) {
          const url = window.URL.createObjectURL(blob);
          const a = document.createElement('a');
          a.href = url;
          a.download = `driver-availability-template.xlsx`;
          document.body.appendChild(a);
          a.click();
          document.body.removeChild(a);
          window.URL.revokeObjectURL(url);
          this.messageService.add({
            severity: 'success',
            summary: 'Success',
            detail: 'Template downloaded',
          });
        }
      });
  }

  downloadServiceTypesTemplate(): void {
    this.bulkApi
      .downloadServiceTypesTemplateExcel()
      .pipe(
        catchError((err) => {
          this.messageService.add({
            severity: 'error',
            summary: 'Error',
            detail: err.detail || err.message || 'Failed to download service types template',
          });
          return of(null);
        })
      )
      .subscribe((blob) => {
        if (blob) {
          const url = window.URL.createObjectURL(blob);
          const a = document.createElement('a');
          a.href = url;
          a.download = `driver-service-types-template.xlsx`;
          document.body.appendChild(a);
          a.click();
          document.body.removeChild(a);
          window.URL.revokeObjectURL(url);
          this.messageService.add({
            severity: 'success',
            summary: 'Success',
            detail: 'Template downloaded',
          });
        }
      });
  }

  triggerUpload(kind: 'availability' | 'serviceTypes', input: HTMLInputElement): void {
    this.pendingUploadKind = kind;
    input.click();
  }

  onFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    if (input.files && input.files.length > 0) {
      const file = input.files[0];
      if (this.pendingUploadKind === 'serviceTypes') {
        this.uploadServiceTypesExcel(file);
      } else {
        this.uploadExcel(file);
      }
      input.value = '';
      this.pendingUploadKind = null;
    }
  }

  uploadExcel(file: File): void {
    this.loading.set(true);
    this.bulkApi
      .uploadAvailabilityExcel(file)
      .pipe(
        catchError((err) => {
          this.loading.set(false);
          this.messageService.add({
            severity: 'error',
            summary: 'Error',
            detail: err.detail || err.message || 'Failed to upload availability file',
          });
          return of(null);
        })
      )
      .subscribe((result) => {
        this.loading.set(false);
        if (result) {
          // Reload data
          this.loadDrivers();
          this.loadAvailabilityForRange(this.startDate(), this.endDate());

          // Show result
          const message = `Inserted: ${result.inserted}, Updated: ${result.updated}, Deleted: ${result.deleted}`;
          this.messageService.add({
            severity: result.errors.length > 0 ? 'warn' : 'success',
            summary: 'Upload Complete',
            detail: message + (result.errors.length > 0 ? ` (${result.errors.length} errors)` : ''),
            life: 5000,
          });

          // Show errors if any
          if (result.errors.length > 0) {
            console.error('Bulk upload errors:', result.errors);
            // You could show errors in a dialog here
          }
        }
      });
  }

  uploadServiceTypesExcel(file: File): void {
    this.loading.set(true);
    this.bulkApi
      .uploadServiceTypesExcel(file)
      .pipe(
        catchError((err) => {
          this.loading.set(false);
          this.messageService.add({
            severity: 'error',
            summary: 'Error',
            detail: err.detail || err.message || 'Failed to upload service types file',
          });
          return of(null);
        })
      )
      .subscribe((result) => {
        this.loading.set(false);
        if (result) {
          this.loadDrivers();
          const message = `Updated: ${result.updated}`;
          this.messageService.add({
            severity: result.errors.length > 0 ? 'warn' : 'success',
            summary: 'Upload Complete',
            detail: message + (result.errors.length > 0 ? ` (${result.errors.length} errors)` : ''),
            life: 5000,
          });

          if (result.errors.length > 0) {
            console.error('Bulk upload errors:', result.errors);
          }
        }
      });
  }

  // Driver CRUD operations
  openEditDriverDialog(driver: DriverDto): void {
    this.isEditMode.set(true);
    this.selectedDriverForEdit.set(driver);
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

  saveDriver(): void {
    const form = this.driverForm();
    const isEdit = this.isEditMode();
    const selected = this.selectedDriverForEdit();

    if (!form.name.trim()) {
      this.messageService.add({
        severity: 'error',
        summary: 'Validation Error',
        detail: 'Name is required',
      });
      return;
    }

    if (form.erpId <= 0) {
      this.messageService.add({
        severity: 'error',
        summary: 'Validation Error',
        detail: 'ErpId must be greater than 0',
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
            this.loadAvailabilityForRange(this.startDate(), this.endDate());
          }
        });
    } else {
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
            // Reload drivers and availability
            this.loadDrivers();
            this.loadAvailabilityForRange(this.startDate(), this.endDate());
          });
      },
    });
  }

  private isSuperAdmin(): boolean {
    const roles = this.auth.currentUser()?.roles ?? [];
    return roles.includes('SuperAdmin');
  }
}
