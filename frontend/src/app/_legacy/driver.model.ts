export interface DriverDto {
  id: number;
  name: string;
  startLatitude: number;
  startLongitude: number;
  defaultServiceMinutes: number;
  maxWorkMinutesPerDay: number;
  imageUrl?: string;
}

export interface DriverAvailabilityDto {
  id: number;
  driverId: number;
  date: string; // ISO date string
  startTime: string; // ISO time string
  endTime: string; // ISO time string
}

export interface UpdateDriverMaxWorkMinutesRequest {
  maxWorkMinutesPerDay: number;
}

