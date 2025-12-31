export interface ServiceTypeDto {
  id: number;
  code: string;
  name: string;
  description?: string;
  isActive: boolean;
  ownerId?: number | null;
  ownerName?: string | null;
}

export interface CreateServiceTypeRequest {
  code: string;
  name: string;
  description?: string;
  ownerId: number;
}

export interface UpdateServiceTypeRequest {
  code: string;
  name: string;
  description?: string;
  isActive?: boolean;
  ownerId: number;
}

