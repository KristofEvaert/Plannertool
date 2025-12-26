export interface DayOverviewDto {
  date: string; // ISO date string
  isLocked: boolean;
  extraWorkMinutes: number;
  drivers: DriverRouteSummaryDto[];
  unplannedPoles: PoleDto[];
  // Backlog counts (existing)
  eligibleBacklogCount: number;
  dueTodayCount: number;
  overdueCount: number;
  dueTodayUnplannedCount: number;
  fixedForDayCount: number;
  fixedForDayUnplannedCount: number;
  // New backlog metrics
  totalOpenPolesCount: number;
  unplannedInHorizonCount: number;
  plannedInHorizonCount: number;
  horizonDays: number;
  // Backlog-driven metrics (core)
  totalBacklogCount: number;
  plannedTodayCount: number;
  remainingBacklogCount: number;
  // Horizon-based metrics (primary)
  totalToPlanInHorizonCount: number;
  remainingInHorizonCount: number;
}

export interface DriverRouteSummaryDto {
  driverId: number;
  driverName: string;
  routeId?: number;
  isRouteLocked: boolean;
  stopCount: number;
  totalMinutes: number;
  totalKm: number;
  startLatitude: number;
  startLongitude: number;
  imageUrl?: string;
  stops: RouteStopDto[];
}

export interface RouteStopDto {
  sequence: number;
  poleId: number;
  serial: string;
  latitude: number;
  longitude: number;
  address?: string;
}

export interface PoleDto {
  poleId: number;
  serial: string;
  latitude: number;
  longitude: number;
  dueDate: string; // ISO date string
  fixedDate?: string; // ISO date string
  serviceMinutes: number;
}

export interface DriverDayDto {
  date: string; // ISO date string
  driverId: number;
  driverName: string;
  dayIsLocked: boolean;
  routeId?: number;
  routeIsLocked: boolean;
  totalMinutes: number;
  totalKm: number;
  startLatitude: number;
  startLongitude: number;
  imageUrl?: string;
  stops: DriverStopDto[];
}

export interface DriverStopDto {
  sequence: number;
  poleId: number;
  serial: string;
  latitude: number;
  longitude: number;
  address?: string;
  dueDate: string; // ISO date string
  fixedDate?: string; // ISO date string
  serviceMinutes: number;
  plannedStart?: string; // ISO date-time string
  plannedEnd?: string; // ISO date-time string
  travelMinutesFromPrev: number;
  travelKmFromPrev: number;
}

export interface GeneratePlanRequest {
  fromDate: string; // ISO date string
  days: number;
}

export interface GeneratePlanResultDto {
  fromDate: string; // ISO date string
  days: number;
  generatedDays: number;
  skippedLockedDays: number;
  plannedPolesCount: number;
  unplannedPolesCount: number;
}

export interface PlanDaySettingsDto {
  date: string; // ISO date string
  extraWorkMinutes: number;
}

export interface SetExtraWorkMinutesRequest {
  extraWorkMinutes: number;
}

