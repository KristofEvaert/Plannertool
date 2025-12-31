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
