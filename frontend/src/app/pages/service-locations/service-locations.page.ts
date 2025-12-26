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
import { MessageService } from 'primeng/api';
import { ServiceLocationsApiService } from '@services/service-locations-api.service';
import { ServiceTypesApiService } from '@services/service-types-api.service';
import { ServiceLocationOwnersApiService, type ServiceLocationOwnerDto } from '@services/service-location-owners-api.service';
import { AuthService } from '@services/auth.service';
import type {
  ServiceLocationDto,
  CreateServiceLocationRequest,
  UpdateServiceLocationRequest,
  ServiceLocationListParams,
  BulkInsertResultDto,
} from '@models/service-location.model';
import type { ServiceTypeDto } from '@models/service-type.model';
import { catchError, of } from 'rxjs';
import { toYmd } from '@utils/date.utils';

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
  serviceTypes = signal<ServiceTypeDto[]>([]);
  owners = signal<ServiceLocationOwnerDto[]>([]);
  selectedServiceTypeId = signal<number | null>(null); // For bulk operations
  selectedOwnerId = signal<number | null>(null); // For bulk operations
  private isUpdatingDate = false;
  // Cache dates to prevent creating new objects on every change detection
  private dateCache = new Map<string, Date>();

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
    latitude: 50.8503, // Default Brussels
    longitude: 4.3517,
    dueDate: toYmd(new Date()),
    priorityDate: undefined,
    serviceMinutes: 20,
    serviceTypeId: 0, // Will be set when service types are loaded
    ownerId: 0, // Will be set when owners are loaded
    driverInstruction: '',
  });

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
    
    // Load service types and owners
    this.loadServiceTypes();
    this.loadOwners();
    
    // Don't auto-load - user must click "Load" button
  }

  loadServiceTypes(): void {
    this.serviceTypesApi
      .getAll(false)
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
        // Set default selected service type for bulk operations (first active one)
        if (types.length > 0 && !this.selectedServiceTypeId()) {
          this.selectedServiceTypeId.set(types[0].id);
        }
        // Set default service type in form (first active one)
        if (types.length > 0) {
          const currentForm = this.form();
          this.form.set({
            ...currentForm,
            serviceTypeId: types[0].id,
          });
        }
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
        }

        if (!isSuperAdmin && currentOwnerId) {
          this.ownerId.set(currentOwnerId);
        }
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
    const defaultServiceTypeId = this.serviceTypes().length > 0 ? this.serviceTypes()[0].id : 0;
    const defaultOwnerId = this.owners().length > 0 ? this.owners()[0].id : 0;
    this.form.set({
      erpId: 0,
      name: '',
      address: '',
      latitude: 50.8503,
      longitude: 4.3517,
      dueDate: toYmd(new Date()),
      priorityDate: undefined,
      serviceMinutes: 20,
      serviceTypeId: defaultServiceTypeId,
      ownerId: defaultOwnerId,
      driverInstruction: '',
    });
    this.showDialog.set(true);
  }

  openEditDialog(item: ServiceLocationDto): void {
    this.isEditMode.set(true);
    this.selectedItem.set(item);
    this.form.set({
      erpId: item.erpId,
      name: item.name,
      address: item.address || '',
      latitude: item.latitude,
      longitude: item.longitude,
      dueDate: item.dueDate,
      priorityDate: item.priorityDate,
      serviceMinutes: item.serviceMinutes,
      serviceTypeId: item.serviceTypeId,
      ownerId: item.ownerId,
      driverInstruction: item.driverInstruction || '',
    });
    this.showDialog.set(true);
  }

  openPriorityDialog(item: ServiceLocationDto): void {
    this.selectedItemForPriority.set(item);
    this.priorityDate.set(item.priorityDate ? new Date(item.priorityDate) : null);
    this.showPriorityDialog.set(true);
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
          this.showDialog.set(false);
          this.messageService.add({
            severity: 'success',
            summary: 'Success',
            detail: isEdit ? 'Service location updated' : 'Service location created',
          });
          this.loadData();
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
    if (item.serviceMinutes === newMinutes || newMinutes < 1 || newMinutes > 240) {
      return;
    }

    const originalMinutes = item.serviceMinutes;
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
        catchError((err) => {
          this.loading.set(false);
          this.messageService.add({
            severity: 'error',
            summary: 'Error',
            detail: err.detail || err.message || 'Failed to upload Excel file',
          });
          return of(null);
        })
      )
      .subscribe((result) => {
        this.loading.set(false);
        if (result) {
          this.bulkResult.set(result);
          this.showBulkResultDialog.set(true);
          
          if (result.inserted > 0 || result.updated > 0) {
            this.loadData();
          }
          
          this.messageService.add({
            severity: result.errors.length > 0 ? 'warn' : 'success',
            summary: 'Upload Complete',
            detail: `Inserted: ${result.inserted}, Updated: ${result.updated}${result.errors.length > 0 ? `, Errors: ${result.errors.length}` : ''}`,
            life: 5000,
          });
        }
      });
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

