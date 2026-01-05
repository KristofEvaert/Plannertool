export interface PoleListItemDto {
  poleId: number;
  serial: string;
  latitude: number;
  longitude: number;
  dueDate: string; // ISO date string
  fixedDate?: string; // ISO date string
  serviceMinutes: number;
  status: string;
}

export interface SetFixedDateRequest {
  fixedDate: string; // ISO date string
}
