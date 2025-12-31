import { Component, computed, effect, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { InputNumberModule } from 'primeng/inputnumber';
import { InputTextModule } from 'primeng/inputtext';
import { ButtonModule } from 'primeng/button';
import { SelectModule } from 'primeng/select';
import { ToastModule } from 'primeng/toast';
import { TableModule } from 'primeng/table';
import { MessageService } from 'primeng/api';
import { SystemCostSettingsApiService } from '@services/system-cost-settings-api.service';
import type { SystemCostSettingsDto, SystemCostSettingsOverviewDto } from '@models/system-cost-settings.model';
import { HelpManualComponent } from '@components/help-manual/help-manual.component';
import { ServiceLocationOwnersApiService, type ServiceLocationOwnerDto } from '@services/service-location-owners-api.service';
import { AuthService } from '@services/auth.service';

type OwnerOption = { label: string; value: number };

@Component({
  selector: 'app-system-cost-settings',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    InputNumberModule,
    InputTextModule,
    ButtonModule,
    SelectModule,
    ToastModule,
    TableModule,
    HelpManualComponent,
  ],
  providers: [MessageService],
  templateUrl: './system-cost-settings.page.html',
})
export class SystemCostSettingsPage {
  private readonly api = inject(SystemCostSettingsApiService);
  private readonly ownersApi = inject(ServiceLocationOwnersApiService);
  private readonly auth = inject(AuthService);
  private readonly messageService = inject(MessageService);

  loading = signal(false);
  ownerOptions = signal<OwnerOption[]>([]);
  selectedOwnerId = signal<number | null>(null);
  overview = signal<SystemCostSettingsOverviewDto[]>([]);
  overviewLoading = signal(false);
  isSuperAdmin = computed(() => this.auth.currentUser()?.roles.includes('SuperAdmin') ?? false);

  form = signal<SystemCostSettingsDto>({
    fuelCostPerKm: 0,
    personnelCostPerHour: 0,
    currencyCode: 'EUR',
  });

  constructor() {
    effect(() => {
      const user = this.auth.currentUser();
      if (!user) return;

      this.loadOwners();

      if (user.roles.includes('SuperAdmin')) {
        this.loadOverview();
      }
    });

    effect(() => {
      const user = this.auth.currentUser();
      if (!user) return;

      if (user.roles.includes('SuperAdmin')) {
        const ownerId = this.selectedOwnerId();
        if (ownerId) {
          this.load(ownerId);
        }
        return;
      }
      this.load();
    });
  }

  onFormChange<K extends keyof SystemCostSettingsDto>(key: K, value: SystemCostSettingsDto[K]): void {
    this.form.update((f) => ({ ...f, [key]: value }));
  }

  load(ownerId?: number | null): void {
    this.loading.set(true);
    this.api.get(ownerId ?? undefined).subscribe({
      next: (settings) => {
        this.loading.set(false);
        this.form.set(settings);
        if (this.isSuperAdmin()) {
          this.loadOverview();
        }
      },
      error: (err) => {
        this.loading.set(false);
        this.messageService.add({
          severity: 'error',
          summary: 'Error',
          detail: err?.error?.message || err.message || 'Failed to load settings',
        });
      },
    });
  }


  selectOwnerForEdit(ownerId: number): void {
    this.selectedOwnerId.set(ownerId);
  }

  save(): void {
    const form = this.form();
    if (!form.currencyCode?.trim()) {
      this.messageService.add({
        severity: 'warn',
        summary: 'Validation',
        detail: 'Currency code is required.',
      });
      return;
    }
    if (this.isSuperAdmin() && !this.selectedOwnerId()) {
      this.messageService.add({
        severity: 'warn',
        summary: 'Validation',
        detail: 'Owner is required.',
      });
      return;
    }

    this.loading.set(true);
    this.api.update({
      ownerId: form.ownerId,
      fuelCostPerKm: form.fuelCostPerKm,
      personnelCostPerHour: form.personnelCostPerHour,
      currencyCode: form.currencyCode.trim().toUpperCase(),
    }, this.isSuperAdmin() ? this.selectedOwnerId() : null).subscribe({
      next: (settings) => {
        this.loading.set(false);
        this.form.set(settings);
        this.messageService.add({ severity: 'success', summary: 'Settings updated' });
        if (this.isSuperAdmin()) {
          this.loadOverview();
        }
      },
      error: (err) => {
        this.loading.set(false);
        this.messageService.add({
          severity: 'error',
          summary: 'Error',
          detail: err?.error?.message || err.message || 'Failed to update settings',
        });
      },
    });
  }


  private loadOverview(): void {
    this.overviewLoading.set(true);
    this.api.getOverview(true).subscribe({
      next: (rows) => {
        this.overviewLoading.set(false);
        this.overview.set(rows);
      },
      error: (err) => {
        this.overviewLoading.set(false);
        this.messageService.add({
          severity: 'error',
          summary: 'Error',
          detail: err?.error?.message || err.message || 'Failed to load cost overview',
        });
      },
    });
  }
  private loadOwners(): void {
    const user = this.auth.currentUser();
    if (!user) return;
    const isSuperAdmin = user.roles.includes('SuperAdmin');

    if (!isSuperAdmin) {
      if (user?.ownerId) {
        this.selectedOwnerId.set(user.ownerId);
        this.ownerOptions.set([{ label: 'My Owner', value: user.ownerId }]);
      }
      return;
    }

    this.ownersApi.getAll(true).subscribe({
      next: (owners: ServiceLocationOwnerDto[]) => {
        const options = owners.map((o) => ({ label: o.name, value: o.id }));
        this.ownerOptions.set(options);
        if (!this.selectedOwnerId() && options.length > 0) {
          this.selectedOwnerId.set(options[0].value);
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
}
