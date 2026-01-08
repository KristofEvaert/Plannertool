import type { DriverAvailabilityDto, DriverDto } from './';

export interface ServiceLocationMapDto {
  toolId: string;
  erpId: number;
  name: string;
  address?: string;
  latitude: number;
  longitude: number;
  dueDate: string;
  priorityDate?: string;
  orderDate: string;
  serviceTypeId: number;
  status: string;   serviceMinutes: number;
  plannedDate?: string;
  plannedDriverName?: string;
}

export interface ServiceLocationsMapResponseDto {
  from: string;
  to: string;
  totalCount: number;
  minOrderDate?: string;
  maxOrderDate?: string;
  items: ServiceLocationMapDto[];
}

export interface DriverWithAvailability {
  driver: DriverDto;
  availability: DriverAvailabilityDto | null;
}

export interface RouteWaypoint {
  type: 'driver-start' | 'location' | 'driver-end';
  name: string;
  address?: string;
  latitude: number;
  longitude: number;
  serviceMinutes?: number;
  erpId?: number;
  travelMinutesFromPrev?: number;
  travelKmFromPrev?: number;
}

export interface RouteOverride {
  address?: string;
  latitude?: number;
  longitude?: number;
}

export interface RouteInfo {
  driver: DriverDto;
  waypoints: RouteWaypoint[];
  totalDistanceKm: number;
  totalTimeMinutes: number;
  roadGeometry?: [number, number][];   startOverride?: RouteOverride;
  endOverride?: RouteOverride;
  isAwaitingBackendTotals?: boolean;
  distanceSource?: 'local' | 'backend';
}

export interface StopSchedule {
  label: string;
  arrivalMinute: number;
  departureMinute: number;
  serviceMinutes: number;
}

export interface LocationWindowInfo {
  isClosed: boolean;
  openMinute: number;
  closeMinute: number;
  label: string;
}

export interface LocationHoursDisplay {
  label: string;
  isClosed: boolean;
  isLoading?: boolean;
}

export interface ArrivalWindow {
  startMinute: number;
  endMinute: number;
}

export interface BulkAddRejection {
  name: string;
  address?: string;
  reason: string;
}

export interface CacheEntry<T> {
  value: T;
  fetchedAt: number;
}

export interface MapPagePreferences {
  ownerId?: number;
  serviceTypeIds?: number[];
  fromDate?: string;
  toDate?: string;
  weightTemplateId?: number | null;
  normalizeWeights?: boolean;
  dueCostCapPercent?: number;
  detourCostCapPercent?: number;
  detourRefKmPercent?: number;
  lateRefMinutesPercent?: number;
}

export interface WeightTemplateOption {
  label: string;
  value: number;
}

export type MarkerColorKey = 'green' | 'yellow' | 'orange' | 'red' | 'white' | 'black';
