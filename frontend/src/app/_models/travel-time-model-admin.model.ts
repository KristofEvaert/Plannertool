export interface TravelTimeModelContributorDto {
  driverId: number;
  driverName: string;
  sampleCount: number;
  lastContributionUtc?: string | null;
}

export interface TravelTimeModelLearnedStatDto {
  id: number;
  regionId: number;
  regionName: string;
  dayType: string;
  bucketStartHour: number;
  bucketEndHour: number;
  distanceBandKmMin: number;
  distanceBandKmMax: number;
  status: string;
  totalSampleCount: number;
  avgMinutesPerKm?: number | null;
  minMinutesPerKm?: number | null;
  maxMinutesPerKm?: number | null;
  lastSampleAtUtc?: string | null;
  baselineMinutesPerKm: number;
  expectedRangeMin: number;
  expectedRangeMax: number;
  deviationPercent?: number | null;
  isOutOfRange: boolean;
  isStale: boolean;
  isLowSample: boolean;
  isHighDeviation: boolean;
  suspiciousRatio: number;
  contributors: TravelTimeModelContributorDto[];
}

export type TravelTimeModelStatus = 'Draft' | 'Approved' | 'Quarantined' | 'Rejected';
