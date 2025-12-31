export type ManualSection = {
  id: string;
  title: string;
  route?: string;
  summary: string;
  functions: string[];
  options: string[];
};

export const MANUAL_SECTIONS: ManualSection[] = [
  {
    id: 'dashboard',
    title: 'Dashboard',
    route: '/start',
    summary: 'Quick entry point to the planning workflow and key tabs.',
    functions: [
      'Navigate to the core planning tabs with the quick buttons.',
      'Review the planning workflow at a glance.',
      'Open the full manual from the info icon.',
    ],
    options: [
      'Quick actions open Drivers and Service Locations.',
    ],
  },
  {
    id: 'map',
    title: 'Map',
    route: '/map',
    summary: 'Plan routes on a map and auto-generate schedules.',
    functions: [
      'Load service locations by service type, owner, and date range.',
      'Visualize urgency and planned status using the legend.',
      'Auto-generate routes for the selected day or for a period.',
      'Manually build or adjust routes, reorder stops, and clear routes.',
      'Override route start and end locations and recalculate.',
      'Export the schedule for the selected filters.',
    ],
    options: [
      'Select the active day for driver routes.',
      'Choose a weight template or set manual weights (time, distance, due date, cost, overtime).',
      'Enable or disable service type matching when planning.',
    ],
  },
  {
    id: 'drivers',
    title: 'Drivers',
    route: '/drivers',
    summary: 'Manage drivers, availability, and service type assignments.',
    functions: [
      'View and filter drivers by owner.',
      'Add or edit drivers, including start address and max work minutes.',
      'Assign service types to drivers.',
      'Toggle active status.',
      'Maintain availability by date range.',
      'Download and upload Excel templates for availability and service types.',
    ],
    options: [
      'Owner filter and a toggle for drivers with availability.',
      'Start date and range control the visible grid.',
    ],
  },
  {
    id: 'service-types',
    title: 'Service Types',
    route: '/service-types',
    summary: 'Define and view service type catalog entries.',
    functions: [
      'Create new service types with code, name, and description.',
      'Review active and inactive service types.',
    ],
    options: [
      'Existing entries are read-only in the table.',
    ],
  },
  {
    id: 'service-locations',
    title: 'Service Locations',
    route: '/service-locations',
    summary: 'Maintain locations, planning metadata, and bulk imports.',
    functions: [
      'Create and edit service locations.',
      'Bulk upload via Excel and download templates.',
      'Filter by status, owner, service type, and date range.',
      'Manage opening hours, exceptions, and visit duration constraints.',
      'Update due dates, priority dates, and instructions.',
    ],
    options: [
      'Bulk uploads use the selected owner and service type.',
      'Opening hours control planning feasibility for auto generation.',
    ],
  },
  {
    id: 'route-followup',
    title: 'Route Follow-up',
    route: '/route-followup',
    summary: 'Track routes and driver messages for a specific day.',
    functions: [
      'Load a route by owner, driver, and date.',
      'Review stop status and timing on the map and list.',
      'Read driver messages and mark them read or resolved.',
    ],
    options: [
      'Reload to refresh the current selection.',
    ],
  },
  {
    id: 'driver',
    title: 'Driver',
    route: '/driver',
    summary: 'Driver route execution and proof of visit.',
    functions: [
      'View assigned route and stops for the selected day.',
      'Record arrive and depart events for each stop.',
      'Edit arrival and departure times manually if needed.',
      'Send messages to the planner for a route or stop.',
      'Acknowledge route change notifications.',
    ],
    options: [
      'Owner, driver, and date selectors load the active route.',
    ],
  },
  {
    id: 'users',
    title: 'Users and Roles',
    route: '/users',
    summary: 'Manage user access, roles, and owner links.',
    functions: [
      'Filter users by email.',
      'Assign roles and update display names.',
      'Link staff accounts to owners.',
      'Set driver start address and coordinates for driver users.',
    ],
    options: [
      'Role controls are limited by current user permissions.',
    ],
  },
  {
    id: 'weight-templates',
    title: 'Weight Templates',
    route: '/weight-templates',
    summary: 'Create reusable weight sets for auto generation.',
    functions: [
      'Create, edit, and delete templates (admin only).',
      'Set scope by global, owner, service type, or location group.',
      'Assign templates to locations or groups.',
    ],
    options: [
      'Filters for owner and service type.',
      'Weights include distance, travel time, due date, cost, and overtime.',
    ],
  },
  {
    id: 'location-groups',
    title: 'Location Groups',
    route: '/location-groups',
    summary: 'Group service locations for template assignment.',
    functions: [
      'Create and edit location groups.',
      'Add or remove service locations in the group.',
    ],
    options: [
      'Owner filter for super admins.',
    ],
  },
  {
    id: 'cost-settings',
    title: 'Cost Settings',
    route: '/system-cost-settings',
    summary: 'Set fuel and personnel cost inputs for route cost scoring.',
    functions: [
      'Update fuel cost per km and personnel cost per hour.',
      'Set currency code for display.',
    ],
    options: [
      'Save persists values used by auto-generate cost weighting.',
    ],
  },
  {
    id: 'owners',
    title: 'Owners',
    route: '/owners',
    summary: 'Manage service location owners (super admin).',
    functions: [
      'Create, edit, and deactivate owners.',
      'Toggle the include inactive filter.',
    ],
    options: [
      'Inactive owners can be shown or hidden.',
    ],
  },
  {
    id: 'audit-trail',
    title: 'Audit Trail',
    route: '/audit-trail',
    summary: 'Review system activity for troubleshooting and compliance.',
    functions: [
      'Filter entries by date range and search text.',
      'Refresh and clear filters to reload activity.',
    ],
    options: [
      'Search supports path, user, body, and trace fields.',
    ],
  },
];
