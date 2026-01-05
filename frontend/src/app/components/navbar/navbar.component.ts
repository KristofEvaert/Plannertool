import { Component, computed, inject } from '@angular/core';
import { Router, RouterModule } from '@angular/router';
import { AuthService } from '@services/auth.service';
import { MenuItem } from 'primeng/api';
import { MenubarModule } from 'primeng/menubar';

@Component({
  selector: 'app-navbar',
  imports: [RouterModule, MenubarModule],
  template: ` @if (menuItems().length > 0) {
    <div class="fixed top-0 left-0 right-0 z-50 bg-white border-b border-gray-200 shadow-sm h-16">
      <div class="container mx-auto px-6 h-full">
        <p-menubar [model]="menuItems()" [style]="{ border: 'none', borderRadius: '0' }">
          <ng-template #start>
            <span class="text-xl font-bold text-gray-900">Service Planning Tool</span>
          </ng-template>

          <ng-template #end>
            <p-menubar [model]="userMenuItems()" [style]="{ border: 'none', borderRadius: '0' }" />
          </ng-template>
        </p-menubar>
      </div>
    </div>
  }`,
  styles: [
    `
      :host ::ng-deep .p-menubar {
        border: none;
        border-radius: 0;
        background: transparent;
        padding: 0;
        height: 100%;
        display: flex;
        align-items: center;
      }
      :host ::ng-deep .p-menubar-root-list {
        margin-left: auto;
      }
    `,
  ],
  standalone: true,
})
export class NavbarComponent {
  public readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  private readonly roles = computed(() => this.auth.currentUser()?.roles ?? []);

  private readonly isStaff = computed(() =>
    this.roles().some((r) => ['SuperAdmin', 'Admin', 'Planner'].includes(r)),
  );
  private readonly isAdmin = computed(() =>
    this.roles().some((r) => ['SuperAdmin', 'Admin'].includes(r)),
  );
  private readonly isSuperAdmin = computed(() => this.roles().includes('SuperAdmin'));
  private readonly isDriver = computed(() => this.roles().includes('Driver'));

  public readonly menuItems = computed<MenuItem[]>(() => {
    const items: MenuItem[] = [];
    if (this.auth.currentUser()) {
      if (this.isStaff()) {
        items.push({ label: 'Dashboard', icon: 'pi pi-home', routerLink: '/start' });
        items.push({ label: 'Map', icon: 'pi pi-map', routerLink: '/map' });
        items.push({ label: 'Drivers', icon: 'pi pi-users', routerLink: '/drivers' });
        items.push({ label: 'Service Types', icon: 'pi pi-list', routerLink: '/service-types' });
        items.push({
          label: 'Service Locations',
          icon: 'pi pi-map-marker',
          routerLink: '/service-locations',
        });
        items.push({
          label: 'Route Follow-up',
          icon: 'pi pi-compass',
          routerLink: '/route-followup',
        });
      }
      if (this.isDriver() || this.isAdmin()) {
        items.push({
          label: this.isDriver() ? 'My Schedule' : 'Driver',
          icon: 'pi pi-car',
          routerLink: '/driver',
        });
      }
    }
    return items;
  });

  public readonly userMenuItems = computed<MenuItem[]>(() => {
    const items: MenuItem[] = [];
    if (this.auth.currentUser()) {
      const userItems: MenuItem[] = [];
      if (this.isAdmin()) {
        userItems.push(
          { label: 'Users/Roles', icon: 'pi pi-user-edit', routerLink: '/users' },
          { label: 'Weight Templates', icon: 'pi pi-cog', routerLink: '/weight-templates' },
          { label: 'Cost Settings', icon: 'pi pi-dollar', routerLink: '/system-cost-settings' },
        );

        if (this.isSuperAdmin()) {
          userItems.push(
            { label: 'Owners', icon: 'pi pi-building', routerLink: '/owners' },
            { label: 'Audit Trail', icon: 'pi pi-history', routerLink: '/audit-trail' },
            {
              label: 'Travel Time Model',
              icon: 'pi pi-chart-line',
              routerLink: '/travel-time-model',
            },
          );
        }
      }

      userItems.push({
        label: 'Logout',
        icon: 'pi pi-sign-out',
        command: () => {
          this.auth.logout();
          this.router.navigate(['/login']);
        },
      });
      items.push({
        label: this.auth.currentUser()?.displayName || this.auth.currentUser()?.email,
        icon: 'pi pi-user',
        items: userItems,
      });
    }

    return items;
  });
}
