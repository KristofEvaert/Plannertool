import { Component, computed, inject, signal } from '@angular/core';
import { CommonModule, DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ButtonModule } from 'primeng/button';
import { TableModule } from 'primeng/table';
import { InputTextModule } from 'primeng/inputtext';
import { DropdownModule } from 'primeng/dropdown';
import { CalendarModule } from 'primeng/calendar';
import { InputNumberModule } from 'primeng/inputnumber';
import { ToastModule } from 'primeng/toast';
import { MessageService } from 'primeng/api';
import { AuditTrailApiService } from '@services/audit-trail-api.service';
import { ServiceLocationOwnersApiService, type ServiceLocationOwnerDto } from '@services/service-location-owners-api.service';
import type { AuditTrailEntryDto } from '@models/audit-trail.model';
import { catchError, of } from 'rxjs';
import { HelpManualComponent } from '@components/help-manual/help-manual.component';

@Component({
  selector: 'app-audit-trail',
  imports: [
    CommonModule,
    FormsModule,
    ButtonModule,
    TableModule,
    InputTextModule,
    DropdownModule,
    CalendarModule,
    InputNumberModule,
    ToastModule,
    HelpManualComponent,
  ],
  providers: [MessageService, DatePipe],
  templateUrl: './audit-trail.page.html',
  standalone: true,
})
export class AuditTrailPage {
  private readonly api = inject(AuditTrailApiService);
  private readonly messageService = inject(MessageService);
  private readonly datePipe = inject(DatePipe);
  private readonly ownersApi = inject(ServiceLocationOwnersApiService);

  items = signal<AuditTrailEntryDto[]>([]);
  totalCount = signal(0);
  loading = signal(false);
  page = signal(1);
  pageSize = signal(50);
  owners = signal<ServiceLocationOwnerDto[]>([]);

  fromUtc = signal<Date | null>(null);
  toUtc = signal<Date | null>(null);
  method = signal<string | null>(null);
  statusCode = signal<number | null>(null);
  pathContains = signal('');
  userEmailContains = signal('');
  ownerId = signal<number | null>(null);
  search = signal('');

  methodOptions = [
    { label: 'All', value: null },
    { label: 'GET', value: 'GET' },
    { label: 'POST', value: 'POST' },
    { label: 'PUT', value: 'PUT' },
    { label: 'PATCH', value: 'PATCH' },
    { label: 'DELETE', value: 'DELETE' },
  ];

  ownerOptions = computed(() => [
    { label: 'All', value: null },
    ...this.owners().map((o) => ({ label: o.name, value: o.id })),
  ]);

  totalPages = computed(() => {
    const size = this.pageSize();
    return size > 0 ? Math.ceil(this.totalCount() / size) : 0;
  });

  private filterTimer: ReturnType<typeof setTimeout> | null = null;

  constructor() {
    const now = new Date();
    const from = new Date(now);
    from.setDate(from.getDate() - 7);
    from.setHours(0, 0, 0, 0);
    this.fromUtc.set(from);
    this.toUtc.set(now);

    this.loadOwners();
    this.loadData();
  }

  onSearchChange(value: string): void {
    this.search.set(value);
    this.scheduleFilter();
  }

  onPathChange(value: string): void {
    this.pathContains.set(value);
    this.scheduleFilter();
  }

  onUserEmailChange(value: string): void {
    this.userEmailContains.set(value);
    this.scheduleFilter();
  }

  loadData(resetPage = true): void {
    if (resetPage) {
      this.page.set(1);
    }

    this.loading.set(true);
    this.api
      .getList({
        fromUtc: this.formatUtc(this.fromUtc()),
        toUtc: this.formatUtc(this.toUtc(), true),
        method: this.method() || undefined,
        statusCode: this.statusCode() ?? undefined,
        pathContains: this.pathContains().trim() || undefined,
        userEmailContains: this.userEmailContains().trim() || undefined,
        ownerId: this.ownerId() ?? undefined,
        search: this.search().trim() || undefined,
        page: this.page(),
        pageSize: this.pageSize(),
      })
      .pipe(
        catchError((err) => {
          this.loading.set(false);
          this.messageService.add({
            severity: 'error',
            summary: 'Error',
            detail: err.detail || err.message || 'Failed to load audit trail',
          });
          return of({ items: [], page: 1, pageSize: this.pageSize(), totalCount: 0, totalPages: 0 });
        })
      )
      .subscribe((result) => {
        this.loading.set(false);
        this.items.set([...result.items]);
        this.totalCount.set(result.totalCount);
      });
  }

  onPageChange(event: any): void {
    if (this.items().length === 0 && this.totalCount() === 0) {
      return;
    }
    const nextRows = event?.rows ?? this.pageSize();
    const nextPage = event?.page != null
      ? event.page + 1
      : Math.floor((event?.first ?? 0) / nextRows) + 1;
    this.page.set(nextPage);
    this.pageSize.set(nextRows);
    this.loadData(false);
  }

  clearFilters(): void {
    this.method.set(null);
    this.statusCode.set(null);
    this.pathContains.set('');
    this.userEmailContains.set('');
    this.ownerId.set(null);
    this.search.set('');
    this.loadData();
  }

  formatTimestamp(value: string): string {
    return this.datePipe.transform(value, 'yyyy-MM-dd HH:mm:ss') ?? value;
  }

  getOwnerName(ownerId?: number | null): string {
    if (!ownerId) return '-';
    return this.owners().find((o) => o.id === ownerId)?.name ?? ownerId.toString();
  }

  private formatUtc(value: Date | null, endOfDay = false): string | undefined {
    if (!value) return undefined;
    const copy = new Date(value);
    if (endOfDay) {
      copy.setHours(23, 59, 59, 999);
    }
    return copy.toISOString();
  }

  private scheduleFilter(): void {
    if (this.filterTimer) {
      clearTimeout(this.filterTimer);
    }

    const searchValue = this.search().trim();
    const pathValue = this.pathContains().trim();
    const userValue = this.userEmailContains().trim();
    const minLength = 2;

    const shouldSearch = (value: string) => value.length >= minLength || value.length === 0;

    if (!shouldSearch(searchValue) || !shouldSearch(pathValue) || !shouldSearch(userValue)) {
      return;
    }

    this.filterTimer = setTimeout(() => {
      this.loadData();
    }, 350);
  }

  private loadOwners(): void {
    this.ownersApi
      .getAll(true)
      .pipe(
        catchError(() => {
          return of([]);
        })
      )
      .subscribe((owners) => {
        this.owners.set(owners);
      });
  }
}
