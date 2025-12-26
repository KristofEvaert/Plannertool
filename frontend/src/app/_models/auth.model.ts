export interface LoginRequest {
  email: string;
  password: string;
}

export interface LoginResponse {
  token: string;
  expiresAtUtc: string;
  email: string;
  displayName?: string;
  userId: string;
  roles: string[];
  ownerId?: number;
}

export interface CurrentUser {
  id: string;
  email: string;
  displayName?: string;
  roles: string[];
  driverToolId?: string;
  driverOwnerId?: number;
  ownerId?: number;
}
