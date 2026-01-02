import { Component, computed, inject, signal } from '@angular/core';
import { CommonModule, DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ButtonModule } from 'primeng/button';
import { TableModule } from 'primeng/table';
import { InputTextModule } from 'primeng/inputtext';
import { DropdownModule } from 'primeng/dropdown';
import { DialogModule } from 'primeng/dialog';
import { CalendarModule } from 'primeng/calendar';
import { InputNumberModule } from 'primeng/inputnumber';
import { ToastModule } from 'primeng/toast';
import { InputTextarea } from 'primeng/inputtextarea';
import { TagModule } from 'primeng/tag';
import { TooltipModule } from 'primeng/tooltip';
import { MessageService } from 'primeng/api';
import type { HttpErrorResponse, HttpResponse } from '@angular/common/http';
import { ServiceLocationsApiService } from '@services/service-locations-api.service';
import { ServiceTypesApiService } from '@services/service-types-api.service';
import { ServiceLocationOwnersApiService, type ServiceLocationOwnerDto } from '@services/service-location-owners-api.service';
import { AuthService } from '@services/auth.service';
import { HelpManualComponent } from '@components/help-manual/help-manual.component';
import type {
  ServiceLocationDto,
  CreateServiceLocationRequest,
  UpdateServiceLocationRequest,
  ServiceLocationListParams,
  BulkInsertResultDto,
  ServiceLocationOpeningHoursDto,
  ServiceLocationExceptionDto,
  ServiceLocationConstraintDto,
  ResolveServiceLocationGeoRequest,
} from '@models/service-location.model';
import type { ServiceTypeDto } from '@models/service-type.model';
import { catchError, finalize, forkJoin, of } from 'rxjs';
import { toYmd } from '@utils/date.utils';

type OpeningHoursFormRow = ServiceLocationOpeningHoursDto & { label: string; hasLunchBreak?: boolean };
type ExceptionFormRow = ServiceLocationExceptionDto & { note?: string };
type ServiceLocationDetail = {
  loading: boolean;
  hours: ServiceLocationOpeningHoursDto[];
  exceptions: ServiceLocationExceptionDto[];
  constraints: ServiceLocationConstraintDto | null;
};

@Component({
  selector: 'app-service-locations',
  imports: [
    CommonModule,
    FormsModule,
    ButtonModule,
    TableModule,
    InputTextModule,
    DropdownModule,
    DialogModule,
    CalendarModule,
    InputNumberModule,
    ToastModule,
    InputTextarea,
    TagModule,
    TooltipModule,
    HelpManualComponent,
  ],
  providers: [MessageService],
  templateUrl: './service-locations.page.html',
  standalone: true,
})
export class ServiceLocationsPage {
  private readonly api = inject(ServiceLocationsApiService);
  private readonly serviceTypesApi = inject(ServiceTypesApiService);
  private readonly ownersApi = inject(ServiceLocationOwnersApiService);
  private readonly messageService = inject(MessageService);
  private readonly auth = inject(AuthService);

  // Data
  items = signal<ServiceLocationDto[]>([]);
  totalCount = signal(0);
  loading = signal(false);
  canEdit = computed(() => {
    const roles = this.auth.currentUser()?.roles ?? [];
    return roles.some((role) => role === 'SuperAdmin' || role === 'Admin' || role === 'Planner');
  });
  serviceTypes = signal<ServiceTypeDto[]>([]);
  formServiceTypes = signal<ServiceTypeDto[]>([]);
  owners = signal<ServiceLocationOwnerDto[]>([]);
  selectedServiceTypeId = signal<number | null>(null); // For bulk operations
  selectedOwnerId = signal<number | null>(null); // For bulk operations
  private isUpdatingDate = false;
  // Cache dates to prevent creating new objects on every change detection
  private dateCache = new Map<string, Date>();
  private savedServiceMinutes = new Map<string, number>();

  // Filters
  status = signal<'Open' | 'Done' | 'Cancelled' | 'Planned' | 'NotVisited' | null>('Open');
  search = signal('');
  fromDue = signal<Date | null>(null);
  toDue = signal<Date | null>(null);
  serviceTypeId = signal<number | null>(null); // Filter by service type
  ownerId = signal<number | null>(null); // Filter by owner
  page = signal(1);
  pageSize = signal(50);

  // Dialog state
  showDialog = signal(false);
  showPriorityDialog = signal(false);
  showBulkResultDialog = signal(false);
  isEditMode = signal(false);
  selectedItem = signal<ServiceLocationDto | null>(null);
  selectedItemForPriority = signal<ServiceLocationDto | null>(null);
  bulkResult = signal<BulkInsertResultDto | null>(null);

  form = signal<CreateServiceLocationRequest>({
    erpId: 0,
    name: '',
    address: '',
    latitude: null,
    longitude: null,
    dueDate: toYmd(new Date()),
    priorityDate: undefined,
    serviceMinutes: 20,
    serviceTypeId: 0, // Will be set when service types are loaded
    ownerId: 0, // Will be set when owners are loaded
    driverInstruction: '',
    extraInstructions: [],
  });

  readonly weekDayLabels = ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'];
  useDefaultOpeningHours = signal(true);
  openingHours = signal<OpeningHoursFormRow[]>([]);
  exceptions = signal<ExceptionFormRow[]>([]);
  constraints = signal<ServiceLocationConstraintDto>({
    minVisitDurationMinutes: null,
    maxVisitDurationMinutes: null,
  });
  expandedRowKeys = signal<Record<string, boolean>>({});
  rowDetails = signal<Record<string, ServiceLocationDetail>>({});
  geoResolving = signal(false);
  geoResolveFailed = signal(false);
  geoValidationMessage = signal<string | null>(null);

  priorityDate = signal<Date | null>(null);

  statusFilterOptions = [
    { label: 'All', value: null },
    { label: 'Open', value: 'Open' },
    { label: 'Completed', value: 'Done' },
    { label: 'Cancelled', value: 'Cancelled' },
    { label: 'Planned', value: 'Planned' },
    { label: 'Not Visited', value: 'NotVisited' },
  ];

  statusEditOptions = [
    { label: 'Open', value: 'Open' },
    { label: 'Completed', value: 'Done' },
    { label: 'Cancelled', value: 'Cancelled' },
    { label: 'Planned', value: 'Planned' },
    { label: 'Not Visited', value: 'NotVisited' },
  ];

  updateMinVisitDuration(value: number | null | undefined): void {
    this.constraints.update((current) => ({
      ...current,
      minVisitDurationMinutes: value ?? null,
    }));
  }

  updateMaxVisitDuration(value: number | null | undefined): void {
    this.constraints.update((current) => ({
      ...current,
      maxVisitDurationMinutes: value ?? null,
    }));
  }

  serviceTypeFilterOptions = computed(() => [
    { label: 'All', value: null },
    ...this.serviceTypes().map((t) => ({ label: t.name, value: t.id })),
  ]);

  ownerFilterOptions = computed(() => {
    const current = this.auth.currentUser();
    const isSuperAdmin = current?.roles.includes('SuperAdmin') ?? false;
    const options = this.owners().map((o) => ({ label: o.name, value: o.id }));
    return isSuperAdmin ? [{ label: 'All', value: null }, ...options] : options;
  });

  // Computed
  get showDialogValue(): boolean {
    return this.showDialog();
  }
  set showDialogValue(value: boolean) {
    this.showDialog.set(value);
  }

  get showPriorityDialogValue(): boolean {
    return this.showPriorityDialog();
  }
  set showPriorityDialogValue(value: boolean) {
    this.showPriorityDialog.set(value);
  }

  constructor() {
    // Set default date range: today to today + 14 days
    const today = new Date();
    today.setHours(0, 0, 0, 0);
    this.fromDue.set(today);
    
    const toDate = new Date(today);
    toDate.setDate(toDate.getDate() + 14);
    this.toDue.set(toDate);
    
    // Load owners first, then service types scoped to the selected owner
    this.loadOwners();
    
    // Don't auto-load - user must click "Load" button
  }

  loadServiceTypes(ownerId?: number | null): void {
    this.serviceTypesApi
      .getAll(false, ownerId ?? undefined)
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
      .subscribe((types) => {
        this.serviceTypes.set(types);
        const firstId = types[0]?.id ?? null;
        // Set default selected service type for bulk operations (first active one)
        if (types.length > 0) {
          const currentSelected = this.selectedServiceTypeId();
          if (!currentSelected || !types.some((t) => t.id === currentSelected)) {
            this.selectedServiceTypeId.set(firstId);
          }
        } else {
          this.selectedServiceTypeId.set(null);
        }
      });
  }

  loadFormServiceTypes(ownerId: number | null): void {
    if (!ownerId) {
      this.formServiceTypes.set([]);
      this.form.update((current) => ({
        ...current,
        serviceTypeId: 0,
      }));
      return;
    }

    this.serviceTypesApi
      .getAll(false, ownerId)
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
      .subscribe((types) => {
        this.formServiceTypes.set(types);
        this.form.update((current) => {
          const isValid = types.some((t) => t.id === current.serviceTypeId);
          return {
            ...current,
            serviceTypeId: isValid ? current.serviceTypeId : (types[0]?.id ?? 0),
          };
        });
      });
  }

  loadOwners(): void {
    const current = this.auth.currentUser();
    const isSuperAdmin = current?.roles.includes('SuperAdmin') ?? false;
    const currentOwnerId = current?.ownerId ?? null;

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
        const filtered = !isSuperAdmin && currentOwnerId
          ? owners.filter((o) => o.id === currentOwnerId)
          : owners;
        this.owners.set(filtered);
        // Set default selected owner for bulk operations (first active one)
        if (filtered.length > 0 && !this.selectedOwnerId()) {
          this.selectedOwnerId.set(filtered[0].id);
        }
        // Set default owner in form (first active one)
        if (filtered.length > 0) {
          const currentForm = this.form();
          this.form.set({
            ...currentForm,
            ownerId: filtered[0].id,
          });
          this.loadFormServiceTypes(filtered[0].id);
        }

        if (!isSuperAdmin && currentOwnerId) {
          this.ownerId.set(currentOwnerId);
        }

        const serviceTypeOwnerId = this.selectedOwnerId() ?? this.ownerId() ?? currentOwnerId ?? null;
        this.loadServiceTypes(serviceTypeOwnerId);
      });
  }

  onOwnerFilterChange(ownerId: number | null): void {
    this.ownerId.set(ownerId);
    if (ownerId) {
      this.selectedOwnerId.set(ownerId);
    }
    this.loadServiceTypes(ownerId ?? this.selectedOwnerId() ?? null);
  }

  onBulkOwnerChange(ownerId: number | null): void {
    this.selectedOwnerId.set(ownerId);
    this.loadServiceTypes(ownerId ?? this.ownerId() ?? null);
  }

  onFormOwnerChange(ownerId: number | null): void {
    const nextOwnerId = ownerId ?? 0;
    this.form.update((current) => ({
      ...current,
      ownerId: nextOwnerId,
    }));
    this.loadFormServiceTypes(nextOwnerId);
  }

  updateFormServiceTypeId(serviceTypeId: number | null): void {
    this.form.update((current) => ({
      ...current,
      serviceTypeId: serviceTypeId ?? 0,
    }));
  }

  onRowExpand(event: { data: ServiceLocationDto }): void {
    const item = event?.data;
    if (!item) {
      return;
    }
    const next = { ...this.expandedRowKeys() };
    next[item.toolId] = true;
    this.expandedRowKeys.set(next);
    this.loadRowDetails(item);
  }

  onRowCollapse(event: { data: ServiceLocationDto }): void {
    const item = event?.data;
    if (!item) {
      return;
    }
    const next = { ...this.expandedRowKeys() };
    delete next[item.toolId];
    this.expandedRowKeys.set(next);
  }

  getRowDetail(item: ServiceLocationDto): ServiceLocationDetail | null {
    return this.rowDetails()[item.toolId] ?? null;
  }

  private loadRowDetails(item: ServiceLocationDto): void {
    const key = item.toolId;
    const existing = this.rowDetails()[key];
    if (existing && !existing.loading) {
      return;
    }

    this.rowDetails.set({
      ...this.rowDetails(),
      [key]: {
        loading: true,
        hours: existing?.hours ?? [],
        exceptions: existing?.exceptions ?? [],
        constraints: existing?.constraints ?? null,
      },
    });

    forkJoin({
      hours: this.api.getOpeningHours(key).pipe(catchError(() => of([]))),
      exceptions: this.api.getExceptions(key).pipe(catchError(() => of([]))),
      constraints: this.api.getConstraints(key).pipe(
        catchError(() =>
          of({
            minVisitDurationMinutes: null,
            maxVisitDurationMinutes: null,
          })
        )
      ),
    }).subscribe(({ hours, exceptions, constraints }) => {
      this.rowDetails.set({
        ...this.rowDetails(),
        [key]: {
          loading: false,
          hours,
          exceptions,
          constraints: {
            minVisitDurationMinutes: constraints.minVisitDurationMinutes ?? null,
            maxVisitDurationMinutes: constraints.maxVisitDurationMinutes ?? null,
          },
        },
      });
    });
  }

  loadData(resetPage = true): void {
    // Validate dates are set
    if (!this.fromDue() || !this.toDue()) {
      this.messageService.add({
        severity: 'warn',
        summary: 'Validation',
        detail: 'Please select both From Due and To Due dates',
      });
      return;
    }

    // Validate date range
    if (this.fromDue()! > this.toDue()!) {
      this.messageService.add({
        severity: 'warn',
        summary: 'Validation',
        detail: 'From Due date must be before or equal to To Due date',
      });
      return;
    }

    this.loading.set(true);
    if (resetPage) {
      this.page.set(1); // Reset to first page when loading new filters
    }
    const params: ServiceLocationListParams = {
      status: this.status() || undefined,
      search: this.search() || undefined,
      fromDue: this.fromDue() ? toYmd(this.fromDue()!) : undefined,
      toDue: this.toDue() ? toYmd(this.toDue()!) : undefined,
      serviceTypeId: this.serviceTypeId() || undefined,
      ownerId: this.ownerId() || undefined,
      page: this.page(),
      pageSize: this.pageSize(),
      order: 'priorityThenDue',
    };

    this.api
      .getList(params)
      .pipe(
        catchError((err) => {
          this.loading.set(false);
          this.messageService.add({
            severity: 'error',
            summary: 'Error',
            detail: err.detail || err.message || 'Failed to load service locations',
          });
          return of({ items: [], page: 1, pageSize: 50, totalCount: 0, totalPages: 0 });
        })
      )
      .subscribe((result) => {
        this.loading.set(false);
        // Clear date cache when loading new data to prevent stale references
        this.dateCache.clear();
        // Create new array reference to trigger change detection
        this.items.set([...result.items]);
        this.totalCount.set(result.totalCount);
        this.savedServiceMinutes.clear();
        for (const item of result.items) {
          this.savedServiceMinutes.set(item.toolId, item.serviceMinutes);
        }
        this.expandedRowKeys.set({});
        this.rowDetails.set({});
      });
  }

  onPageChange(event: any): void {
    // Only change page if we have data loaded
    if (this.items().length === 0 && this.totalCount() === 0) {
      return; // Don't trigger load if no data has been loaded yet
    }
    const nextRows = event?.rows ?? this.pageSize();
    const nextPage = event?.page != null
      ? event.page + 1
      : Math.floor((event?.first ?? 0) / nextRows) + 1;
    this.page.set(nextPage);
    this.pageSize.set(nextRows);
    this.loadData(false);
  }

  getOrderDate(item: ServiceLocationDto): string {
    return item.priorityDate || item.dueDate;
  }

  openAddDialog(): void {
    this.isEditMode.set(false);
    this.selectedItem.set(null);
    const defaultOwnerId = this.owners().length > 0 ? this.owners()[0].id : 0;
    this.form.set({
      erpId: 0,
      name: '',
      address: '',
      latitude: null,
      longitude: null,
      dueDate: toYmd(new Date()),
      priorityDate: undefined,
      serviceMinutes: 20,
      serviceTypeId: 0,
      ownerId: defaultOwnerId,
      driverInstruction: '',
      extraInstructions: [],
    });
    this.loadFormServiceTypes(defaultOwnerId);
    this.geoResolveFailed.set(false);
    this.geoResolving.set(false);
    this.geoValidationMessage.set(null);
    this.useDefaultOpeningHours.set(true);
    this.openingHours.set(this.buildDefaultOpeningHours());
    this.exceptions.set([]);
    this.constraints.set({ minVisitDurationMinutes: null, maxVisitDurationMinutes: null });
    this.showDialog.set(true);
  }

  openEditDialog(item: ServiceLocationDto): void {
    if (!this.canEdit()) {
      return;
    }
    this.isEditMode.set(true);
    this.selectedItem.set(item);
    const latitude = item.latitude === 0 ? null : item.latitude;
    const longitude = item.longitude === 0 ? null : item.longitude;
    this.form.set({
      erpId: item.erpId,
      name: item.name,
      address: item.address || '',
      latitude,
      longitude,
      dueDate: item.dueDate,
      priorityDate: item.priorityDate,
      serviceMinutes: item.serviceMinutes,
      serviceTypeId: item.serviceTypeId,
      ownerId: item.ownerId,
      driverInstruction: item.driverInstruction || '',
      extraInstructions: item.extraInstructions ? [...item.extraInstructions] : [],
    });
    this.loadFormServiceTypes(item.ownerId);
    this.geoResolveFailed.set(false);
    this.geoResolving.set(false);
    this.geoValidationMessage.set(null);
    this.loadLocationExtras(item.toolId);
    this.showDialog.set(true);
  }

  private buildDefaultOpeningHours(): OpeningHoursFormRow[] {
    return this.weekDayLabels.map((label, index) => ({
      id: undefined,
      dayOfWeek: index,
      label,
      openTime: '08:00',
      closeTime: '17:00',
      openTime2: null,
      closeTime2: null,
      isClosed: false,
      hasLunchBreak: false,
    }));
  }

  private normalizeOpeningHours(items: ServiceLocationOpeningHoursDto[]): OpeningHoursFormRow[] {
    const rows = this.buildDefaultOpeningHours();
    for (const item of items) {
      const target = rows.find((row) => row.dayOfWeek === item.dayOfWeek);
      if (!target) continue;
      target.openTime = item.openTime ?? target.openTime;
      target.closeTime = item.closeTime ?? target.closeTime;
      target.openTime2 = item.openTime2 ?? null;
      target.closeTime2 = item.closeTime2 ?? null;
      target.hasLunchBreak = !!(item.openTime2 || item.closeTime2);
      target.isClosed = item.isClosed;
    }
    return rows;
  }

  private loadLocationExtras(toolId: string): void {
    forkJoin({
      hours: this.api.getOpeningHours(toolId).pipe(catchError(() => of([]))),
      exceptions: this.api.getExceptions(toolId).pipe(catchError(() => of([]))),
      constraints: this.api.getConstraints(toolId).pipe(
        catchError(() =>
          of({
            minVisitDurationMinutes: null,
            maxVisitDurationMinutes: null,
          })
        )
      ),
    }).subscribe(({ hours, exceptions, constraints }) => {
      const hasHours = hours.length > 0;
      this.useDefaultOpeningHours.set(!hasHours);
      this.openingHours.set(hasHours ? this.normalizeOpeningHours(hours) : this.buildDefaultOpeningHours());
      this.exceptions.set(
        exceptions.map((ex) => ({
          id: ex.id,
          date: toYmd(new Date(ex.date)),
          openTime: ex.openTime ?? '08:00',
          closeTime: ex.closeTime ?? '17:00',
          isClosed: ex.isClosed,
          note: ex.note ?? '',
        }))
      );
      this.constraints.set({
        minVisitDurationMinutes: constraints.minVisitDurationMinutes ?? null,
        maxVisitDurationMinutes: constraints.maxVisitDurationMinutes ?? null,
      });
    });
  }

  private saveLocationExtras(toolId: string) {
    const hoursPayload = this.useDefaultOpeningHours()
      ? []
      : this.openingHours().map(({ label, hasLunchBreak, ...rest }) => ({
          ...rest,
          openTime: rest.isClosed ? null : rest.openTime || null,
          closeTime: rest.isClosed ? null : rest.closeTime || null,
          openTime2: rest.isClosed || !hasLunchBreak ? null : rest.openTime2 || null,
          closeTime2: rest.isClosed || !hasLunchBreak ? null : rest.closeTime2 || null,
        }));

    const exceptionsPayload = this.exceptions()
      .filter((ex) => !!ex.date)
      .map((ex) => ({
        id: ex.id,
        date: ex.date,
        openTime: ex.isClosed ? null : ex.openTime || null,
        closeTime: ex.isClosed ? null : ex.closeTime || null,
        isClosed: ex.isClosed,
        note: ex.note?.trim() || undefined,
      }));

    const constraintPayload: ServiceLocationConstraintDto = {
      minVisitDurationMinutes: this.constraints().minVisitDurationMinutes ?? null,
      maxVisitDurationMinutes: this.constraints().maxVisitDurationMinutes ?? null,
    };

    return forkJoin({
      hours: this.api.saveOpeningHours(toolId, hoursPayload),
      exceptions: this.api.saveExceptions(toolId, exceptionsPayload),
      constraints: this.api.saveConstraints(toolId, constraintPayload),
    });
  }

  addExceptionRow(): void {
    const rows = this.exceptions();
    rows.push({
      date: toYmd(new Date()),
      openTime: '08:00',
      closeTime: '17:00',
      isClosed: false,
      note: '',
    });
    this.exceptions.set([...rows]);
  }

  removeExceptionRow(index: number): void {
    const rows = this.exceptions();
    rows.splice(index, 1);
    this.exceptions.set([...rows]);
  }

  toggleLunchBreak(row: OpeningHoursFormRow): void {
    if (this.useDefaultOpeningHours() || row.isClosed) {
      return;
    }

    const rows = this.openingHours();
    const target = rows.find((item) => item.dayOfWeek === row.dayOfWeek);
    if (!target) {
      return;
    }

    const nextValue = !target.hasLunchBreak;
    target.hasLunchBreak = nextValue;
    if (!nextValue) {
      target.openTime2 = null;
      target.closeTime2 = null;
    }

    this.openingHours.set([...rows]);
  }

  onOpeningHoursClosedChange(row: OpeningHoursFormRow, isClosed: boolean): void {
    const rows = this.openingHours();
    const target = rows.find((item) => item.dayOfWeek === row.dayOfWeek);
    if (!target) {
      return;
    }

    target.isClosed = isClosed;
    if (isClosed) {
      target.hasLunchBreak = false;
      target.openTime2 = null;
      target.closeTime2 = null;
    }

    this.openingHours.set([...rows]);
  }

  addInstructionLine(): void {
    const form = this.form();
    const lines = [...(form.extraInstructions ?? [])];
    const lastLine = lines[lines.length - 1];
    if (typeof lastLine === 'string' && !lastLine.trim()) {
      this.messageService.add({
        severity: 'warn',
        summary: 'Validation',
        detail: 'Fill in the current line before adding a new one.',
      });
      return;
    }

    lines.push('');
    this.form.set({ ...form, extraInstructions: lines });
  }

  updateInstructionLine(index: number, value: string): void {
    const form = this.form();
    const lines = [...(form.extraInstructions ?? [])];
    lines[index] = value;
    this.form.set({ ...form, extraInstructions: lines });
  }

  removeInstructionLine(index: number): void {
    const form = this.form();
    const lines = [...(form.extraInstructions ?? [])];
    lines.splice(index, 1);
    this.form.set({ ...form, extraInstructions: lines });
  }

  private normalizeInstructions(lines?: string[] | null): string[] {
    if (!lines) {
      return [];
    }

    return lines
      .map((line) => line.trim())
      .filter((line) => line.length > 0);
  }

  openPriorityDialog(item: ServiceLocationDto): void {
    this.selectedItemForPriority.set(item);
    this.priorityDate.set(item.priorityDate ? new Date(item.priorityDate) : null);
    this.showPriorityDialog.set(true);
  }

  onAddressBlur(): void {
    this.attemptGeoResolve();
  }

  onCoordinatesBlur(): void {
    this.attemptGeoResolve();
  }

  private attemptGeoResolve(): void {
    if (this.geoResolving()) {
      return;
    }

    const form = this.form();
    const address = form.address?.trim() ?? '';
    const hasAddress = address.length > 0;
    const hasLatitude = form.latitude !== null && form.latitude !== undefined;
    const hasLongitude = form.longitude !== null && form.longitude !== undefined;

    if (hasLatitude !== hasLongitude) {
      this.geoValidationMessage.set('Latitude and longitude must be both filled.');
      return;
    }

    if (!hasAddress && !hasLatitude) {
      this.geoValidationMessage.set(null);
      return;
    }

    this.geoValidationMessage.set(null);

    if (hasAddress && (!hasLatitude || !hasLongitude)) {
      this.resolveGeo({ address });
      return;
    }

    if (!hasAddress && hasLatitude && hasLongitude) {
      this.resolveGeo({ latitude: form.latitude, longitude: form.longitude });
    }
  }

  private resolveGeo(request: ResolveServiceLocationGeoRequest): void {
    this.geoResolving.set(true);
    this.geoResolveFailed.set(false);

    const payload: ResolveServiceLocationGeoRequest = {
      address: request.address?.trim() || undefined,
      latitude: request.latitude ?? null,
      longitude: request.longitude ?? null,
    };

    this.api
      .resolveGeo(payload)
      .pipe(
        finalize(() => this.geoResolving.set(false)),
        catchError((err) => {
          this.geoResolveFailed.set(true);
          const message = err.detail || err.message || 'Unable to resolve address or coordinates.';
          this.geoValidationMessage.set(message);
          this.messageService.add({
            severity: 'error',
            summary: 'Geocoding failed',
            detail: message,
          });
          return of(null);
        })
      )
      .subscribe((result) => {
        if (!result) {
          return;
        }
        const current = this.form();
        this.form.set({
          ...current,
          address: result.address,
          latitude: result.latitude,
          longitude: result.longitude,
        });
        this.geoValidationMessage.set(null);
      });
  }

  private getGeoValidationError(form: CreateServiceLocationRequest): string | null {
    const address = form.address?.trim() ?? '';
    const hasAddress = address.length > 0;
    const hasLatitude = form.latitude !== null && form.latitude !== undefined;
    const hasLongitude = form.longitude !== null && form.longitude !== undefined;

    if (hasLatitude !== hasLongitude) {
      return 'Latitude and longitude must be both filled.';
    }

    if (!hasAddress && !hasLatitude) {
      return 'Provide an address or both latitude and longitude.';
    }

    return null;
  }

  private parseTimeToMinutes(value: string | null | undefined): number | null {
    if (!value) {
      return null;
    }
    const parts = value.split(':');
    if (parts.length < 2) {
      return null;
    }
    const hours = Number(parts[0]);
    const minutes = Number(parts[1]);
    if (Number.isNaN(hours) || Number.isNaN(minutes)) {
      return null;
    }
    if (hours < 0 || hours > 23 || minutes < 0 || minutes > 59) {
      return null;
    }
    return hours * 60 + minutes;
  }

  private validateOpeningHours(): string | null {
    if (this.useDefaultOpeningHours()) {
      return null;
    }

    for (const row of this.openingHours()) {
      if (row.isClosed) {
        continue;
      }

      const open1 = this.parseTimeToMinutes(row.openTime);
      const close1 = this.parseTimeToMinutes(row.closeTime);
      if (open1 === null || close1 === null) {
        return `${row.label}: From1 and To1 are required.`;
      }
      if (open1 >= close1) {
        return `${row.label}: From1 must be before To1.`;
      }

      if (row.hasLunchBreak) {
        const open2 = this.parseTimeToMinutes(row.openTime2);
        const close2 = this.parseTimeToMinutes(row.closeTime2);
        if (open2 === null || close2 === null) {
          return `${row.label}: From2 and To2 are required.`;
        }
        if (open2 >= close2) {
          return `${row.label}: From2 must be before To2.`;
        }
        if (close1 > open2) {
          return `${row.label}: From2 must be after To1.`;
        }
      }
    }

    return null;
  }

  save(): void {
    const form = this.form();
    const isEdit = this.isEditMode();
    const selected = this.selectedItem();

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

    const geoError = this.getGeoValidationError(form);
    if (geoError) {
      this.geoValidationMessage.set(geoError);
      return;
    }

    if (this.geoResolving()) {
      this.geoValidationMessage.set('Resolving address or coordinates. Please wait.');
      return;
    }

    if (this.geoResolveFailed()) {
      const hasAddress = (form.address?.trim() ?? '').length > 0;
      const hasLatitude = form.latitude !== null && form.latitude !== undefined;
      const hasLongitude = form.longitude !== null && form.longitude !== undefined;
      if (!(hasAddress && hasLatitude && hasLongitude)) {
        return;
      }
      this.geoResolveFailed.set(false);
    }

    this.geoValidationMessage.set(null);

    const openingHoursError = this.validateOpeningHours();
    if (openingHoursError) {
      this.messageService.add({
        severity: 'error',
        summary: 'Validation Error',
        detail: openingHoursError,
      });
      return;
    }

    const extraInstructions = this.normalizeInstructions(form.extraInstructions);

    this.loading.set(true);
    const request: CreateServiceLocationRequest | UpdateServiceLocationRequest = {
      erpId: form.erpId,
      name: form.name.trim(),
      address: form.address?.trim() || undefined,
      latitude: form.latitude,
      longitude: form.longitude,
      dueDate: toYmd(new Date(form.dueDate)),
      priorityDate: form.priorityDate ? toYmd(new Date(form.priorityDate)) : undefined,
      serviceMinutes: form.serviceMinutes,
      serviceTypeId: form.serviceTypeId,
      ownerId: form.ownerId,
      driverInstruction: form.driverInstruction?.trim() || undefined,
      extraInstructions,
    };

    const apiCall = isEdit && selected
      ? this.api.update(selected.toolId, request as UpdateServiceLocationRequest)
      : this.api.create(request as CreateServiceLocationRequest);

    apiCall
      .pipe(
        catchError((err) => {
          this.loading.set(false);
          this.messageService.add({
            severity: 'error',
            summary: 'Error',
            detail: err.detail || err.message || 'Failed to save service location',
          });
          return of(null);
        })
        )
        .subscribe((result) => {
          this.loading.set(false);
          if (result) {
            this.saveLocationExtras(result.toolId).pipe(
              catchError((err) => {
                this.messageService.add({
                  severity: 'error',
                  summary: 'Error',
                  detail: err.detail || err.message || 'Failed to save opening hours or exceptions',
                });
                return of(null);
              })
            ).subscribe(() => {
              this.showDialog.set(false);
              this.messageService.add({
                severity: 'success',
                summary: 'Success',
                detail: isEdit ? 'Service location updated' : 'Service location created',
              });
              this.loadData();
            });
          }
        });
    }

  savePriorityDate(): void {
    const item = this.selectedItemForPriority();
    if (!item) return;

    const priorityDateStr = this.priorityDate() ? toYmd(this.priorityDate()!) : null;

    this.loading.set(true);
    this.api
      .setPriorityDate(item.toolId, priorityDateStr)
      .pipe(
        catchError((err) => {
          this.loading.set(false);
          this.messageService.add({
            severity: 'error',
            summary: 'Error',
            detail: err.detail || err.message || 'Failed to set priority date',
          });
          return of(null);
        })
      )
      .subscribe((result) => {
        this.loading.set(false);
        if (result) {
          this.showPriorityDialog.set(false);
          this.messageService.add({
            severity: 'success',
            summary: 'Success',
            detail: 'Priority date updated',
          });
          this.loadData();
        }
      });
  }

  markDone(item: ServiceLocationDto): void {
    this.loading.set(true);
    this.api
      .markDone(item.toolId)
      .pipe(
        catchError((err) => {
          this.loading.set(false);
          this.messageService.add({
            severity: 'error',
            summary: 'Error',
            detail: err.detail || err.message || 'Failed to mark as done',
          });
          return of(null);
        })
      )
      .subscribe((result) => {
        this.loading.set(false);
        if (result) {
          this.messageService.add({
            severity: 'success',
            summary: 'Success',
            detail: 'Service location marked as done',
          });
          this.loadData();
        }
      });
  }

  markOpen(item: ServiceLocationDto): void {
    this.loading.set(true);
    this.api
      .markOpen(item.toolId)
      .pipe(
        catchError((err) => {
          this.loading.set(false);
          this.messageService.add({
            severity: 'error',
            summary: 'Error',
            detail: err.detail || err.message || 'Failed to mark as open',
          });
          return of(null);
        })
      )
      .subscribe((result) => {
        this.loading.set(false);
        if (result) {
          this.messageService.add({
            severity: 'success',
            summary: 'Success',
            detail: 'Service location marked as open',
          });
          this.loadData();
        }
      });
  }

  onStatusChange(item: ServiceLocationDto, newStatus: 'Open' | 'Done' | 'Cancelled' | 'Planned' | 'NotVisited'): void {
    if (!this.canEdit()) {
      return;
    }
    if (item.status === newStatus) return;

    const originalStatus = item.status;
    // Optimistic update
    item.status = newStatus;

    if (newStatus === 'NotVisited') {
      // NotVisited is driven by driver updates; prevent manual set from grid
      item.status = originalStatus;
      this.messageService.add({
        severity: 'warn',
        summary: 'Not allowed',
        detail: 'Not visited is set by drivers only.',
      });
      return;
    }

    let remark: string | undefined;
    if (newStatus === 'Cancelled') {
      remark = window.prompt('Enter a remark for cancellation') || '';
      if (!remark.trim()) {
        item.status = originalStatus;
        return;
      }
    }

    // Use dedicated endpoints for status changes
    const apiCall = newStatus === 'Done'
      ? this.api.markDone(item.toolId)
      : newStatus === 'Open'
        ? this.api.markOpen(item.toolId)
        : newStatus === 'Planned'
          ? this.api.markPlanned(item.toolId)
          : this.api.markCancelled(item.toolId, remark!);

    apiCall
      .pipe(
        catchError((err) => {
          item.status = originalStatus; // Revert on error
          this.messageService.add({
            severity: 'error',
            summary: 'Error',
            detail: err.detail || err.message || 'Failed to update status',
          });
          return of(null);
        })
      )
      .subscribe((result: ServiceLocationDto | null) => {
        if (result) {
          this.messageService.add({
            severity: 'success',
            summary: 'Success',
            detail: 'Status updated',
          });
          this.loadData();
        }
      });
  }

  onServiceMinutesChange(item: ServiceLocationDto, newMinutes: number): void {
    if (!this.canEdit()) {
      return;
    }
    const previousMinutes = this.savedServiceMinutes.get(item.toolId);
    if (previousMinutes !== undefined && previousMinutes === newMinutes) {
      return;
    }
    if (newMinutes < 1 || newMinutes > 240) {
      return;
    }

    const originalMinutes = previousMinutes ?? item.serviceMinutes;
    // Optimistic update
    item.serviceMinutes = newMinutes;

    const updateRequest: UpdateServiceLocationRequest = {
      erpId: item.erpId,
      name: item.name,
      address: item.address,
      latitude: item.latitude,
      longitude: item.longitude,
      dueDate: item.dueDate,
      priorityDate: item.priorityDate,
      serviceMinutes: newMinutes,
      serviceTypeId: item.serviceTypeId,
      ownerId: item.ownerId,
    };

    this.api
      .update(item.toolId, updateRequest)
      .pipe(
        catchError((err) => {
          item.serviceMinutes = originalMinutes; // Revert on error
          this.messageService.add({
            severity: 'error',
            summary: 'Error',
            detail: err.detail || err.message || 'Failed to update service minutes',
          });
          return of(null);
        })
      )
      .subscribe((result) => {
        if (result) {
          this.savedServiceMinutes.set(item.toolId, newMinutes);
          this.messageService.add({
            severity: 'success',
            summary: 'Success',
            detail: 'Service minutes updated',
            life: 2000,
          });
          // Optionally refresh to ensure consistency
          //           this.loadData();
        }
      });
  }

  onDriverInstructionChange(item: ServiceLocationDto, newInstruction: string | undefined): void {
    const trimmed = newInstruction?.trim() || undefined;
    if (item.driverInstruction === trimmed) {
      return;
    }

    const original = item.driverInstruction;
    item.driverInstruction = trimmed;

    const updateRequest: UpdateServiceLocationRequest = {
      erpId: item.erpId,
      name: item.name,
      address: item.address,
      latitude: item.latitude,
      longitude: item.longitude,
      dueDate: item.dueDate,
      priorityDate: item.priorityDate,
      serviceMinutes: item.serviceMinutes,
      serviceTypeId: item.serviceTypeId,
      ownerId: item.ownerId,
      driverInstruction: trimmed,
    };

    this.api.update(item.toolId, updateRequest).pipe(
      catchError((err) => {
        item.driverInstruction = original;
        this.messageService.add({
          severity: 'error',
          summary: 'Error',
          detail: err.detail || err.message || 'Failed to update driver instruction',
        });
        return of(null);
      })
    ).subscribe();
  }

  getDayLabel(dayOfWeek: number): string {
    return this.weekDayLabels[dayOfWeek] ?? `Day ${dayOfWeek}`;
  }

  formatOpeningHoursRow(row: ServiceLocationOpeningHoursDto): string {
    if (row.isClosed) {
      return 'Closed';
    }
    const first = row.openTime && row.closeTime ? `${row.openTime}-${row.closeTime}` : '—';
    if (row.openTime2 && row.closeTime2) {
      return `${first}, ${row.openTime2}-${row.closeTime2}`;
    }
    return first;
  }

  formatExceptionRow(ex: ServiceLocationExceptionDto): string {
    if (ex.isClosed) {
      return 'Closed';
    }
    if (ex.openTime && ex.closeTime) {
      return `${ex.openTime}-${ex.closeTime}`;
    }
    return '—';
  }

  formatLatLon(value: number | null | undefined): string {
    return typeof value === 'number' ? value.toFixed(6) : '—';
  }

  getMapsLink(item: ServiceLocationDto): string | null {
    const hasLat = typeof item.latitude === 'number';
    const hasLon = typeof item.longitude === 'number';
    if (hasLat && hasLon) {
      return `https://www.google.com/maps?q=${item.latitude},${item.longitude}`;
    }
    const address = item.address?.trim();
    if (address) {
      return `https://www.google.com/maps/search/?api=1&query=${encodeURIComponent(address)}`;
    }
    return null;
  }

  getOrderDateAsDate(item: ServiceLocationDto): Date {
    const dateStr = item.priorityDate || item.dueDate;
    const cacheKey = `${item.toolId}_${dateStr}`;
    
    // Return cached date if available to prevent creating new objects on every change detection
    if (this.dateCache.has(cacheKey)) {
      return this.dateCache.get(cacheKey)!;
    }
    
    // Create and cache new date
    const date = new Date(dateStr);
    date.setHours(0, 0, 0, 0);
    this.dateCache.set(cacheKey, date);
    return date;
  }

  onOrderDateChange(item: ServiceLocationDto, newDate: Date | null): void {
    if (!this.canEdit()) {
      return;
    }
    // Prevent infinite loops
    if (this.isUpdatingDate) return;
    if (!newDate) return;

    const newDateStr = toYmd(newDate);
    const currentOrderDate = item.priorityDate || item.dueDate;
    
    // Prevent unnecessary updates if date hasn't actually changed
    if (newDateStr === currentOrderDate) {
      return;
    }
    
    // Clear cache for this item since date is changing
    const oldCacheKey = `${item.toolId}_${currentOrderDate}`;
    this.dateCache.delete(oldCacheKey);

    const dueDate = new Date(item.dueDate);
    const newDateObj = new Date(newDateStr);

    // If new date is before due date, set it as priority date
    // If new date is same or after due date, clear priority date
    let priorityDateStr: string | undefined;
    
    if (newDateObj < dueDate) {
      // New date is before due date -> set as priority date
      priorityDateStr = newDateStr;
    } else {
      // New date is same or after due date -> clear priority date
      priorityDateStr = undefined;
    }

    // Prevent update if priority date hasn't actually changed
    if (priorityDateStr === item.priorityDate) {
      return;
    }

    this.isUpdatingDate = true;

    // Optimistic update
    const originalPriorityDate = item.priorityDate;
    item.priorityDate = priorityDateStr;

    this.api
      .setPriorityDate(item.toolId, priorityDateStr ?? null)
      .pipe(
        catchError((err) => {
          item.priorityDate = originalPriorityDate; // Revert on error
          this.isUpdatingDate = false;
          this.messageService.add({
            severity: 'error',
            summary: 'Error',
            detail: err.detail || err.message || 'Failed to update date',
          });
          return of(null);
        })
      )
      .subscribe((result: ServiceLocationDto | null) => {
        this.isUpdatingDate = false;
        if (result) {
          // Clear cache for updated item
          const newDateStr = result.priorityDate || result.dueDate;
          const newCacheKey = `${item.toolId}_${newDateStr}`;
          this.dateCache.delete(newCacheKey);
          
          this.messageService.add({
            severity: 'success',
            summary: 'Success',
            detail: priorityDateStr ? 'Priority date updated' : 'Priority date cleared',
            life: 2000,
          });
          // Update the item in place instead of reloading all data
          const index = this.items().findIndex(i => i.toolId === item.toolId);
          if (index >= 0) {
            const updatedItems = [...this.items()];
            updatedItems[index] = result;
            this.items.set(updatedItems);
          }
        }
      });
  }

  downloadTemplate(): void {
    // Check if owners are loaded
    if (this.owners().length === 0) {
      this.messageService.add({
        severity: 'warn',
        summary: 'Loading',
        detail: 'Owners are still loading. Please wait a moment and try again.',
      });
      return;
    }

    const serviceTypeId = this.selectedServiceTypeId();
    const ownerId = this.selectedOwnerId();
    
    if (!serviceTypeId || serviceTypeId <= 0) {
      this.messageService.add({
        severity: 'warn',
        summary: 'Validation',
        detail: 'Please select a service type first',
      });
      return;
    }
    
    if (!ownerId || ownerId <= 0) {
      this.messageService.add({
        severity: 'warn',
        summary: 'Validation',
        detail: 'Please select an owner first. If no owner is visible, owners may still be loading.',
      });
      return;
    }

    this.loading.set(true);
    this.api
      .downloadTemplate(serviceTypeId, ownerId)
      .pipe(
        catchError((err) => {
          this.loading.set(false);
          this.messageService.add({
            severity: 'error',
            summary: 'Error',
            detail: err.detail || err.message || 'Failed to download template',
          });
          return of(null);
        })
      )
      .subscribe((blob) => {
        this.loading.set(false);
        if (blob) {
          const url = window.URL.createObjectURL(blob);
          const link = document.createElement('a');
          link.href = url;
          link.download = `ServiceLocations_Template_${new Date().toISOString().split('T')[0]}.xlsx`;
          document.body.appendChild(link);
          link.click();
          document.body.removeChild(link);
          window.URL.revokeObjectURL(url);
          this.messageService.add({
            severity: 'success',
            summary: 'Success',
            detail: 'Template downloaded',
            life: 2000,
          });
        }
      });
  }

  onFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    if (input.files && input.files.length > 0) {
      const file = input.files[0];
      this.uploadExcel(file);
      input.value = '';
    }
  }

  uploadExcel(file: File): void {
    // Check if owners are loaded
    if (this.owners().length === 0) {
      this.messageService.add({
        severity: 'warn',
        summary: 'Loading',
        detail: 'Owners are still loading. Please wait a moment and try again.',
      });
      return;
    }

    const serviceTypeId = this.selectedServiceTypeId();
    const ownerId = this.selectedOwnerId();
    
    if (!serviceTypeId || serviceTypeId <= 0) {
      this.messageService.add({
        severity: 'warn',
        summary: 'Validation',
        detail: 'Please select a service type first',
      });
      return;
    }
    
    if (!ownerId || ownerId <= 0) {
      this.messageService.add({
        severity: 'warn',
        summary: 'Validation',
        detail: 'Please select an owner first. If no owner is visible, owners may still be loading.',
      });
      return;
    }

    this.loading.set(true);
    this.api
      .uploadExcel(file, serviceTypeId, ownerId)
      .pipe(
        catchError((err: HttpErrorResponse) => {
          this.loading.set(false);
          if (err.error instanceof Blob) {
            err.error.text().then((text) => {
              try {
                const details = JSON.parse(text);
                this.messageService.add({
                  severity: 'error',
                  summary: 'Error',
                  detail: details?.detail || details?.message || 'Failed to upload Excel file',
                });
              } catch {
                this.messageService.add({
                  severity: 'error',
                  summary: 'Error',
                  detail: 'Failed to upload Excel file',
                });
              }
            });
            return of(null);
          }

          this.messageService.add({
            severity: 'error',
            summary: 'Error',
            detail: err.error?.detail || err.message || 'Failed to upload Excel file',
          });
          return of(null);
        })
      )
      .subscribe((response: HttpResponse<Blob> | null) => {
        this.loading.set(false);
        if (!response || !response.body) {
          return;
        }

        const contentType = response.headers.get('content-type') ?? '';
        if (!contentType.includes('application/json')) {
          const filename = this.getDownloadFilename(
            response.headers.get('content-disposition'),
            `ServiceLocations_Errors_${new Date().toISOString().split('T')[0]}.xlsx`
          );
          this.downloadBlob(response.body, filename);
          this.messageService.add({
            severity: 'warn',
            summary: 'Upload completed with errors',
            detail: 'An error file was downloaded. Fix the rows and re-upload.',
            life: 5000,
          });
          return;
        }

        response.body.text().then((text) => {
          try {
            const result = JSON.parse(text) as BulkInsertResultDto;
            this.bulkResult.set(result);
            this.showBulkResultDialog.set(true);

            if (result.inserted > 0 || result.updated > 0) {
              this.loadData();
            }

            this.messageService.add({
              severity: result.errors.length > 0 ? 'warn' : 'success',
              summary: 'Upload Complete',
              detail: `Inserted: ${result.inserted}, Updated: ${result.updated}${
                result.errors.length > 0 ? `, Errors: ${result.errors.length}` : ''
              }`,
              life: 5000,
            });
          } catch {
            this.messageService.add({
              severity: 'error',
              summary: 'Error',
              detail: 'Failed to parse upload response',
            });
          }
        });
      });
  }

  private downloadBlob(blob: Blob, filename: string): void {
    const url = window.URL.createObjectURL(blob);
    const link = document.createElement('a');
    link.href = url;
    link.download = filename;
    document.body.appendChild(link);
    link.click();
    document.body.removeChild(link);
    window.URL.revokeObjectURL(url);
  }

  private getDownloadFilename(contentDisposition: string | null, fallback: string): string {
    if (!contentDisposition) {
      return fallback;
    }

    const filenameMatch = /filename\*?=(?:UTF-8''|")?([^\";]+)/i.exec(contentDisposition);
    if (!filenameMatch || !filenameMatch[1]) {
      return fallback;
    }

    const raw = filenameMatch[1].trim().replace(/\"/g, '');
    try {
      return decodeURIComponent(raw);
    } catch {
      return raw;
    }
  }

  get showBulkResultDialogValue(): boolean {
    return this.showBulkResultDialog();
  }
  set showBulkResultDialogValue(value: boolean) {
    this.showBulkResultDialog.set(value);
  }

  formatDueDateAsDate(dueDate: string): string {
    if (!dueDate) return '—';
    try {
      const date = new Date(dueDate);
      // Format as date only (yyyy-MM-dd)
      const year = date.getFullYear();
      const month = String(date.getMonth() + 1).padStart(2, '0');
      const day = String(date.getDate()).padStart(2, '0');
      return `${year}-${month}-${day}`;
    } catch {
      // If parsing fails, try to extract just the date part from the string
      return dueDate.split('T')[0] || dueDate;
    }
  }
}

