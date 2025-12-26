export interface UserDto {
  id: string;
  email: string;
  displayName?: string;
  roles: string[];
  driverToolId?: string;
  driverOwnerId?: number;
  ownerId?: number;
  driverStartAddress?: string;
  driverStartLatitude?: number;
  driverStartLongitude?: number;
}

export interface CreateUserRequest {
  email: string;
  password: string;
  displayName?: string;
}

export interface AssignRolesRequest {
  userId: string;
  roles: string[];
  ownerIdForDriver?: number;
  ownerIdForStaff?: number;
  displayName?: string;
  driverStartAddress?: string;
  driverStartLatitude?: number | null;
  driverStartLongitude?: number | null;
}
