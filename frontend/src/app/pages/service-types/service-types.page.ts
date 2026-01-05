
import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { HelpManualComponent } from '@components/help-manual/help-manual.component';
import type {
  CreateServiceTypeRequest,
  ServiceTypeDto,
  UpdateServiceTypeRequest,
} from '@models/service-type.model';
import { AuthService } from '@services/auth.service';
import {
  ServiceLocationOwnersApiService,
  type ServiceLocationOwnerDto,
} from '@services/service-location-owners-api.service';
import { ServiceTypesApiService } from '@services/service-types-api.service';
import { MessageService } from 'primeng/api';
import { ButtonModule } from 'primeng/button';
import { CheckboxModule } from 'primeng/checkbox';
import { DialogModule } from 'primeng/dialog';
import { InputTextModule } from 'primeng/inputtext';
import { SelectModule } from 'primeng/select';
import { TableModule } from 'primeng/table';
import { ToastModule } from 'primeng/toast';
import { catchError, of } from 'rxjs';

interface ServiceTypeForm {
  code: string;
  name: string;
  description?: string;
  ownerId: number | null;
  isActive: boolean;
}
interface OwnerFilterOption {
  label: string;
  value: number | null;
}

@Component({
  selector: 'app-service-types',
  imports: [
    FormsModule,
    ButtonModule,
    TableModule,
    InputTextModule,
    SelectModule,
    DialogModule,
    CheckboxModule,
    ToastModule,
    HelpManualComponent
],
  providers: [MessageService],
  templateUrl: './service-types.page.html',
  standalone: true,
})
export class ServiceTypesPage {
  private readonly api = inject(ServiceTypesApiService);
  private readonly ownersApi = inject(ServiceLocationOwnersApiService);
  private readonly auth = inject(AuthService);
  private readonly messageService = inject(MessageService);

  // Data
  items = signal<ServiceTypeDto[]>([]);
  loading = signal(false);
  owners = signal<ServiceLocationOwnerDto[]>([]);
  ownerFilterId = signal<number | null>(null);
  isSuperAdmin = computed(() => this.auth.currentUser()?.roles.includes('SuperAdmin') ?? false);
  ownerFilterOptions = computed<OwnerFilterOption[]>(() => [
    { label: 'All owners', value: null },
    ...this.owners().map((o) => ({ label: o.name, value: o.id })),
  ]);

  // Dialog state
  showDialog = signal(false);
  isEditMode = signal(false);
  editingId = signal<number | null>(null);

  // Computed for two-way binding
  get showDialogValue(): boolean {
    return this.showDialog();
  }
  set showDialogValue(value: boolean) {
    this.showDialog.set(value);
  }

  form = signal<ServiceTypeForm>({
    code: '',
    name: '',
    description: '',
    ownerId: null,
    isActive: true,
  });

  constructor() {
    this.loadOwners();
  }

  loadData(): void {
    const ownerId = this.isSuperAdmin()
      ? (this.ownerFilterId() ?? undefined)
      : (this.auth.currentUser()?.ownerId ?? undefined);

    this.loading.set(true);
    this.api
      .getAll(false, ownerId)
      .pipe(
        catchError((err) => {
          this.loading.set(false);
          this.messageService.add({
            severity: 'error',
            summary: 'Error',
            detail: err.detail || err.message || 'Failed to load service types',
          });
          return of([]);
        }),
      )
      .subscribe((types) => {
        this.loading.set(false);
        this.items.set(types);
      });
  }

  loadOwners(): void {
    const current = this.auth.currentUser();
    const isSuperAdmin = current?.roles.includes('SuperAdmin') ?? false;
    const currentOwnerId = current?.ownerId ?? null;

    if (!isSuperAdmin && currentOwnerId) {
      this.ownerFilterId.set(currentOwnerId);
    }

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
        }),
      )
      .subscribe((owners) => {
        const filtered =
          !isSuperAdmin && currentOwnerId ? owners.filter((o) => o.id === currentOwnerId) : owners;
        this.owners.set(filtered);
        this.loadData();
      });
  }

  openAddDialog(): void {
    const defaultOwnerId = this.ownerFilterId() ?? this.owners()[0]?.id ?? null;
    this.isEditMode.set(false);
    this.editingId.set(null);
    this.form.set({
      code: '',
      name: '',
      description: '',
      ownerId: defaultOwnerId,
      isActive: true,
    });
    this.showDialog.set(true);
  }

  openEditDialog(item: ServiceTypeDto): void {
    if (!this.isSuperAdmin()) {
      return;
    }
    this.isEditMode.set(true);
    this.editingId.set(item.id);
    this.form.set({
      code: item.code,
      name: item.name,
      description: item.description ?? '',
      ownerId: item.ownerId ?? this.ownerFilterId() ?? null,
      isActive: item.isActive,
    });
    this.showDialog.set(true);
  }

  updateForm(values: Partial<ServiceTypeForm>): void {
    this.form.update((current) => ({ ...current, ...values }));
  }

  save(): void {
    const form = this.form();

    if (!form.code.trim()) {
      this.messageService.add({
        severity: 'error',
        summary: 'Validation Error',
        detail: 'Code is required',
      });
      return;
    }

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

    // Validate code format (uppercase, alphanumeric, underscores)
    const codeRegex = /^[A-Z0-9_]+$/;
    if (!codeRegex.test(form.code)) {
      this.messageService.add({
        severity: 'error',
        summary: 'Validation Error',
        detail: 'Code must be uppercase, alphanumeric, and can contain underscores',
      });
      return;
    }

    this.loading.set(true);
    const baseRequest = {
      code: form.code.trim().toUpperCase(),
      name: form.name.trim(),
      description: form.description?.trim() || undefined,
      ownerId: form.ownerId,
    };

    const request$ =
      this.isEditMode() && this.editingId()
        ? this.api.update(this.editingId()!, {
            ...baseRequest,
            isActive: form.isActive,
          } as UpdateServiceTypeRequest)
        : this.api.create(baseRequest as CreateServiceTypeRequest);

    request$
      .pipe(
        catchError((err) => {
          this.loading.set(false);
          this.messageService.add({
            severity: 'error',
            summary: 'Error',
            detail: err.detail || err.message || 'Failed to save service type',
          });
          return of(null);
        }),
      )
      .subscribe((result) => {
        this.loading.set(false);
        if (result) {
          this.showDialog.set(false);
          this.messageService.add({
            severity: 'success',
            summary: 'Success',
            detail: this.isEditMode() ? 'Service type updated' : 'Service type created',
          });
          this.loadData();
        }
      });
  }
}
