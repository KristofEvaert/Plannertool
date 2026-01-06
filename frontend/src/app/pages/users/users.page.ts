import { NgTemplateOutlet } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { HelpManualComponent } from '@components/help-manual/help-manual.component';
import type { UserDto } from '@models/user.model';
import { AuthService } from '@services/auth.service';
import { ServiceLocationOwnersApiService } from '@services/service-location-owners-api.service';
import { UsersApiService } from '@services/users-api.service';
import { MessageService } from 'primeng/api';
import { BadgeModule } from 'primeng/badge';
import { ButtonModule } from 'primeng/button';
import { CardModule } from 'primeng/card';
import { CheckboxModule } from 'primeng/checkbox';
import { InputGroupModule } from 'primeng/inputgroup';
import { InputGroupAddonModule } from 'primeng/inputgroupaddon';
import { InputTextModule } from 'primeng/inputtext';
import { SelectModule } from 'primeng/select';
import { ToastModule } from 'primeng/toast';

const ALL_ROLES = ['SuperAdmin', 'Admin', 'Planner', 'Driver'] as const;
type Role = (typeof ALL_ROLES)[number];

@Component({
  selector: 'app-users-page',
  standalone: true,
  imports: [
    FormsModule,
    ButtonModule,
    InputTextModule,
    SelectModule,
    ToastModule,
    HelpManualComponent,
    CardModule,
    CheckboxModule,
    InputGroupModule,
    InputGroupAddonModule,
    BadgeModule,
    NgTemplateOutlet,
  ],
  providers: [MessageService],
  templateUrl: './users.page.html',
})
export class UsersPage {
  private readonly usersApi = inject(UsersApiService);
  private readonly ownersApi = inject(ServiceLocationOwnersApiService);
  private readonly messageService = inject(MessageService);
  private readonly auth = inject(AuthService);

  users = signal<UserDto[]>([]);
  emailFilter = signal('');
  owners = signal<{ label: string; value: number }[]>([]);
  loading = signal(false);

  createEmail = '';
  createPassword = '';
  createDisplayName = '';

  roleSelections = new Map<string, Set<Role>>();
  driverOwnerSelection: Record<string, number | undefined> = {};
  staffOwnerSelection: Record<string, number | undefined> = {};
  driverStartAddressSelection: Record<string, string> = {};
  driverStartLatitudeSelection: Record<string, number | null | undefined> = {};
  driverStartLongitudeSelection: Record<string, number | null | undefined> = {};
  rolesList: Role[] = [...ALL_ROLES];

  isCurrentSuperAdmin(): boolean {
    return this.auth.currentUser()?.roles.includes('SuperAdmin') ?? false;
  }

  filteredUsers = computed(() => {
    const term = this.emailFilter().trim().toLowerCase();
    if (!term) return this.users();
    return this.users().filter((u) => u.email.toLowerCase().includes(term));
  });

  pendingUsers = computed(() => {
    return this.filteredUsers().filter((u) => u.roles.length === 0);
  });

  regularUsers = computed(() => {
    return this.filteredUsers().filter((u) => u.roles.length > 0);
  });

  constructor() {
    this.loadOwners();
    this.loadUsers();
  }

  loadOwners(): void {
    this.ownersApi.getAll(false).subscribe({
      next: (data) => {
        this.owners.set(data.map((o) => ({ label: o.name, value: o.id })));
      },
    });
  }

  loadUsers(): void {
    this.loading.set(true);
    this.usersApi.getUsers().subscribe({
      next: (data) => {
        this.users.set(data);
        this.roleSelections.clear();
        this.driverOwnerSelection = {};
        this.staffOwnerSelection = {};
        data.forEach((u) => {
          this.roleSelections.set(u.id, new Set(u.roles as Role[]));
          if (u.driverOwnerId) {
            this.driverOwnerSelection[u.id] = u.driverOwnerId;
          }
          if (u.ownerId) {
            this.staffOwnerSelection[u.id] = u.ownerId;
          }
          this.driverStartAddressSelection[u.id] = u.driverStartAddress || '';
          this.driverStartLatitudeSelection[u.id] =
            u.driverStartLatitude && u.driverStartLatitude !== 0 ? u.driverStartLatitude : null;
          this.driverStartLongitudeSelection[u.id] =
            u.driverStartLongitude && u.driverStartLongitude !== 0 ? u.driverStartLongitude : null;
        });
        this.loading.set(false);
      },
      error: (err) => {
        this.loading.set(false);
        this.messageService.add({
          severity: 'error',
          summary: 'Error',
          detail: err?.message || 'Failed to load users',
        });
      },
    });
  }

  createUser(): void {
    if (!this.createEmail || !this.createPassword) {
      this.messageService.add({
        severity: 'warn',
        summary: 'Validation',
        detail: 'Email and password required',
      });
      return;
    }
    this.loading.set(true);
    this.usersApi
      .createUser({
        email: this.createEmail,
        password: this.createPassword,
        displayName: this.createDisplayName || undefined,
      })
      .subscribe({
        next: () => {
          this.messageService.add({ severity: 'success', summary: 'User created' });
          this.createEmail = '';
          this.createPassword = '';
          this.createDisplayName = '';
          this.loadUsers();
        },
        error: (err) => {
          this.loading.set(false);
          this.messageService.add({
            severity: 'error',
            summary: 'Error',
            detail: err?.error?.message || err?.message || 'Failed to create user',
          });
        },
      });
  }

  toggleRole(userId: string, role: Role): void {
    const set = this.roleSelections.get(userId) ?? new Set<Role>();
    if (set.has(role)) set.delete(role);
    else set.add(role);
    this.roleSelections.set(userId, set);
  }

  assignRoles(user: UserDto): void {
    const roles = Array.from(this.roleSelections.get(user.id) ?? []);
    const ownerIdForDriver = roles.includes('Driver')
      ? this.driverOwnerSelection[user.id]
      : undefined;
    if (roles.includes('Driver') && (!ownerIdForDriver || ownerIdForDriver <= 0)) {
      this.messageService.add({
        severity: 'warn',
        summary: 'Validation',
        detail: 'Select an owner for driver role',
      });
      return;
    }
    const startAddress = (this.driverStartAddressSelection[user.id] || '').trim();
    const startLatitude = this.driverStartLatitudeSelection[user.id];
    const startLongitude = this.driverStartLongitudeSelection[user.id];
    const hasStartAddress = startAddress.length > 0;
    const hasStartLatitude =
      startLatitude !== null && startLatitude !== undefined && startLatitude !== 0;
    const hasStartLongitude =
      startLongitude !== null && startLongitude !== undefined && startLongitude !== 0;
    if (roles.includes('Driver')) {
      if (hasStartLatitude !== hasStartLongitude) {
        this.messageService.add({
          severity: 'warn',
          summary: 'Validation',
          detail: 'Provide both latitude and longitude, or leave both empty',
        });
        return;
      }
      if (!hasStartAddress && !hasStartLatitude) {
        this.messageService.add({
          severity: 'warn',
          summary: 'Validation',
          detail: 'Provide start address or start coordinates for driver',
        });
        return;
      }
    }
    const needsStaffOwner = roles.some((r) => r === 'Admin' || r === 'Planner');
    const ownerIdForStaff = needsStaffOwner ? this.staffOwnerSelection[user.id] : undefined;
    if (needsStaffOwner && (!ownerIdForStaff || ownerIdForStaff <= 0)) {
      this.messageService.add({
        severity: 'warn',
        summary: 'Validation',
        detail: 'Select an owner for admin/planner',
      });
      return;
    }
    this.loading.set(true);
    this.usersApi
      .assignRoles({
        userId: user.id,
        roles,
        ownerIdForDriver,
        ownerIdForStaff,
        displayName: user.displayName,
        driverStartAddress: hasStartAddress ? startAddress : undefined,
        driverStartLatitude: hasStartLatitude ? startLatitude! : undefined,
        driverStartLongitude: hasStartLongitude ? startLongitude! : undefined,
      })
      .subscribe({
        next: (updated) => {
          this.messageService.add({ severity: 'success', summary: 'Roles updated' });
          this.loading.set(false);
          this.users.update((list) =>
            list.map((u) => (u.id === updated.id ? { ...u, ...updated } : u)),
          );
          this.roleSelections.set(updated.id, new Set(updated.roles as Role[]));
          if (updated.driverOwnerId) {
            this.driverOwnerSelection[updated.id] = updated.driverOwnerId;
          }
          if (updated.ownerId) {
            this.staffOwnerSelection[updated.id] = updated.ownerId;
          }
          this.driverStartAddressSelection[updated.id] = updated.driverStartAddress || '';
          this.driverStartLatitudeSelection[updated.id] =
            updated.driverStartLatitude && updated.driverStartLatitude !== 0
              ? updated.driverStartLatitude
              : null;
          this.driverStartLongitudeSelection[updated.id] =
            updated.driverStartLongitude && updated.driverStartLongitude !== 0
              ? updated.driverStartLongitude
              : null;
        },
        error: (err) => {
          this.loading.set(false);
          if (
            err.error.message ===
            'Unable to resolve DriverStartLatitude/DriverStartLongitude from DriverStartAddress.'
          ) {
            this.messageService.add({
              severity: 'error',
              summary: 'Error',
              detail: 'Unable to get coordinates from address',
            });
          } else {
            this.messageService.add({
              severity: 'error',
              summary: 'Error',
              detail: err?.error?.message || err?.message || 'Failed to assign roles',
            });
          }
        },
      });
  }

  rolesFor(userId: string): Role[] {
    return Array.from(this.roleSelections.get(userId) ?? []);
  }
}
