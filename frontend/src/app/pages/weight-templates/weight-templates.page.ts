import { Component, computed, effect, inject, signal } from '@angular/core';

import { FormsModule } from '@angular/forms';
import { HelpManualComponent } from '@components/help-manual/help-manual.component';
import type { SaveWeightTemplateRequest, WeightTemplateDto } from '@models';
import { AuthService } from '@services/auth.service';
import type { ServiceLocationOwnerDto } from '@services/service-location-owners-api.service';
import { ServiceLocationOwnersApiService } from '@services/service-location-owners-api.service';
import { WeightTemplatesApiService } from '@services/weight-templates-api.service';
import { MessageService } from 'primeng/api';
import { ButtonModule } from 'primeng/button';
import { CheckboxModule } from 'primeng/checkbox';
import { DialogModule } from 'primeng/dialog';
import { InputTextModule } from 'primeng/inputtext';
import { SelectModule } from 'primeng/select';
import { TableModule } from 'primeng/table';
import { TagModule } from 'primeng/tag';
import { ToastModule } from 'primeng/toast';

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
  private readonly auth = inject(AuthService);
  private readonly messageService = inject(MessageService);

  templates = signal<WeightTemplateDto[]>([]);
  loading = signal(false);

  ownerOptions = signal<Option[]>([]);
  algorithmOptions = [{ label: 'Lollipop', value: 'Lollipop' }];

  selectedOwnerId = signal<number | null>(null);
  includeInactive = signal(false);

  showDialog = signal(false);
  isEdit = signal(false);
  currentId: number | null = null;

  form = signal<SaveWeightTemplateRequest>({
    name: '',
    ownerId: null,
    isActive: true,
    algorithmType: 'Lollipop',
    dueDatePriority: 50,
    worktimeDeviationPercent: 10,
  });

  isSuperAdmin = computed(() => this.auth.currentUser()?.roles.includes('SuperAdmin') ?? false);
  canManageTemplates = computed(() => {
    const roles = this.auth.currentUser()?.roles ?? [];
    return roles.includes('Admin') || roles.includes('SuperAdmin');
  });
  ownerFilterOptions = computed<OwnerFilterOption[]>(() => [
    { label: 'All owners', value: null },
    ...this.ownerOptions(),
  ]);

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
      const includeInactive = this.includeInactive();
      this.loadTemplates(ownerId, includeInactive);
    });
  }

  onFormChange<K extends keyof SaveWeightTemplateRequest>(
    key: K,
    value: SaveWeightTemplateRequest[K],
  ): void {
    this.form.update((f) => ({ ...f, [key]: value }));
  }

  ownerName(ownerId?: number | null): string {
    if (!ownerId) return '-';
    const match = this.ownerOptions().find((o) => o.value === ownerId);
    return match?.label ?? `Owner ${ownerId}`;
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

  private loadTemplates(ownerId: number | null, includeInactive: boolean): void {
    this.loading.set(true);
    const effectiveOwnerId = ownerId != null && ownerId > 0 ? ownerId : null;
    this.api
      .getAll(effectiveOwnerId ?? undefined, includeInactive)
      .subscribe({
        next: (items) => {
          this.loading.set(false);
          this.templates.set(items ?? []);
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
    if (!this.canManageTemplates()) {
      return;
    }
    this.isEdit.set(false);
    this.currentId = null;
    const selectedOwnerId = this.selectedOwnerId();
    this.form.set({
      name: '',
      ownerId: selectedOwnerId != null && selectedOwnerId > 0 ? selectedOwnerId : null,
      isActive: true,
      algorithmType: 'Lollipop',
      dueDatePriority: 50,
      worktimeDeviationPercent: 10,
    });
    this.showDialog.set(true);
  }

  openEdit(template: WeightTemplateDto): void {
    if (!this.canManageTemplates()) {
      return;
    }
    this.isEdit.set(true);
    this.currentId = template.id;
    const selectedOwnerId = this.selectedOwnerId();
    this.form.set({
      name: template.name,
      ownerId:
        template.ownerId ??
        (selectedOwnerId != null && selectedOwnerId > 0 ? selectedOwnerId : null),
      isActive: template.isActive,
      algorithmType: template.algorithmType ?? 'Lollipop',
      dueDatePriority: template.dueDatePriority ?? 50,
      worktimeDeviationPercent: template.worktimeDeviationPercent ?? 10,
    });
    this.showDialog.set(true);
  }

  canEditTemplate(template: WeightTemplateDto): boolean {
    if (!this.canManageTemplates()) {
      return false;
    }
    if (this.isSuperAdmin()) {
      return true;
    }
    const ownerId = this.auth.currentUser()?.ownerId ?? null;
    return ownerId != null && template.ownerId === ownerId;
  }

  save(): void {
    if (!this.canManageTemplates()) {
      return;
    }
    const form = this.form();
    if (!form.name.trim()) {
      this.messageService.add({
        severity: 'warn',
        summary: 'Validation',
        detail: 'Name is required.',
      });
      return;
    }

    const ownerId = this.isSuperAdmin()
      ? (form.ownerId ?? null)
      : (this.auth.currentUser()?.ownerId ?? form.ownerId ?? null);
    if (!ownerId) {
      this.messageService.add({
        severity: 'warn',
        summary: 'Validation',
        detail: 'Owner is required.',
      });
      return;
    }

    this.loading.set(true);
    const normalized: SaveWeightTemplateRequest = {
      name: form.name.trim(),
      ownerId: ownerId,
      isActive: form.isActive,
      algorithmType: form.algorithmType?.trim() || 'Lollipop',
      dueDatePriority: Number(form.dueDatePriority),
      worktimeDeviationPercent: Number(form.worktimeDeviationPercent),
    };

    if (this.isEdit() && this.currentId != null) {
      this.api.update(this.currentId, normalized).subscribe({
        next: () => {
          this.loading.set(false);
          this.showDialog.set(false);
          this.messageService.add({ severity: 'success', summary: 'Template updated' });
          this.loadTemplates(this.selectedOwnerId(), this.includeInactive());
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
        this.loadTemplates(this.selectedOwnerId(), this.includeInactive());
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
    if (!this.canEditTemplate(template)) {
      return;
    }
    if (!confirm(`Delete template "${template.name}"?`)) return;
    this.loading.set(true);
    this.api.delete(template.id).subscribe({
      next: () => {
        this.loading.set(false);
        this.messageService.add({ severity: 'success', summary: 'Template deleted' });
        this.loadTemplates(this.selectedOwnerId(), this.includeInactive());
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
