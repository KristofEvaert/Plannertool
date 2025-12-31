import { Component, computed, effect, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { TableModule } from 'primeng/table';
import { ButtonModule } from 'primeng/button';
import { DialogModule } from 'primeng/dialog';
import { InputTextModule } from 'primeng/inputtext';
import { InputNumberModule } from 'primeng/inputnumber';
import { MultiSelectModule } from 'primeng/multiselect';
import { SelectModule } from 'primeng/select';
import { CheckboxModule } from 'primeng/checkbox';
import { ToastModule } from 'primeng/toast';
import { MessageService } from 'primeng/api';
import { TagModule } from 'primeng/tag';
import { WeightTemplatesApiService } from '@services/weight-templates-api.service';
import { LocationGroupsApiService } from '@services/location-groups-api.service';
import { ServiceLocationsApiService } from '@services/service-locations-api.service';
import { ServiceLocationOwnersApiService } from '@services/service-location-owners-api.service';
import { ServiceTypesApiService } from '@services/service-types-api.service';
import { AuthService } from '@services/auth.service';
import { HelpManualComponent } from '@components/help-manual/help-manual.component';
import type { WeightTemplateDto, SaveWeightTemplateRequest } from '@models/weight-template.model';
import type { LocationGroupDto } from '@models/location-group.model';
import type { ServiceLocationDto } from '@models/service-location.model';
import type { ServiceTypeDto } from '@models/service-type.model';
import type { ServiceLocationOwnerDto } from '@services/service-location-owners-api.service';

type Option = { label: string; value: number };

@Component({
  selector: 'app-weight-templates',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    TableModule,
    ButtonModule,
    DialogModule,
    InputTextModule,
    InputNumberModule,
    MultiSelectModule,
    SelectModule,
    CheckboxModule,
    TagModule,
    ToastModule,
    HelpManualComponent,
  ],
  providers: [MessageService],
  templateUrl: './weight-templates.page.html',
})
export class WeightTemplatesPage {
  private readonly api = inject(WeightTemplatesApiService);
  private readonly locationGroupsApi = inject(LocationGroupsApiService);
  private readonly locationsApi = inject(ServiceLocationsApiService);
  private readonly ownersApi = inject(ServiceLocationOwnersApiService);
  private readonly serviceTypesApi = inject(ServiceTypesApiService);
  private readonly auth = inject(AuthService);
  private readonly messageService = inject(MessageService);

  templates = signal<WeightTemplateDto[]>([]);
  loading = signal(false);

  ownerOptions = signal<Option[]>([]);
  serviceTypeOptions = signal<Option[]>([]);
  locationOptions = signal<Option[]>([]);
  locationGroupOptions = signal<Option[]>([]);

  selectedOwnerId = signal<number | null>(null);
  selectedServiceTypeId = signal<number | null>(null);

  showDialog = signal(false);
  isEdit = signal(false);
  currentId: number | null = null;

  form = signal<SaveWeightTemplateRequest>({
    name: '',
    scopeType: 'Global',
    ownerId: null,
    serviceTypeId: null,
    isActive: true,
    weightDistance: 10,
    weightTravelTime: 10,
    weightOvertime: 10,
    weightCost: 10,
    weightDate: 10,
    serviceLocationIds: [],
    locationGroupIds: [],
  });

  scopeOptions = [
    { label: 'Global', value: 'Global' },
    { label: 'Owner', value: 'Owner' },
    { label: 'Service type', value: 'ServiceType' },
    { label: 'Location', value: 'Location' },
    { label: 'Location group', value: 'LocationGroup' },
  ];

  isSuperAdmin = computed(() => this.auth.currentUser()?.roles.includes('SuperAdmin') ?? false);

  get showDialogValue(): boolean {
    return this.showDialog();
  }
  set showDialogValue(value: boolean) {
    this.showDialog.set(value);
  }

  constructor() {
    this.loadOwners();
    this.loadServiceTypes();

    effect(() => {
      const ownerId = this.selectedOwnerId();
      this.loadTemplates(ownerId, this.selectedServiceTypeId());
      if (ownerId) {
        this.loadServiceLocations(ownerId);
        this.loadLocationGroups(ownerId);
      } else {
        this.locationOptions.set([]);
        this.locationGroupOptions.set([]);
      }
    });
  }

  onFormChange<K extends keyof SaveWeightTemplateRequest>(key: K, value: SaveWeightTemplateRequest[K]): void {
    this.form.update((f) => ({ ...f, [key]: value }));
  }

  scopeTypeMatches(value: string): boolean {
    return this.form().scopeType === value;
  }

  ownerName(ownerId?: number | null): string {
    if (!ownerId) return 'Global';
    const match = this.ownerOptions().find((o) => o.value === ownerId);
    return match?.label ?? `Owner ${ownerId}`;
  }

  serviceTypeName(serviceTypeId?: number | null): string {
    if (!serviceTypeId) return '-';
    const match = this.serviceTypeOptions().find((t) => t.value === serviceTypeId);
    return match?.label ?? `ServiceType ${serviceTypeId}`;
  }

  private loadOwners(): void {
    const user = this.auth.currentUser();
    const isSuperAdmin = user?.roles.includes('SuperAdmin') ?? false;

    if (!isSuperAdmin) {
      if (user?.ownerId) {
        this.ownerOptions.set([{ label: 'My Owner', value: user.ownerId }]);
        this.selectedOwnerId.set(user.ownerId);
      }
      return;
    }

    this.ownersApi.getAll(true).subscribe({
      next: (owners: ServiceLocationOwnerDto[]) => {
        const opts = owners.map((o) => ({ label: o.name, value: o.id }));
        this.ownerOptions.set(opts);
        if (!this.selectedOwnerId() && opts.length > 0) {
          this.selectedOwnerId.set(opts[0].value);
        }
      },
      error: (err) => {
        this.messageService.add({
          severity: 'error',
          summary: 'Error',
          detail: err?.error?.message || err.message || 'Failed to load owners',
        });
      },
    });
  }

  private loadServiceTypes(): void {
    this.serviceTypesApi.getAll(true).subscribe({
      next: (types: ServiceTypeDto[]) => {
        const opts = types.map((t) => ({ label: t.name, value: t.id }));
        this.serviceTypeOptions.set(opts);
      },
      error: (err) => {
        this.messageService.add({
          severity: 'error',
          summary: 'Error',
          detail: err?.error?.message || err.message || 'Failed to load service types',
        });
      },
    });
  }

  private loadTemplates(ownerId: number | null, serviceTypeId: number | null): void {
    this.loading.set(true);
    this.api.getAll(ownerId ?? undefined, serviceTypeId ?? undefined, true).subscribe({
      next: (items) => {
        this.loading.set(false);
        this.templates.set(items);
      },
      error: (err) => {
        this.loading.set(false);
        this.messageService.add({
          severity: 'error',
          summary: 'Error',
          detail: err?.error?.message || err.message || 'Failed to load weight templates',
        });
      },
    });
  }

  private loadServiceLocations(ownerId: number): void {
    this.locationsApi.getList({ ownerId, page: 1, pageSize: 200, order: 'priorityThenDue' }).subscribe({
      next: (result) => {
        const options = result.items.map((loc: ServiceLocationDto) => ({
          label: loc.address ? `${loc.name} - ${loc.address}` : loc.name,
          value: loc.id,
        }));
        this.locationOptions.set(options);
      },
      error: () => {
        this.locationOptions.set([]);
      },
    });
  }

  private loadLocationGroups(ownerId: number): void {
    this.locationGroupsApi.getAll(ownerId).subscribe({
      next: (groups: LocationGroupDto[]) => {
        const options = groups.map((g) => ({ label: g.name, value: g.id }));
        this.locationGroupOptions.set(options);
      },
      error: () => {
        this.locationGroupOptions.set([]);
      },
    });
  }

  openCreate(): void {
    this.isEdit.set(false);
    this.currentId = null;
    this.form.set({
      name: '',
      scopeType: 'Global',
      ownerId: this.selectedOwnerId(),
      serviceTypeId: null,
      isActive: true,
      weightDistance: 10,
      weightTravelTime: 10,
      weightOvertime: 10,
      weightCost: 10,
      weightDate: 10,
      serviceLocationIds: [],
      locationGroupIds: [],
    });
    this.showDialog.set(true);
  }

  openEdit(template: WeightTemplateDto): void {
    this.isEdit.set(true);
    this.currentId = template.id;
    this.form.set({
      name: template.name,
      scopeType: template.scopeType,
      ownerId: template.ownerId ?? this.selectedOwnerId(),
      serviceTypeId: template.serviceTypeId ?? null,
      isActive: template.isActive,
      weightDistance: template.weightDistance,
      weightTravelTime: template.weightTravelTime,
      weightOvertime: template.weightOvertime,
      weightCost: template.weightCost,
      weightDate: template.weightDate,
      serviceLocationIds: template.serviceLocationIds ?? [],
      locationGroupIds: template.locationGroupIds ?? [],
    });
    this.showDialog.set(true);
  }

  save(): void {
    const form = this.form();
    if (!form.name.trim()) {
      this.messageService.add({
        severity: 'warn',
        summary: 'Validation',
        detail: 'Name is required.',
      });
      return;
    }

    if (form.scopeType === 'Owner' && !form.ownerId) {
      this.messageService.add({
        severity: 'warn',
        summary: 'Validation',
        detail: 'Owner is required for Owner scope.',
      });
      return;
    }

    if (form.scopeType === 'ServiceType' && !form.serviceTypeId) {
      this.messageService.add({
        severity: 'warn',
        summary: 'Validation',
        detail: 'Service type is required for ServiceType scope.',
      });
      return;
    }

    if (form.scopeType === 'Location' && (!form.serviceLocationIds || form.serviceLocationIds.length === 0)) {
      this.messageService.add({
        severity: 'warn',
        summary: 'Validation',
        detail: 'Select at least one location for Location scope.',
      });
      return;
    }

    if (form.scopeType === 'LocationGroup' && (!form.locationGroupIds || form.locationGroupIds.length === 0)) {
      this.messageService.add({
        severity: 'warn',
        summary: 'Validation',
        detail: 'Select at least one location group for LocationGroup scope.',
      });
      return;
    }

    this.loading.set(true);

    if (this.isEdit() && this.currentId != null) {
      this.api.update(this.currentId, form).subscribe({
        next: () => {
          this.loading.set(false);
          this.showDialog.set(false);
          this.messageService.add({ severity: 'success', summary: 'Template updated' });
          this.loadTemplates(this.selectedOwnerId(), this.selectedServiceTypeId());
        },
        error: (err) => {
          this.loading.set(false);
          this.messageService.add({
            severity: 'error',
            summary: 'Error',
            detail: err?.error?.message || err.message || 'Failed to update template',
          });
        },
      });
      return;
    }

    this.api.create(form).subscribe({
      next: () => {
        this.loading.set(false);
        this.showDialog.set(false);
        this.messageService.add({ severity: 'success', summary: 'Template created' });
        this.loadTemplates(this.selectedOwnerId(), this.selectedServiceTypeId());
      },
      error: (err) => {
        this.loading.set(false);
        this.messageService.add({
          severity: 'error',
          summary: 'Error',
          detail: err?.error?.message || err.message || 'Failed to create template',
        });
      },
    });
  }

  delete(template: WeightTemplateDto): void {
    if (!confirm(`Delete template "${template.name}"?`)) return;
    this.loading.set(true);
    this.api.delete(template.id).subscribe({
      next: () => {
        this.loading.set(false);
        this.messageService.add({ severity: 'success', summary: 'Template deleted' });
        this.loadTemplates(this.selectedOwnerId(), this.selectedServiceTypeId());
      },
      error: (err) => {
        this.loading.set(false);
        this.messageService.add({
          severity: 'error',
          summary: 'Error',
          detail: err?.error?.message || err.message || 'Failed to delete template',
        });
      },
    });
  }
}
