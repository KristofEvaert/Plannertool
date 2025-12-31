export interface SystemCostSettingsDto {
  ownerId?: number | null;
  fuelCostPerKm: number;
  personnelCostPerHour: number;
  currencyCode: string;
}


export interface SystemCostSettingsOverviewDto {
  ownerId: number;
  ownerCode: string;
  ownerName: string;
  ownerIsActive: boolean;
  fuelCostPerKm: number;
  personnelCostPerHour: number;
  currencyCode: string;
  updatedAtUtc: string | null;
}
