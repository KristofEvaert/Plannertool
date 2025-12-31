export type TechnicalSection = {
  id: string;
  title: string;
  summary: string;
  businessLogic: string[];
  database: string[];
  api: string[];
  notes: string[];
};

export type TechnicalDiagram = {
  id: string;
  title: string;
  keywords: string[];
};

export const TECHNICAL_SECTIONS: TechnicalSection[] = [
  {
    id: 'architecture',
    title: 'Architecture and data flow',
    summary: 'Angular frontend calls the ASP.NET Core API, which uses EF Core for persistence.',
    businessLogic: [
      'Frontend (Angular) uses API services under frontend/src/app/_services.',
      'API controllers orchestrate requests and use application and infrastructure services.',
      'JWT auth protects staff endpoints; driver UI uses the same API with role checks.',
    ],
    database: [
      'EF Core DbContext: TransportPlannerDbContext (TransportPlanner.Infrastructure).',
      'SQL Server connection string: ConnectionStrings:DefaultConnection in TransportPlanner.Api/appsettings.json.',
      'Migrations apply on startup; DatabaseSeeder runs afterward.',
    ],
    api: [
      'AuthController: /api/auth/login issues JWT and sets roles.',
      'UsersController: /api/users for role and owner assignments.',
    ],
    notes: [
      'Role checks are enforced in API controllers using policies and User roles.',
      'Audit trail writes to App_Data/audit-trail.txt (AuditTrail options).',
    ],
  },
  {
    id: 'planning',
    title: 'Routing and auto-generate',
    summary: 'Auto-generate creates routes per day or period with weighted scoring.',
    businessLogic: [
      'Auto-generate for a driver: /api/routes/auto-generate.',
      'Auto-generate for all drivers: /api/routes/auto-generate/all.',
      'Weights include time, distance, due date, cost, and overtime.',
      'Service type matching can be enforced on planning and manual additions.',
      'Recalculations clear planned stops for the selected day before rebuilding.',
    ],
    database: [
      'Routes, RouteStops store the day plan.',
      'PlanningClusters and PlanningClusterItems group candidates.',
      'DriverAvailabilities, DayPlanLocks, DriverDayOverrides set day capacity.',
      'WeightTemplates and WeightTemplateLocationLinks store reusable weights.',
    ],
    api: [
      'RoutesController: create, update, clear, and override start/end locations.',
      'MapController: map data for the selected owner, service type, and date range.',
    ],
    notes: [
      'Route start and end fall back to the driver default if no override exists.',
      'ServiceLocation status switches between Open and Planned during route changes.',
    ],
  },
  {
    id: 'time-windows',
    title: 'Time windows and constraints',
    summary: 'Opening hours and exceptions define feasibility windows for planning.',
    businessLogic: [
      'Time window intersection uses driver availability, location opening hours, and due dates.',
      'If no opening hours exist, the location is treated as always open.',
      'Manual planning can override with a warning if outside hours.',
    ],
    database: [
      'ServiceLocationOpeningHours: weekly hours (dayOfWeek, openTime, closeTime, isClosed).',
      'ServiceLocationExceptions: date-specific closures or modified hours.',
      'ServiceLocationConstraints: min and max visit duration.',
    ],
    api: [
      'ServiceLocationsController: CRUD for locations.',
      'Service location hours and exceptions endpoints under /api/service-locations.',
    ],
    notes: [
      'Dates are treated as local time (owner time zone).',
    ],
  },
  {
    id: 'travel-model',
    title: 'Travel time and cost model',
    summary: 'Uses regional speed profiles and learned travel stats for ETA.',
    businessLogic: [
      'Travel time uses LearnedTravelStats when sample count threshold is met.',
      'Fallback order: learned stats, region profile, routing engine duration.',
      'Cost formula: distanceKm * fuelCostPerKm + (travelMinutes/60) * personnelCostPerHour.',
    ],
    database: [
      'TravelTimeRegions, RegionSpeedProfiles define regional defaults.',
      'LearnedTravelStats stores aggregated averages by region and hour.',
      'SystemCostSettings stores fuel/personnel cost values.',
    ],
    api: [
      'SystemCostSettingsController: /api/system-cost-settings.',
      'Travel time is used in route calculations within TravelTimeModelService.',
    ],
    notes: [
      'Travel time seed data is loaded automatically on first run.',
    ],
  },
  {
    id: 'execution',
    title: 'Driver execution and proof of visit',
    summary: 'Drivers record arrive and depart with audit trails.',
    businessLogic: [
      'Arrive and depart endpoints update stop status and proof fields.',
      'Manual edits update timestamps and store audit events.',
      'Depart or manual departure marks stop as visited.',
    ],
    database: [
      'RouteStops store actual arrival, departure, and proof status.',
      'RouteStopEvents store the audit trail for stop updates.',
    ],
    api: [
      'RouteStopsController: /api/routeStops/{id}/arrive and /depart.',
      'RouteStopsController: PATCH /api/routeStops/{id} for manual edits.',
    ],
    notes: [
      'Driver page is restricted to Driver role or admins.',
    ],
  },
  {
    id: 'messaging',
    title: 'Messages and route changes',
    summary: 'Driver messages and in-progress route changes are tracked with notifications.',
    businessLogic: [
      'Route changes while in progress create a new route version.',
      'Notifications are created per driver and acknowledged in the driver UI.',
      'Driver messages push to planners via SignalR.',
    ],
    database: [
      'RouteVersions and RouteChangeNotifications track mid-day changes.',
      'RouteMessages store planner-driver messages.',
    ],
    api: [
      'RouteChangeNotificationsController: list and acknowledge.',
      'RouteMessagesController: create and resolve messages.',
      'RouteMessagesHub: SignalR updates for planners.',
    ],
    notes: [
      'Planner follow-up page shows messages and status.',
    ],
  },
  {
    id: 'bulk',
    title: 'Bulk import and export',
    summary: 'Excel and JSON bulk flows update drivers and service locations.',
    businessLogic: [
      'Service locations bulk upload upserts by ERP ID.',
      'Driver availability bulk upload clears values when cells are empty.',
      'Driver service types bulk upload overwrites existing links.',
    ],
    database: [
      'ServiceLocations, DriverAvailabilities, DriverServiceTypes updated by bulk services.',
      'Bulk failures return error lists or Excel error files.',
    ],
    api: [
      'ServiceLocationsBulkController: bulk insert and templates.',
      'DriversBulkController: availability and service type uploads.',
    ],
    notes: [
      'Templates include available service types and driver emails.',
    ],
  },
  {
    id: 'exports',
    title: 'Exports',
    summary: 'Schedule export for selected map period without driver info.',
    businessLogic: [
      'Export filters by owner, service type, and date range.',
      'Output omits driver names and IDs.',
      'Rows are ordered by planned date and time.',
    ],
    database: [
      'Routes and RouteStops supply planned order and timing.',
      'ServiceLocations provide name and address.',
    ],
    api: [
      'ExportsController: /api/exports/routes?from=...&to=...&ownerId=...&serviceTypeId=...',
    ],
    notes: [
      'Excel format is used for exports.',
    ],
  },
  {
    id: 'security',
    title: 'Security and permissions',
    summary: 'Role-based access enforces staff and driver boundaries.',
    businessLogic: [
      'SuperAdmin and Admin manage templates, owners, and system settings.',
      'Planner can run auto-generate and manage planning without editing templates.',
      'Driver only accesses their own route execution flow.',
    ],
    database: [
      'AspNetUsers, AspNetRoles, and AspNetUserRoles handle identity.',
      'Drivers link to ApplicationUser via UserId.',
    ],
    api: [
      'Authorization policies enforce role checks per controller.',
      'AuthController uses JWT with issuer and audience from appsettings.',
    ],
    notes: [
      'Owners are used to limit data visibility for staff.',
    ],
  },
];

export const TECHNICAL_DIAGRAMS: TechnicalDiagram[] = [
  {
    id: 'flow',
    title: 'Planning flow',
    keywords: ['flow', 'architecture', 'api', 'db', 'routing', 'planner'],
  },
  {
    id: 'data-model',
    title: 'Core data model',
    keywords: ['data', 'model', 'routes', 'stops', 'drivers', 'locations'],
  },
  {
    id: 'travel-model',
    title: 'Travel time model',
    keywords: ['travel', 'time', 'region', 'learned', 'stats'],
  },
];
