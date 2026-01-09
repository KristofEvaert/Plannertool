export interface WeightTemplateDto {
  id: number;
  name: string;
  ownerId?: number | null;
  isActive: boolean;
  algorithmType: string;
  dueDatePriority: number;
  worktimeDeviationPercent: number;
}

export interface SaveWeightTemplateRequest {
  name: string;
  ownerId?: number | null;
  isActive: boolean;
  algorithmType: string;
  dueDatePriority: number;
  worktimeDeviationPercent: number;
}
