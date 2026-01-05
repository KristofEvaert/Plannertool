import { Component, computed, effect, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { TableModule } from 'primeng/table';
import { ButtonModule } from 'primeng/button';
import { DialogModule } from 'primeng/dialog';
import { InputTextModule } from 'primeng/inputtext';
import { SelectModule } from 'primeng/select';
import { CheckboxModule } from 'primeng/checkbox';
import { ToastModule } from 'primeng/toast';
import { MessageService } from 'primeng/api';
import { TagModule } from 'primeng/tag';
import { WeightTemplatesApiService } from '@services/weight-templates-api.service';
import { ServiceLocationOwnersApiService } from '@services/service-location-owners-api.service';
import { ServiceTypesApiService } from '@services/service-types-api.service';
import { AuthService } from '@services/auth.service';
import { HelpManualComponent } from '@components/help-manual/help-manual.component';
import type { WeightTemplateDto, SaveWeightTemplateRequest } from '@models/weight-template.model';
import type { ServiceTypeDto } from '@models/service-type.model';
import type { ServiceLocationOwnerDto } from '@services/service-location-owners-api.service';

interface Option {
  label: string;
  value: number;
}
interface OwnerFilterOption {
  label: string;
  value: number | null;
}

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
  private readonly ownersApi = inject(ServiceLocationOwnersApiService);
  private readonly serviceTypesApi = inject(ServiceTypesApiService);
  private readonly auth = inject(AuthService);
  private readonly messageService = inject(MessageService);

  templates = signal<WeightTemplateDto[]>([]);
  loading = signal(false);

  ownerOptions = signal<Option[]>([]);
  serviceTypeOptions = signal<Option[]>([]);
  serviceTypeNameMap = signal<Map<number, string>>(new Map());

  selectedOwnerId = signal<number | null>(null);
  selectedServiceTypeId = signal<number | null>(null);

  showDialog = signal(false);
  isEdit = signal(false);
  currentId: number | null = null;

  form = signal<SaveWeightTemplateRequest>({
    name: '',
    scopeType: 'ServiceType',
    ownerId: null,
    serviceTypeId: null,
    isActive: true,
    weightDistance: 10,
    weightTravelTime: 10,
    weightOvertime: 10,
    weightCost: 10,
    weightDate: 10,
    dueCostCapPercent: 50,
    detourCostCapPercent: 50,
    detourRefKmPercent: 50,
    lateRefMinutesPercent: 50,
    serviceLocationIds: [],
  });

  isSuperAdmin = computed(() => this.auth.currentUser()?.roles.includes('SuperAdmin') ?? false);
  isOwnerFilterUnscoped = computed(() => {
    const ownerId = this.selectedOwnerId();
    return ownerId == null || ownerId < 0;
  });
  scopeOptions = computed(() =>
    this.isSuperAdmin()
      ? [
          { label: 'Global', value: 'Global' },
          { label: 'Service type', value: 'ServiceType' },
        ]
      : [{ label: 'Service type', value: 'ServiceType' }],
  );
  ownerFilterOptions = computed<OwnerFilterOption[]>(() => [
    { label: 'All owners', value: null },
    { label: 'Global templates', value: -1 },
    ...this.ownerOptions(),
  ]);
  showCostDoubleCountWarning = computed(() => {
    const form = this.form();
    return form.weightCost > 0 && form.weightDistance > 0;
  });
  showInactiveWeights = signal(false);
  activeWeightSummary = computed(() => this.buildWeightSummary().active);
  inactiveWeightSummary = computed(() => this.buildWeightSummary().inactive);
  suppressedWeightSummary = computed(() => this.buildWeightSummary().suppressed);
  hasInactiveWeights = computed(() => {
    const summary = this.buildWeightSummary();
    return summary.inactive.length > 0 || summary.suppressed.length > 0;
  });

  get showDialogValue(): boolean {
    return this.showDialog();
  }
  set showDialogValue(value: boolean) {
    this.showDialog.set(value);
  }

  constructor() {
    this.loadOwners();

    effect(() => {
      const ownerId = this.selectedOwnerId();
      this.loadTemplates(ownerId, this.selectedServiceTypeId());
    });

    effect(() => {
      const ownerId = this.selectedOwnerId();
      this.loadServiceTypes(ownerId);
    });
  }

  onFormChange<K extends keyof SaveWeightTemplateRequest>(
    key: K,
    value: SaveWeightTemplateRequest[K],
  ): void {
    this.form.update((f) => ({ ...f, [key]: value }));
    if (key === 'ownerId' && typeof value === 'number') {
      this.selectedOwnerId.set(value);
      this.loadServiceTypes(value);
      this.form.update((f) => ({ ...f, serviceTypeId: null }));
    }
    if (key === 'scopeType' && value === 'Global') {
      this.form.update((f) => ({
        ...f,
        ownerId: null,
        serviceTypeId: null,
        serviceLocationIds: [],
      }));
    }
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
    const name = this.serviceTypeNameMap().get(serviceTypeId);
    return name ?? `#${serviceTypeId}`;
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

  private loadServiceTypes(ownerId: number | null): void {
    const isUnscopedOwner = ownerId == null || ownerId < 0;
    if (this.isSuperAdmin() && isUnscopedOwner) {
      this.serviceTypeOptions.set([]);
      this.selectedServiceTypeId.set(null);
      this.form.update((f) => ({ ...f, serviceTypeId: null }));
      this.loadServiceTypeNamesForAll();
      return;
    }

    const resolvedOwnerId =
      ownerId ?? (this.isSuperAdmin() ? null : (this.auth.currentUser()?.ownerId ?? null));
    this.serviceTypesApi.getAll(true, resolvedOwnerId ?? undefined).subscribe({
      next: (types: ServiceTypeDto[]) => {
        const opts = types.map((t) => ({ label: t.name, value: t.id }));
        this.serviceTypeOptions.set(opts);
        this.setServiceTypeNames(types);

        const selectedFilter = this.selectedServiceTypeId();
        if (selectedFilter && !opts.some((o) => o.value === selectedFilter)) {
          this.selectedServiceTypeId.set(null);
        }

        const formServiceTypeId = this.form().serviceTypeId;
        if (formServiceTypeId && !opts.some((o) => o.value === formServiceTypeId)) {
          this.form.update((f) => ({ ...f, serviceTypeId: null }));
        }
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

  private loadServiceTypeNamesForAll(): void {
    this.serviceTypesApi.getAll(true).subscribe({
      next: (types: ServiceTypeDto[]) => {
        this.setServiceTypeNames(types);
      },
      error: () => {
        this.serviceTypeNameMap.set(new Map());
      },
    });
  }

  private setServiceTypeNames(types: ServiceTypeDto[]): void {
    const map = new Map<number, string>();
    for (const type of types) {
      map.set(type.id, type.name);
    }
    this.serviceTypeNameMap.set(map);
  }

  private loadTemplates(ownerId: number | null, serviceTypeId: number | null): void {
    this.loading.set(true);
    const isGlobalOnly = ownerId != null && ownerId < 0;
    const effectiveOwnerId = ownerId != null && ownerId > 0 ? ownerId : null;
    const effectiveServiceTypeId = effectiveOwnerId ? serviceTypeId : null;
    const includeGlobal = effectiveOwnerId == null && effectiveServiceTypeId == null;
    this.api
      .getAll(
        effectiveOwnerId ?? undefined,
        effectiveServiceTypeId ?? undefined,
        true,
        includeGlobal,
      )
      .subscribe({
        next: (items) => {
          this.loading.set(false);
          const filtered = isGlobalOnly ? items.filter((t) => t.scopeType === 'Global') : items;
          this.templates.set(filtered);
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

  openCreate(): void {
    this.isEdit.set(false);
    this.currentId = null;
    this.showInactiveWeights.set(false);
    const selectedOwnerId = this.selectedOwnerId();
    this.form.set({
      name: '',
      scopeType: 'ServiceType',
      ownerId: selectedOwnerId != null && selectedOwnerId > 0 ? selectedOwnerId : null,
      serviceTypeId: null,
      isActive: true,
      weightDistance: 10,
      weightTravelTime: 10,
      weightOvertime: 10,
      weightCost: 10,
      weightDate: 10,
      dueCostCapPercent: 50,
      detourCostCapPercent: 50,
      detourRefKmPercent: 50,
      lateRefMinutesPercent: 50,
      serviceLocationIds: [],
    });
    this.showDialog.set(true);
  }

  openEdit(template: WeightTemplateDto): void {
    this.isEdit.set(true);
    this.currentId = template.id;
    this.showInactiveWeights.set(false);
    const selectedOwnerId = this.selectedOwnerId();
    this.form.set({
      name: template.name,
      scopeType: template.scopeType === 'Global' ? 'Global' : 'ServiceType',
      ownerId:
        template.ownerId ??
        (selectedOwnerId != null && selectedOwnerId > 0 ? selectedOwnerId : null),
      serviceTypeId: template.serviceTypeId ?? null,
      isActive: template.isActive,
      weightDistance: template.weightDistance,
      weightTravelTime: template.weightTravelTime,
      weightOvertime: template.weightOvertime,
      weightCost: template.weightCost,
      weightDate: template.weightDate,
      dueCostCapPercent: template.dueCostCapPercent ?? 50,
      detourCostCapPercent: template.detourCostCapPercent ?? 50,
      detourRefKmPercent: template.detourRefKmPercent ?? 50,
      lateRefMinutesPercent: template.lateRefMinutesPercent ?? 50,
      serviceLocationIds: template.serviceLocationIds ?? [],
    });
    this.showDialog.set(true);
  }

  canEditTemplate(template: WeightTemplateDto): boolean {
    if (template.scopeType === 'Global') {
      return this.isSuperAdmin();
    }
    if (this.isSuperAdmin()) {
      return true;
    }
    const ownerId = this.auth.currentUser()?.ownerId ?? null;
    return ownerId != null && template.ownerId === ownerId;
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

    if (form.scopeType === 'ServiceType' && !form.serviceTypeId) {
      this.messageService.add({
        severity: 'warn',
        summary: 'Validation',
        detail: 'Service type is required for ServiceType scope.',
      });
      return;
    }

    if (form.scopeType === 'Global' && !this.isSuperAdmin()) {
      this.messageService.add({
        severity: 'warn',
        summary: 'Validation',
        detail: 'Only SuperAdmin can create global templates.',
      });
      return;
    }

    if (this.isSuperAdmin() && form.scopeType === 'ServiceType' && !form.ownerId) {
      this.messageService.add({
        severity: 'warn',
        summary: 'Validation',
        detail: 'Owner is required to create a service type template.',
      });
      return;
    }

    this.loading.set(true);
    const normalized: SaveWeightTemplateRequest = {
      ...form,
      scopeType: form.scopeType === 'Global' ? 'Global' : 'ServiceType',
      ownerId: form.scopeType === 'Global' ? null : form.ownerId,
      serviceTypeId: form.scopeType === 'Global' ? null : form.serviceTypeId,
      serviceLocationIds: [],
    };

    if (this.isEdit() && this.currentId != null) {
      this.api.update(this.currentId, normalized).subscribe({
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

    this.api.create(normalized).subscribe({
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

  private buildWeightSummary(): {
    active: string[];
    inactive: string[];
    suppressed: string[];
  } {
    const form = this.form();
    const costActive = form.weightCost > 0;
    const entries = [
      { label: 'Driver Time', value: form.weightTravelTime, suppressed: costActive },
      { label: 'Distance', value: form.weightDistance, suppressed: costActive },
      { label: 'Due Date', value: form.weightDate, suppressed: false },
      { label: 'Cost', value: form.weightCost, suppressed: false },
      { label: 'Overtime', value: form.weightOvertime, suppressed: false },
    ];

    const active: string[] = [];
    const inactive: string[] = [];
    const suppressed: string[] = [];

    for (const entry of entries) {
      const label = `${entry.label} ${entry.value}%`;
      if (entry.suppressed) {
        suppressed.push(label);
      } else if (entry.value > 0) {
        active.push(label);
      } else {
        inactive.push(label);
      }
    }

    return { active, inactive, suppressed };
  }

  delete(template: WeightTemplateDto): void {
    if (!this.canEditTemplate(template)) {
      return;
    }
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
