export interface ServiceLocationDto {
  id: number;
  toolId: string; // Guid as string
  erpId: number;
  name: string;
  address?: string;
  latitude: number;
  longitude: number;
  dueDate: string; // ISO date string (date-only)
  priorityDate?: string; // ISO date string (date-only), optional
  serviceMinutes: number;
  serviceTypeId: number; // FK to ServiceTypes
  serviceTypeName?: string; // Convenience field from backend
  ownerId: number; // FK to ServiceLocationOwners
  ownerName?: string; // Convenience field from backend
  driverInstruction?: string;
  status: 'Open' | 'Done' | 'Cancelled' | 'Planned' | 'NotVisited';
  isActive: boolean;
  remark?: string;
}

export interface CreateServiceLocationRequest {
  erpId: number;
  name: string;
  address?: string;
  latitude: number | null;
  longitude: number | null;
  dueDate: string; // ISO date string (date-only)
  priorityDate?: string; // ISO date string (date-only), optional
  serviceMinutes?: number;
  serviceTypeId: number; // Required FK to ServiceTypes
  ownerId: number; // Required FK to ServiceLocationOwners
  driverInstruction?: string;
}

export interface UpdateServiceLocationRequest {
  erpId: number;
  name: string;
  address?: string;
  latitude: number | null;
  longitude: number | null;
  dueDate: string; // ISO date string (date-only)
  priorityDate?: string; // ISO date string (date-only), optional
  serviceMinutes?: number;
  serviceTypeId: number; // Required FK to ServiceTypes
  ownerId: number; // Required FK to ServiceLocationOwners
  driverInstruction?: string;
}

export interface SetPriorityDateRequest {
  priorityDate?: string; // ISO date string (date-only), null to clear
}

export interface PagedResult<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
}

export interface ServiceLocationListParams {
  status?: 'Open' | 'Done' | 'Cancelled' | 'Planned' | 'NotVisited';
  search?: string;
  fromDue?: string; // yyyy-MM-dd
  toDue?: string; // yyyy-MM-dd
  serviceTypeId?: number; // Filter by ServiceType
  ownerId?: number; // Filter by Owner
  page?: number;
  pageSize?: number;
  order?: 'priorityThenDue' | 'dueDate';
}

// Bulk insert types
export interface BulkServiceLocationInsertDto {
  erpId: number;
  name: string;
  address?: string;
  latitude: number | null;
  longitude: number | null;
  dueDate: string; // yyyy-MM-dd
  priorityDate?: string; // yyyy-MM-dd, optional
  serviceMinutes?: number;
  driverInstruction?: string;
}

export interface BulkInsertServiceLocationsRequest {
  serviceTypeId: number; // Required - applies to all items
  ownerId: number; // Required - applies to all items
  items: BulkServiceLocationInsertDto[];
}

export interface BulkErrorDto {
  rowRef: string;
  message: string;
}

export interface BulkInsertResultDto {
  inserted: number;
  updated: number;
  skipped: number;
  errors: BulkErrorDto[];
}

export interface ServiceLocationOpeningHoursDto {
  id?: number;
  dayOfWeek: number;
  openTime?: string | null;
  closeTime?: string | null;
  isClosed: boolean;
}

export interface ServiceLocationExceptionDto {
  id?: number;
  date: string;
  openTime?: string | null;
  closeTime?: string | null;
  isClosed: boolean;
  note?: string;
}

export interface ServiceLocationConstraintDto {
  minVisitDurationMinutes?: number | null;
  maxVisitDurationMinutes?: number | null;
}

