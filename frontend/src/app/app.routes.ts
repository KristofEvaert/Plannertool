import { Routes } from '@angular/router';
import { authGuard } from '@guards/auth.guard';

export const routes: Routes = [
  {
    path: '',
    pathMatch: 'full',
    redirectTo: 'login',
  },
  {
    path: 'login',
    loadComponent: () => import('@pages/login/login.page').then((m) => m.LoginPage),
  },
  {
    path: 'driver',
    canMatch: [authGuard],
    data: { roles: ['SuperAdmin', 'Admin', 'Driver'] },
    loadComponent: () => import('@pages/driver/driver.page').then((m) => m.DriverPage),
  },
  {
    path: 'drivers',
    canMatch: [authGuard],
    data: { roles: ['SuperAdmin', 'Admin', 'Planner'] },
    loadComponent: () => import('@pages/drivers/drivers-availability-grid.page').then((m) => m.DriversAvailabilityGridPage),
  },
  {
    path: 'service-locations',
    canMatch: [authGuard],
    data: { roles: ['SuperAdmin', 'Admin', 'Planner'] },
    loadComponent: () => import('@pages/service-locations/service-locations.page').then((m) => m.ServiceLocationsPage),
  },
  {
    path: 'service-types',
    canMatch: [authGuard],
    data: { roles: ['SuperAdmin', 'Admin', 'Planner'] },
    loadComponent: () => import('@pages/service-types/service-types.page').then((m) => m.ServiceTypesPage),
  },
  {
    path: 'weight-templates',
    canMatch: [authGuard],
    data: { roles: ['SuperAdmin', 'Admin'] },
    loadComponent: () =>
      import('@pages/weight-templates/weight-templates.page').then((m) => m.WeightTemplatesPage),
  },
  {
    path: 'location-groups',
    canMatch: [authGuard],
    data: { roles: ['SuperAdmin', 'Admin'] },
    loadComponent: () =>
      import('@pages/location-groups/location-groups.page').then((m) => m.LocationGroupsPage),
  },
  {
    path: 'map',
    canMatch: [authGuard],
    data: { roles: ['SuperAdmin', 'Admin', 'Planner'] },
    loadComponent: () => import('@pages/map/map.page').then((m) => m.MapPage),
  },
  {
    path: 'route-followup',
    canMatch: [authGuard],
    data: { roles: ['SuperAdmin', 'Admin', 'Planner'] },
    loadComponent: () =>
      import('@pages/route-followup/route-followup.page').then((m) => m.RouteFollowupPage),
  },
  {
    path: 'users',
    canMatch: [authGuard],
    data: { roles: ['SuperAdmin', 'Admin'] },
    loadComponent: () =>
      import('@pages/users/users.page').then((m) => m.UsersPage),
  },
  {
    path: 'system-cost-settings',
    canMatch: [authGuard],
    data: { roles: ['SuperAdmin', 'Admin'] },
    loadComponent: () =>
      import('@pages/system-cost-settings/system-cost-settings.page').then((m) => m.SystemCostSettingsPage),
  },
  {
    path: 'owners',
    canMatch: [authGuard],
    data: { roles: ['SuperAdmin'] },
    loadComponent: () => import('@pages/owners/owners.page').then((m) => m.OwnersPage),
  },
  {
    path: 'audit-trail',
    canMatch: [authGuard],
    data: { roles: ['SuperAdmin'] },
    loadComponent: () => import('@pages/audit-trail/audit-trail.page').then((m) => m.AuditTrailPage),
  },
  {
    path: 'start',
    canMatch: [authGuard],
    loadComponent: () => import('@pages/start/start.page').then((m) => m.StartPage),
  },
  {
    path: '**',
    redirectTo: '',
  },
];
