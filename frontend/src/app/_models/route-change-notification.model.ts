export interface RouteChangeNotificationDto {
  id: number;
  routeId: number;
  routeVersionId: number;
  severity: string;
  createdUtc: string;
  acknowledgedUtc?: string | null;
  changeSummary?: string | null;
}
