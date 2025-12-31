import { Component, computed, effect, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { TableModule } from 'primeng/table';
import { ButtonModule } from 'primeng/button';
import { DialogModule } from 'primeng/dialog';
import { InputTextModule } from 'primeng/inputtext';
import { InputTextarea } from 'primeng/inputtextarea';
import { MultiSelectModule } from 'primeng/multiselect';
import { SelectModule } from 'primeng/select';
import { ToastModule } from 'primeng/toast';
import { MessageService } from 'primeng/api';
import { LocationGroupsApiService } from '@services/location-groups-api.service';
import { ServiceLocationsApiService } from '@services/service-locations-api.service';
import { ServiceLocationOwnersApiService } from '@services/service-location-owners-api.service';
import { AuthService } from '@services/auth.service';
import type { LocationGroupDto, SaveLocationGroupRequest } from '@models/location-group.model';
import type { ServiceLocationDto } from '@models/service-location.model';
import type { ServiceLocationOwnerDto } from '@services/service-location-owners-api.service';

type OwnerOption = { label: string; value: number };
type LocationOption = { label: string; value: number };

@Component({
  selector: 'app-location-groups',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    TableModule,
    ButtonModule,
    DialogModule,
    InputTextModule,
    InputTextarea,
    MultiSelectModule,
    SelectModule,
    ToastModule,
  ],
  providers: [MessageService],
  templateUrl: './location-groups.page.html',
})
export class LocationGroupsPage {
  private readonly api = inject(LocationGroupsApiService);
  private readonly locationsApi = inject(ServiceLocationsApiService);
  private readonly ownersApi = inject(ServiceLocationOwnersApiService);
  private readonly auth = inject(AuthService);
  private readonly messageService = inject(MessageService);

  groups = signal<LocationGroupDto[]>([]);
  loading = signal(false);

  ownerOptions = signal<OwnerOption[]>([]);
  selectedOwnerId = signal<number | null>(null);
  locationOptions = signal<LocationOption[]>([]);

  showDialog = signal(false);
  isEdit = signal(false);
  currentId: number | null = null;
  form = signal<SaveLocationGroupRequest>({
    name: '',
    description: '',
    ownerId: null,
    serviceLocationIds: [],
  });

  isSuperAdmin = computed(() => this.auth.currentUser()?.roles.includes('SuperAdmin') ?? false);

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
      if (!ownerId) {
        this.groups.set([]);
        this.locationOptions.set([]);
        return;
      }
      this.loadGroups(ownerId);
      this.loadServiceLocations(ownerId);
    });
  }

  onFormChange<K extends keyof SaveLocationGroupRequest>(key: K, value: SaveLocationGroupRequest[K]): void {
    this.form.update((f) => ({ ...f, [key]: value }));
  }

  ownerName(ownerId?: number | null): string {
    if (!ownerId) return 'Global';
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

  private loadGroups(ownerId: number): void {
    this.loading.set(true);
    this.api.getAll(ownerId).subscribe({
      next: (groups) => {
        this.loading.set(false);
        this.groups.set(groups);
      },
      error: (err) => {
        this.loading.set(false);
        this.messageService.add({
          severity: 'error',
          summary: 'Error',
          detail: err?.error?.message || err.message || 'Failed to load location groups',
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

  openCreate(): void {
    this.isEdit.set(false);
    this.currentId = null;
    this.form.set({
      name: '',
      description: '',
      ownerId: this.selectedOwnerId(),
      serviceLocationIds: [],
    });
    this.showDialog.set(true);
  }

  openEdit(group: LocationGroupDto): void {
    this.isEdit.set(true);
    this.currentId = group.id;
    this.form.set({
      name: group.name,
      description: group.description ?? '',
      ownerId: group.ownerId ?? this.selectedOwnerId(),
      serviceLocationIds: group.serviceLocationIds ?? [],
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

    this.loading.set(true);
    const request: SaveLocationGroupRequest = {
      name: form.name.trim(),
      description: form.description?.trim() || undefined,
      ownerId: form.ownerId ?? undefined,
      serviceLocationIds: form.serviceLocationIds ?? [],
    };

    const ownerId = request.ownerId ?? this.selectedOwnerId();
    if (!ownerId) {
      this.loading.set(false);
      this.messageService.add({
        severity: 'warn',
        summary: 'Validation',
        detail: 'Owner is required.',
      });
      return;
    }

    if (this.isEdit() && this.currentId != null) {
      this.api.update(this.currentId, request).subscribe({
        next: () => {
          this.loading.set(false);
          this.showDialog.set(false);
          this.messageService.add({ severity: 'success', summary: 'Location group updated' });
          this.loadGroups(ownerId);
        },
        error: (err) => {
          this.loading.set(false);
          this.messageService.add({
            severity: 'error',
            summary: 'Error',
            detail: err?.error?.message || err.message || 'Failed to update location group',
          });
        },
      });
      return;
    }

    this.api.create(request).subscribe({
      next: () => {
        this.loading.set(false);
        this.showDialog.set(false);
        this.messageService.add({ severity: 'success', summary: 'Location group created' });
        this.loadGroups(ownerId);
      },
      error: (err) => {
        this.loading.set(false);
        this.messageService.add({
          severity: 'error',
          summary: 'Error',
          detail: err?.error?.message || err.message || 'Failed to create location group',
        });
      },
    });
  }

  delete(group: LocationGroupDto): void {
    if (!confirm(`Delete location group "${group.name}"?`)) return;
    this.loading.set(true);
    this.api.delete(group.id).subscribe({
      next: () => {
        this.loading.set(false);
        this.messageService.add({ severity: 'success', summary: 'Location group deleted' });
        const ownerId = this.selectedOwnerId();
        if (ownerId) {
          this.loadGroups(ownerId);
        }
      },
      error: (err) => {
        this.loading.set(false);
        this.messageService.add({
          severity: 'error',
          summary: 'Error',
          detail: err?.error?.message || err.message || 'Failed to delete location group',
        });
      },
    });
  }
}
