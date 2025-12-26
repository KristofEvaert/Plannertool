export interface RouteActionResultDto {
  routeId: number;
  status: string;
  startedAt?: string; // ISO date-time string
  completedAt?: string; // ISO date-time string
}

export interface StopActionResultDto {
  routeId: number;
  stopId: number;
  status: string;
  arrivedAt?: string; // ISO date-time string
  completedAt?: string; // ISO date-time string
  note?: string;
}

export interface AddStopNoteRequest {
  note: string;
}

