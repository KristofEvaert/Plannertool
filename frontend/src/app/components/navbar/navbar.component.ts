import { Component, effect, inject, signal } from '@angular/core';
import { Router, RouterModule } from '@angular/router';
import { MenubarModule } from 'primeng/menubar';
import { MenuItem } from 'primeng/api';
import { AuthService } from '@services/auth.service';

@Component({
  selector: 'app-navbar',
  imports: [RouterModule, MenubarModule],
  template: `
    <div class="fixed top-0 left-0 right-0 z-50 bg-white border-b border-gray-200 shadow-sm h-16">
      <div class="container mx-auto px-6 h-full">
        <p-menubar [model]="menuItems()" [style]="{ border: 'none', borderRadius: '0' }">
          <ng-template pTemplate="start">
            <span class="text-xl font-bold text-gray-900">Service Planning Tool</span>
          </ng-template>
        </p-menubar>
      </div>
    </div>
  `,
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
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  menuItems = signal<MenuItem[]>([]);

  constructor() {
    effect(() => {
      const user = this.auth.currentUser();
      const roles = user?.roles ?? [];
      const isStaff = roles.some((r) => ['SuperAdmin', 'Admin', 'Planner'].includes(r));
      const isAdmin = roles.some((r) => ['SuperAdmin', 'Admin'].includes(r));
      const isSuperAdmin = roles.includes('SuperAdmin');
      const isDriver = roles.includes('Driver');

      const items: MenuItem[] = [];
      if (user) {
        if (isStaff) {
          items.push({ label: 'Dashboard', routerLink: '/start' });
          items.push({ label: 'Map', routerLink: '/map' });
          items.push({ label: 'Drivers', routerLink: '/drivers' });
          items.push({ label: 'Service Types', routerLink: '/service-types' });
          items.push({ label: 'Service Locations', routerLink: '/service-locations' });
          items.push({ label: 'Route Follow-up', routerLink: '/route-followup' });
        }
        if (isDriver || isAdmin) {
          items.push({ label: 'Driver', routerLink: '/driver' });
        }
        if (isAdmin) {
          items.push({ label: 'Users/Roles', routerLink: '/users' });
          items.push({ label: 'Weight Templates', routerLink: '/weight-templates' });
          items.push({ label: 'Cost Settings', routerLink: '/system-cost-settings' });
        }
        if (isSuperAdmin) {
          items.push({ label: 'Owners', routerLink: '/owners' });
          items.push({ label: 'Audit Trail', routerLink: '/audit-trail' });
          items.push({ label: 'Travel Time Model', routerLink: '/travel-time-model' });
        }
        items.push({
          label: `Logout (${user.displayName || user.email})`,
          command: () => {
            this.auth.logout();
            this.router.navigate(['/login']);
          },
        });
      } else {
        items.push({ label: 'Login', routerLink: '/login' });
      }
      this.menuItems.set(items);
    });
  }
}
