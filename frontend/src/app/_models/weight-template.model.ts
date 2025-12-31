export interface WeightTemplateDto {
  id: number;
  name: string;
  scopeType: string;
  ownerId?: number | null;
  serviceTypeId?: number | null;
  isActive: boolean;
  weightDistance: number;
  weightTravelTime: number;
  weightOvertime: number;
  weightCost: number;
  weightDate: number;
  serviceLocationIds: number[];
}

export interface SaveWeightTemplateRequest {
  name: string;
  scopeType: string;
  ownerId?: number | null;
  serviceTypeId?: number | null;
  isActive: boolean;
  weightDistance: number;
  weightTravelTime: number;
  weightOvertime: number;
  weightCost: number;
  weightDate: number;
  serviceLocationIds: number[];
}
