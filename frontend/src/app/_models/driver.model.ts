export interface DriverDto {
  toolId: string; // Guid as string
  erpId: number;
  name: string;
  startAddress?: string;
  startLatitude: number;
  startLongitude: number;
  defaultServiceMinutes: number;
  maxWorkMinutesPerDay: number;
  ownerId: number;
  ownerName?: string; // Convenience field from backend
  isActive: boolean;
  serviceTypeIds: number[];
}

export interface CreateDriverRequest {
  erpId: number;
  name: string;
  startAddress?: string;
  startLatitude: number | null;
  startLongitude: number | null;
  defaultServiceMinutes: number;
  maxWorkMinutesPerDay: number;
  ownerId: number; // Required
  isActive: boolean;
  serviceTypeIds?: number[];
}

export interface UpdateDriverRequest {
  erpId: number;
  name: string;
  startAddress?: string;
  startLatitude: number | null;
  startLongitude: number | null;
  defaultServiceMinutes: number;
  maxWorkMinutesPerDay: number;
  ownerId: number; // Required
  isActive: boolean;
  serviceTypeIds?: number[];
}

export interface DriverAvailabilityDto {
  date: string; // ISO date string
  startMinuteOfDay: number;
  endMinuteOfDay: number;
  availableMinutes: number;
}

export interface UpsertAvailabilityRequest {
  startMinuteOfDay: number;
  endMinuteOfDay: number;
}

export interface BulkUpsertDriversRequest {
  drivers: BulkDriverDto[];
  availabilities: BulkAvailabilityDto[];
}

export interface BulkDriverDto {
  toolId?: string; // Guid as string
  erpId: number;
  name: string;
  startLocationLabel?: string;
  startAddress?: string;
  startLatitude: number | null;
  startLongitude: number | null;
  defaultServiceMinutes?: number;
  maxWorkMinutesPerDay?: number;
  isActive?: boolean;
}

export interface BulkAvailabilityDto {
  driverToolId?: string; // Guid as string
  driverErpId?: number;
  date: string; // yyyy-MM-dd
  startMinuteOfDay: number;
  endMinuteOfDay: number;
}

export interface BulkUpsertResultDto {
  driversCreated: number;
  driversUpdated: number;
  availabilitiesUpserted: number;
  errors: BulkErrorDto[];
}

export interface DriverServiceTypesBulkItem {
  email?: string;
  driverToolId?: string;
  driverErpId?: number;
  serviceTypeIds?: string;
}

export interface DriverServiceTypesBulkRequest {
  drivers: DriverServiceTypesBulkItem[];
}

export interface DriverServiceTypesBulkFailedItem {
  email?: string;
  serviceTypeIds?: string;
  rowRef?: string;
  message?: string;
}

export interface DriverServiceTypesBulkResult {
  updated: number;
  errors: BulkErrorDto[];
  failedItems: DriverServiceTypesBulkFailedItem[];
}

export interface DriverServiceTypesBulkExportResponse {
  generatedAtUtc: string;
  drivers: DriverServiceTypesBulkItem[];
}

export interface AvailabilityBulkUpsertResultDto {
  inserted: number;
  updated: number;
  deleted: number;
  errors: BulkErrorDto[];
  conflicts: AvailabilityBulkConflictDto[];
}

export interface AvailabilityBulkConflictDto {
  email?: string;
  driverName?: string;
  date?: string;
  existingStartMinuteOfDay?: number;
  existingEndMinuteOfDay?: number;
  newStartMinuteOfDay?: number;
  newEndMinuteOfDay?: number;
  rowRef?: string;
  reason?: string;
}

export interface BulkErrorDto {
  scope: string; // "Driver" | "Availability"
  rowRef: string;
  message: string;
}
