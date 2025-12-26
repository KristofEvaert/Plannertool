export interface ServiceTypeDto {
  id: number;
  code: string;
  name: string;
  description?: string;
  isActive: boolean;
}

export interface CreateServiceTypeRequest {
  code: string;
  name: string;
  description?: string;
}

export interface UpdateServiceTypeRequest {
  code: string;
  name: string;
  description?: string;
  isActive?: boolean;
}

