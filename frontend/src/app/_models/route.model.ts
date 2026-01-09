export interface RouteStopDto {
  id: number;
  sequence: number;
  serviceLocationId?: number;
  serviceLocationToolId?: string; // Use ToolId (Guid as string) for matching
  name?: string;
  latitude: number;
  longitude: number;
  serviceMinutes: number;
  actualServiceMinutes?: number;
  actualArrivalUtc?: string;
  actualDepartureUtc?: string;
  travelKmFromPrev: number;
  travelMinutesFromPrev: number;
  status?: string;
  arrivedAtUtc?: string;
  completedAtUtc?: string;
  note?: string;
  driverNote?: string;
  issueCode?: string;
  followUpRequired?: boolean;
  checklistItems?: RouteStopChecklistItemDto[];
  proofStatus?: string;
  hasProofPhoto?: boolean;
  hasProofSignature?: boolean;
  lastUpdatedByUserId?: string;
  lastUpdatedUtc?: string;
  driverInstruction?: string;
  remark?: string;
}

export interface RouteStopChecklistItemDto {
  text: string;
  isChecked: boolean;
}

export interface RouteGeometryPointDto {
  lat: number;
  lng: number;
}

export interface RouteDto {
  id: number;
  date: string;
  ownerId: number;
  serviceTypeId: number;
  driverId: number;
  driverName: string;
  driverStartLatitude?: number;
  driverStartLongitude?: number;
  startAddress?: string;
  startLatitude?: number;
  startLongitude?: number;
  endAddress?: string;
  endLatitude?: number;
  endLongitude?: number;
  weightTemplateId?: number;
  totalMinutes: number;
  totalKm: number;
  status: string;
  stops: RouteStopDto[];
  geometry?: RouteGeometryPointDto[];
}

export interface CreateRouteStopRequest {
  sequence: number;
  serviceLocationToolId?: string; // Use ToolId (Guid as string) instead of serviceLocationId
  latitude: number;
  longitude: number;
  serviceMinutes: number;
  travelKmFromPrev: number;
  travelMinutesFromPrev: number;
}

export interface CreateRouteRequest {
  date: string; // ISO date string
  ownerId: number;
  // serviceTypeId removed - routes are identified by date, driver, owner only
  driverToolId: string; // Use ToolId (Guid as string) instead of driverId
  totalMinutes: number;
  totalKm: number;
  startAddress?: string;
  startLatitude?: number;
  startLongitude?: number;
  endAddress?: string;
  endLatitude?: number;
  endLongitude?: number;
  weightTemplateId?: number;
  stops: CreateRouteStopRequest[];
}

export interface UpdateRouteStopRequest {
  arrivedAtUtc?: string;
  completedAtUtc?: string;
  actualServiceMinutes?: number;
  note?: string;
  driverNote?: string;
  issueCode?: string;
  followUpRequired?: boolean;
  checklistItems?: RouteStopChecklistItemDto[];
  proofStatus?: string;
  status?: string;
}

export interface AutoGenerateAllResponse {
  routes: RouteDto[];
  skippedDrivers: string[];
}
