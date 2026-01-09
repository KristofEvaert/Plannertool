import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { HelpManualComponent } from '@components';
import {
  ServiceLocationOwnerDto,
  ServiceLocationOwnersApiService,
  UpsertServiceLocationOwnerRequest,
} from '@services/service-location-owners-api.service';
import { MessageService } from 'primeng/api';
import { ButtonModule } from 'primeng/button';
import { CheckboxModule } from 'primeng/checkbox';
import { DialogModule } from 'primeng/dialog';
import { InputTextModule } from 'primeng/inputtext';
import { TableModule } from 'primeng/table';
import { ToastModule } from 'primeng/toast';

@Component({
  selector: 'app-owners-page',
  imports: [
    FormsModule,
    TableModule,
    ButtonModule,
    DialogModule,
    InputTextModule,
    CheckboxModule,
    ToastModule,
    HelpManualComponent,
  ],
  providers: [MessageService],
  templateUrl: './owners.page.html',
})
export class OwnersPage {
  private readonly ownersApi = inject(ServiceLocationOwnersApiService);
  private readonly messageService = inject(MessageService);

  owners = signal<ServiceLocationOwnerDto[]>([]);
  loading = signal(false);
  includeInactive = signal(false);

  showDialog = signal(false);
  isEdit = signal(false);
  currentOwnerId: number | null = null;
  form = signal<UpsertServiceLocationOwnerRequest>({
    code: '',
    name: '',
    isActive: true,
  });

  // Helpers for PrimeNG two-way bindings
  get showDialogValue(): boolean {
    return this.showDialog();
  }
  set showDialogValue(value: boolean) {
    this.showDialog.set(value);
  }

  constructor() {
    this.loadOwners();
  }

  onFormChange<K extends keyof UpsertServiceLocationOwnerRequest>(
    key: K,
    value: UpsertServiceLocationOwnerRequest[K],
  ) {
    this.form.update((f) => ({ ...f, [key]: value }));
  }

  loadOwners(): void {
    this.loading.set(true);
    this.ownersApi.getAll(this.includeInactive()).subscribe({
      next: (owners) => {
        this.loading.set(false);
        this.owners.set(owners);
      },
      error: (err) => {
        this.loading.set(false);
        this.messageService.add({
          severity: 'error',
          summary: 'Error',
          detail: err?.error?.message || err.message || 'Failed to load owners',
        });
      },
    });
  }

  openCreate(): void {
    this.isEdit.set(false);
    this.currentOwnerId = null;
    this.form.set({
      code: '',
      name: '',
      isActive: true,
    });
    this.showDialog.set(true);
  }

  openEdit(owner: ServiceLocationOwnerDto): void {
    this.isEdit.set(true);
    this.currentOwnerId = owner.id;
    this.form.set({
      code: owner.code,
      name: owner.name,
      isActive: owner.isActive,
    });
    this.showDialog.set(true);
  }

  save(): void {
    const form = this.form();
    if (!form.code.trim() || !form.name.trim()) {
      this.messageService.add({
        severity: 'warn',
        summary: 'Validation',
        detail: 'Code and Name are required',
      });
      return;
    }
    this.loading.set(true);
    if (this.isEdit() && this.currentOwnerId != null) {
      this.ownersApi.update(this.currentOwnerId, form).subscribe({
        next: () => {
          this.loading.set(false);
          this.showDialog.set(false);
          this.messageService.add({ severity: 'success', summary: 'Owner updated' });
          this.loadOwners();
        },
        error: (err) => {
          this.loading.set(false);
          this.messageService.add({
            severity: 'error',
            summary: 'Error',
            detail: err?.error?.message || err.message || 'Failed to update owner',
          });
        },
      });
    } else {
      this.ownersApi.create(form).subscribe({
        next: () => {
          this.loading.set(false);
          this.showDialog.set(false);
          this.messageService.add({ severity: 'success', summary: 'Owner created' });
          this.loadOwners();
        },
        error: (err) => {
          this.loading.set(false);
          this.messageService.add({
            severity: 'error',
            summary: 'Error',
            detail: err?.error?.message || err.message || 'Failed to create owner',
          });
        },
      });
    }
  }

  delete(owner: ServiceLocationOwnerDto): void {
    if (!confirm(`Deactivate owner "${owner.name}"?`)) return;
    this.loading.set(true);
    this.ownersApi.delete(owner.id).subscribe({
      next: () => {
        this.loading.set(false);
        this.messageService.add({ severity: 'success', summary: 'Owner deactivated' });
        this.loadOwners();
      },
      error: (err) => {
        this.loading.set(false);
        this.messageService.add({
          severity: 'error',
          summary: 'Error',
          detail: err?.error?.message || err.message || 'Failed to delete owner',
        });
      },
    });
  }
}
