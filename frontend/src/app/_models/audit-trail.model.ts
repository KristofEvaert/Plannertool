export interface AuditTrailEntryDto {
  timestampUtc: string;
  method: string;
  path: string;
  query?: string;
  statusCode: number;
  durationMs: number;
  userId?: string;
  userEmail?: string;
  userName?: string;
  roles: string[];
  ownerId?: number;
  ipAddress?: string;
  userAgent?: string;
  endpoint?: string;
  requestHeaders?: string;
  body?: string;
  bodyTruncated: boolean;
  responseBody?: string;
  responseBodyTruncated: boolean;
  traceId?: string;
}

export interface AuditTrailQueryParams {
  fromUtc?: string;
  toUtc?: string;
  method?: string;
  pathContains?: string;
  userEmailContains?: string;
  userId?: string;
  statusCode?: number;
  ownerId?: number;
  search?: string;
  page?: number;
  pageSize?: number;
}
