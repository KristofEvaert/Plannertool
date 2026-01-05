export interface ImportPolesResultDto {
  imported: number;
  updated: number;
  total: number;
  fromDueDate: string; // ISO date string
  toDueDate: string; // ISO date string
}
