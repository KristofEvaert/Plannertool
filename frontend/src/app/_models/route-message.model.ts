export interface RouteMessageDto {
  id: number;
  routeId: number;
  routeStopId?: number | null;
  driverId: number;
  driverName?: string | null;
  plannerId?: string | null;
  messageText: string;
  createdUtc: string;
  status: string;
  category: string;
}

export interface CreateRouteMessageRequest {
  routeId: number;
  routeStopId?: number | null;
  messageText: string;
  category: string;
}
