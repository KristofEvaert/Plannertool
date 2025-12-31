import { Component, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { InputNumberModule } from 'primeng/inputnumber';
import { InputTextModule } from 'primeng/inputtext';
import { ButtonModule } from 'primeng/button';
import { ToastModule } from 'primeng/toast';
import { MessageService } from 'primeng/api';
import { SystemCostSettingsApiService } from '@services/system-cost-settings-api.service';
import type { SystemCostSettingsDto } from '@models/system-cost-settings.model';

@Component({
  selector: 'app-system-cost-settings',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    InputNumberModule,
    InputTextModule,
    ButtonModule,
    ToastModule,
  ],
  providers: [MessageService],
  templateUrl: './system-cost-settings.page.html',
})
export class SystemCostSettingsPage {
  private readonly api = inject(SystemCostSettingsApiService);
  private readonly messageService = inject(MessageService);

  loading = signal(false);
  form = signal<SystemCostSettingsDto>({
    fuelCostPerKm: 0,
    personnelCostPerHour: 0,
    currencyCode: 'EUR',
  });

  constructor() {
    this.load();
  }

  onFormChange<K extends keyof SystemCostSettingsDto>(key: K, value: SystemCostSettingsDto[K]): void {
    this.form.update((f) => ({ ...f, [key]: value }));
  }

  load(): void {
    this.loading.set(true);
    this.api.get().subscribe({
      next: (settings) => {
        this.loading.set(false);
        this.form.set(settings);
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

    this.loading.set(true);
    this.api.update({
      fuelCostPerKm: form.fuelCostPerKm,
      personnelCostPerHour: form.personnelCostPerHour,
      currencyCode: form.currencyCode.trim().toUpperCase(),
    }).subscribe({
      next: (settings) => {
        this.loading.set(false);
        this.form.set(settings);
        this.messageService.add({ severity: 'success', summary: 'Settings updated' });
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
}
