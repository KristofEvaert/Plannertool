import { CommonModule } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { HelpManualComponent } from '@components/help-manual/help-manual.component';
import type {
  TravelTimeModelLearnedStatDto,
  TravelTimeModelStatus,
} from '@models';
import { TravelTimeModelAdminApiService } from '@services/travel-time-model-admin-api.service';
import { MessageService } from 'primeng/api';
import { ButtonModule } from 'primeng/button';
import { CheckboxModule } from 'primeng/checkbox';
import { SelectModule } from 'primeng/select';
import { TagModule } from 'primeng/tag';
import { ToastModule } from 'primeng/toast';
import { TooltipModule } from 'primeng/tooltip';

interface SelectOption<T> {
  label: string;
  value: T;
}

@Component({
  selector: 'app-travel-time-model-admin-page',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    SelectModule,
    ButtonModule,
    TagModule,
    TooltipModule,
    CheckboxModule,
    ToastModule,
    HelpManualComponent,
  ],
  providers: [MessageService],
  templateUrl: './travel-time-model-admin.page.html',
  styleUrl: './travel-time-model-admin.page.css',
})
export class TravelTimeModelAdminPage {
  private readonly api = inject(TravelTimeModelAdminApiService);
  private readonly messageService = inject(MessageService);

  rows = signal<TravelTimeModelLearnedStatDto[]>([]);
  loading = signal(false);

  selectedRegionId = signal<number | null>(null);
  selectedStatus = signal<TravelTimeModelStatus | null>(null);
  selectedDayType = signal<string | null>(null);
  selectedDistanceBand = signal<string | null>(null);
  showFlaggedOnly = signal(false);

  expandedRows = signal<Record<number, boolean>>({});

  regionOptions = computed(() => this.buildRegionOptions());
  statusOptions = computed(() => this.buildStatusOptions());
  dayTypeOptions = computed(() => this.buildDayTypeOptions());
  distanceBandOptions = computed(() => this.buildDistanceBandOptions());

  filteredRows = computed(() => {
    const regionId = this.selectedRegionId();
    const status = this.selectedStatus();
    const dayType = this.selectedDayType();
    const distanceBand = this.selectedDistanceBand();
    const flaggedOnly = this.showFlaggedOnly();

    return this.rows().filter((row) => {
      if (regionId != null && row.regionId !== regionId) return false;
      if (status && row.status !== status) return false;
      if (dayType && row.dayType !== dayType) return false;
      if (distanceBand && this.distanceBandKey(row) !== distanceBand) return false;
      if (flaggedOnly && !this.isFlagged(row)) return false;
      return true;
    });
  });

  constructor() {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.api.getLearned().subscribe({
      next: (rows) => {
        this.loading.set(false);
        this.rows.set(rows);
      },
      error: (err) => {
        this.loading.set(false);
        this.messageService.add({
          severity: 'error',
          summary: 'Error',
          detail: err?.error?.message || err?.message || 'Failed to load learned travel stats',
        });
      },
    });
  }

  applyStatus(row: TravelTimeModelLearnedStatDto, status: TravelTimeModelStatus): void {
    this.api.updateStatus(row.id, status).subscribe({
      next: () => {
        this.messageService.add({ severity: 'success', summary: `Status set to ${status}` });
        this.load();
      },
      error: (err) => {
        this.messageService.add({
          severity: 'error',
          summary: 'Error',
          detail: err?.error?.message || err?.message || 'Failed to update status',
        });
      },
    });
  }

  resetBucket(row: TravelTimeModelLearnedStatDto): void {
    if (
      !confirm(
        `Reset learned bucket for ${row.regionName} ${row.dayType} ${this.formatBucket(row)}?`,
      )
    )
      return;
    this.api.resetBucket(row.id).subscribe({
      next: () => {
        this.messageService.add({ severity: 'success', summary: 'Bucket reset' });
        this.load();
      },
      error: (err) => {
        this.messageService.add({
          severity: 'error',
          summary: 'Error',
          detail: err?.error?.message || err?.message || 'Failed to reset bucket',
        });
      },
    });
  }

  toggleRow(row: TravelTimeModelLearnedStatDto): void {
    this.expandedRows.update((current) => {
      const next = { ...current };
      if (next[row.id]) {
        delete next[row.id];
      } else {
        next[row.id] = true;
      }
      return next;
    });
  }

  isRowExpanded(row: TravelTimeModelLearnedStatDto): boolean {
    return !!this.expandedRows()[row.id];
  }

  formatBucket(row: TravelTimeModelLearnedStatDto): string {
    return `${row.bucketStartHour}:00-${row.bucketEndHour}:00`;
  }

  formatDistanceBand(row: TravelTimeModelLearnedStatDto): string {
    return `${row.distanceBandKmMin}-${row.distanceBandKmMax} km`;
  }

  distanceBandKey(row: TravelTimeModelLearnedStatDto): string {
    return `${row.distanceBandKmMin}-${row.distanceBandKmMax}`;
  }

  formatDeviation(row: TravelTimeModelLearnedStatDto): string {
    if (row.deviationPercent == null) return '—';
    const sign = row.deviationPercent >= 0 ? '+' : '';
    return `${sign}${row.deviationPercent.toFixed(1)}%`;
  }

  formatRatio(row: TravelTimeModelLearnedStatDto): string {
    return `${(row.suspiciousRatio * 100).toFixed(1)}%`;
  }

  isFlagged(row: TravelTimeModelLearnedStatDto): boolean {
    return (
      row.isOutOfRange ||
      row.isStale ||
      row.isLowSample ||
      row.isHighDeviation ||
      row.suspiciousRatio > 0
    );
  }

  statusSeverity(
    status: string,
  ): 'success' | 'secondary' | 'info' | 'warn' | 'danger' | 'contrast' | undefined | null {
    switch (status) {
      case 'Approved':
        return 'success';
      case 'Quarantined':
        return 'warn';
      case 'Rejected':
        return 'danger';
      default:
        return 'secondary';
    }
  }

  private buildRegionOptions(): SelectOption<number>[] {
    const seen = new Map<number, string>();
    for (const row of this.rows()) {
      if (!seen.has(row.regionId)) {
        seen.set(row.regionId, row.regionName);
      }
    }
    return Array.from(seen.entries())
      .map(([value, label]) => ({ value, label }))
      .sort((a, b) => a.label.localeCompare(b.label));
  }

  private buildStatusOptions(): SelectOption<TravelTimeModelStatus>[] {
    const statuses: TravelTimeModelStatus[] = ['Draft', 'Approved', 'Quarantined', 'Rejected'];
    return statuses.map((status) => ({ label: status, value: status }));
  }

  private buildDayTypeOptions(): SelectOption<string>[] {
    const types = Array.from(new Set(this.rows().map((row) => row.dayType)));
    return types.map((type) => ({ label: type, value: type }));
  }

  private buildDistanceBandOptions(): SelectOption<string>[] {
    const bands = Array.from(new Set(this.rows().map((row) => this.distanceBandKey(row))));
    return bands
      .map((band) => ({ label: `${band} km`, value: band }))
      .sort((a, b) => {
        const aStart = Number(a.value.split('-')[0]);
        const bStart = Number(b.value.split('-')[0]);
        return aStart - bStart;
      });
  }
}
