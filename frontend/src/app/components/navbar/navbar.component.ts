import { Component, computed, inject, viewChild } from '@angular/core';
import { Router, RouterModule } from '@angular/router';
import { AuthService } from '@services/auth.service';
import { MenuItem } from 'primeng/api';
import { Menu, MenuModule } from 'primeng/menu';
import { MenubarModule } from 'primeng/menubar';

@Component({
  selector: 'app-navbar',
  imports: [RouterModule, MenubarModule, MenuModule],
  template: ` @if (this.auth.currentUser()) {
    <div class="fixed top-0 left-0 right-0 z-50 bg-white border-b border-gray-200 shadow-sm h-16">
      <div class="mx-auto px-6 h-full">
        <p-menubar
          breakpoint="1350px"
          [model]="menuItems()"
          [style]="{ border: 'none', borderRadius: '0' }"
        >
          <ng-template #start>
            <span class="text-xl font-bold text-gray-900">Service Planning Tool</span>
          </ng-template>

          <ng-template #end>
            <div
              class="flex items-center gap-2 cursor-pointer px-3 py-2 hover:bg-gray-100 rounded-md transition-colors"
              (click)="userMenu.toggle($event)"
            >
              <i class="pi pi-user text-gray-600"></i>
              <span class="font-medium text-gray-700 text-sm">{{
                auth.currentUser()?.displayName || auth.currentUser()?.email
              }}</span>
              <i class="pi pi-angle-down text-gray-500 text-xs"></i>
            </div>
            <p-menu #userMenu appendTo="body" [model]="userMenuItems()" [popup]="true" />
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
  public userMenu = viewChild<Menu>('userMenu');
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
    if (!this.auth.currentUser()) {
      return [];
    }
    const items: MenuItem[] = [];

    if (this.isAdmin()) {
      items.push(
        { label: 'Users/Roles', icon: 'pi pi-user-edit', routerLink: '/users' },
        { label: 'Weight Templates', icon: 'pi pi-cog', routerLink: '/weight-templates' },
        { label: 'Cost Settings', icon: 'pi pi-dollar', routerLink: '/system-cost-settings' },
      );

      if (this.isSuperAdmin()) {
        items.push(
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

    items.push({
      label: 'Logout',
      icon: 'pi pi-sign-out',
      command: () => {
        this.userMenu()?.hide();
        this.userMenu()?.onHide.subscribe(() => {
          this.auth.logout();
          this.router.navigate(['/login']);
        });
      },
    });

    return items;
  });
}
