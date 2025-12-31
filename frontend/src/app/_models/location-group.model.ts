export interface LocationGroupDto {
  id: number;
  name: string;
  description?: string | null;
  ownerId?: number | null;
  serviceLocationIds: number[];
}

export interface SaveLocationGroupRequest {
  name: string;
  description?: string | null;
  ownerId?: number | null;
  serviceLocationIds: number[];
}
