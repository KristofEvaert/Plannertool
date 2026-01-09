import { CommonModule, DatePipe } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { HelpManualComponent, JsonViewerComponent } from '@components';
import type { AuditTrailEntryDto } from '@models';
import { AuditTrailApiService } from '@services/audit-trail-api.service';
import { MessageService } from 'primeng/api';
import { AutoCompleteModule } from 'primeng/autocomplete';
import { ButtonModule } from 'primeng/button';
import { ChipModule } from 'primeng/chip';
import { DatePickerModule } from 'primeng/datepicker';
import { InputTextModule } from 'primeng/inputtext';
import { MultiSelectModule } from 'primeng/multiselect';
import { SelectModule } from 'primeng/select';
import { TableModule } from 'primeng/table';
import { TabsModule } from 'primeng/tabs';
import { TagModule } from 'primeng/tag';
import { ToastModule } from 'primeng/toast';
import { TooltipModule } from 'primeng/tooltip';
import { catchError, of } from 'rxjs';

type AuditTrailRow = AuditTrailEntryDto & { rowId: string };
type StatusFilter = 'all' | '2xx' | '4xx' | '5xx' | string;

@Component({
  selector: 'app-audit-trail',
  imports: [
    CommonModule,
    FormsModule,
    ButtonModule,
    TableModule,
    InputTextModule,
    SelectModule,
    DatePickerModule,
    ToastModule,
    MultiSelectModule,
    AutoCompleteModule,
    TagModule,
    TooltipModule,
    TabsModule,
    ChipModule,
    HelpManualComponent,
    JsonViewerComponent,
  ],
  providers: [MessageService, DatePipe],
  templateUrl: './audit-trail.page.html',
  styleUrl: './audit-trail.page.css',
})
export class AuditTrailPage {
  private readonly api = inject(AuditTrailApiService);
  private readonly messageService = inject(MessageService);
  private readonly datePipe = inject(DatePipe);

  items = signal<AuditTrailEntryDto[]>([]);
  totalCount = signal(0);
  loading = signal(false);
  page = signal(1);
  pageSize = signal(50);

  fromUtc = signal<Date | null>(null);
  toUtc = signal<Date | null>(null);
  dateRange = signal<Date[] | null>(null);
  methods = signal<string[]>([]);
  statusFilter = signal<StatusFilter>('all');
  search = signal('');
  selectedUser = signal<string | null>(null);
  roleFilters = signal<string[]>([]);

  quickErrorsOnly = signal(false);
  quickSlowOnly = signal(false);
  quickMutatingOnly = signal(false);

  userSuggestions = signal<string[]>([]);

  methodOptions = [
    { label: 'GET', value: 'GET' },
    { label: 'POST', value: 'POST' },
    { label: 'PUT', value: 'PUT' },
    { label: 'PATCH', value: 'PATCH' },
    { label: 'DELETE', value: 'DELETE' },
  ];

  statusOptions = [
    { label: 'All', value: 'all' },
    { label: '2xx', value: '2xx' },
    { label: '4xx', value: '4xx' },
    { label: '5xx', value: '5xx' },
    { label: '200', value: '200' },
    { label: '201', value: '201' },
    { label: '204', value: '204' },
    { label: '400', value: '400' },
    { label: '401', value: '401' },
    { label: '403', value: '403' },
    { label: '404', value: '404' },
    { label: '409', value: '409' },
    { label: '422', value: '422' },
    { label: '500', value: '500' },
    { label: '502', value: '502' },
    { label: '503', value: '503' },
  ];

  roleOptions = computed(() => {
    const roles = new Set<string>();
    for (const item of this.items()) {
      for (const role of item.roles ?? []) {
        roles.add(role);
      }
    }
    return Array.from(roles)
      .sort((a, b) => a.localeCompare(b))
      .map((role) => ({ label: role, value: role }));
  });

  displayedItems = computed<AuditTrailRow[]>(() => {
    const filtered = this.applyLocalFilters(this.items());
    const sorted = this.applySort(filtered);
    return sorted.map((item, index) => ({
      ...item,
      rowId: this.buildRowId(item, index),
    }));
  });

  totalPages = computed(() => {
    const size = this.pageSize();
    return size > 0 ? Math.ceil(this.totalCount() / size) : 0;
  });

  sortField = signal<string>('timestampUtc');
  sortOrder = signal<number>(-1);
  expandedRowKeys = signal<Record<string, boolean>>({});

  constructor() {
    const now = new Date();
    const from = new Date(now);
    from.setDate(from.getDate() - 7);
    from.setHours(0, 0, 0, 0);
    this.fromUtc.set(from);
    this.toUtc.set(now);
    this.dateRange.set([from, now]);

    this.loadData();
  }

  onDateRangeChange(value: Date[] | null): void {
    this.dateRange.set(value);
    if (!value || value.length === 0) {
      this.fromUtc.set(null);
      this.toUtc.set(null);
      return;
    }
    this.fromUtc.set(value[0] ?? null);
    this.toUtc.set(value[1] ?? value[0] ?? null);
  }

  onSearchChange(value: string): void {
    this.search.set(value);
  }

  onUserSearch(event: { query: string }): void {
    const query = event.query.toLowerCase();
    const candidates = new Set<string>();
    for (const item of this.items()) {
      if (item.userEmail) candidates.add(item.userEmail);
      if (item.userName) candidates.add(item.userName);
    }
    const matches = Array.from(candidates).filter((value) => value.toLowerCase().includes(query));
    this.userSuggestions.set(matches.slice(0, 12));
  }

  loadData(resetPage = true): void {
    if (resetPage) {
      this.page.set(1);
    }

    const selectedMethods = this.methods();
    const methodParam = selectedMethods.length === 1 ? selectedMethods[0] : undefined;
    const statusCodeParam = this.getStatusCodeParam(this.statusFilter());

    this.loading.set(true);
    this.api
      .getList({
        fromUtc: this.formatUtc(this.fromUtc()),
        toUtc: this.formatUtc(this.toUtc(), true),
        method: methodParam,
        statusCode: statusCodeParam,
        userEmailContains: this.selectedUser()?.trim() || undefined,
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
          return of({
            items: [],
            page: 1,
            pageSize: this.pageSize(),
            totalCount: 0,
            totalPages: 0,
          });
        }),
      )
      .subscribe((result) => {
        this.loading.set(false);
        this.items.set([...result.items]);
        this.totalCount.set(result.totalCount);
        this.expandedRowKeys.set({});
      });
  }

  onPageChange(event: any): void {
    if (this.items().length === 0 && this.totalCount() === 0) {
      return;
    }
    const nextRows = event?.rows ?? this.pageSize();
    const nextPage =
      event?.page != null ? event.page + 1 : Math.floor((event?.first ?? 0) / nextRows) + 1;
    this.page.set(nextPage);
    this.pageSize.set(nextRows);
    this.loadData(false);
  }

  onSort(event: { field?: string; order?: number }): void {
    if (!event?.field || event.order == null) {
      return;
    }
    if (event.order === 0) {
      this.sortField.set('');
      this.sortOrder.set(1);
      return;
    }
    this.sortField.set(event.field);
    this.sortOrder.set(event.order);
  }

  onRowExpand(event: { data?: AuditTrailRow }): void {
    const rowId = event.data?.rowId;
    if (!rowId) {
      return;
    }
    const next = { ...this.expandedRowKeys() };
    next[rowId] = true;
    this.expandedRowKeys.set(next);
  }

  onRowCollapse(event: { data?: AuditTrailRow }): void {
    const rowId = event.data?.rowId;
    if (!rowId) {
      return;
    }
    const next = { ...this.expandedRowKeys() };
    delete next[rowId];
    this.expandedRowKeys.set(next);
  }

  applyFilters(): void {
    this.loadData();
  }

  clearFilters(): void {
    const now = new Date();
    const from = new Date(now);
    from.setDate(from.getDate() - 7);
    from.setHours(0, 0, 0, 0);
    this.fromUtc.set(from);
    this.toUtc.set(now);
    this.dateRange.set([from, now]);
    this.methods.set([]);
    this.statusFilter.set('all');
    this.search.set('');
    this.selectedUser.set(null);
    this.roleFilters.set([]);
    this.quickErrorsOnly.set(false);
    this.quickSlowOnly.set(false);
    this.quickMutatingOnly.set(false);
    this.loadData();
  }

  refresh(): void {
    this.loadData(false);
  }

  formatTimestamp(value: string): string {
    return this.datePipe.transform(value, 'yyyy-MM-dd HH:mm:ss') ?? value;
  }

  formatRelativeTime(value: string): string {
    const timestamp = new Date(value).getTime();
    if (!Number.isFinite(timestamp)) {
      return '';
    }
    const deltaMs = Date.now() - timestamp;
    const deltaSeconds = Math.floor(deltaMs / 1000);
    if (deltaSeconds < 60) return `${deltaSeconds}s ago`;
    const deltaMinutes = Math.floor(deltaSeconds / 60);
    if (deltaMinutes < 60) return `${deltaMinutes}m ago`;
    const deltaHours = Math.floor(deltaMinutes / 60);
    if (deltaHours < 24) return `${deltaHours}h ago`;
    const deltaDays = Math.floor(deltaHours / 24);
    return `${deltaDays}d ago`;
  }

  getEndpointLabel(item: AuditTrailEntryDto): string {
    return item.path || '-';
  }

  getEndpointTooltip(item: AuditTrailEntryDto): string {
    return `${item.path || ''}${item.query || ''}` || '-';
  }

  getFullEndpoint(item: AuditTrailEntryDto): string {
    return `${item.path || ''}${item.query || ''}` || '-';
  }

  getUserWithRole(item: AuditTrailEntryDto): string {
    const user = item.userName || item.userEmail || '-';
    const roles = item.roles?.length ? item.roles.join(', ') : '';
    return roles ? `${user} (${roles})` : user;
  }

  getQueryChips(item: AuditTrailEntryDto): string[] {
    return this.getQueryParams(item).slice(0, 3);
  }

  getQueryExtraCount(item: AuditTrailEntryDto): number {
    const count = this.getQueryParams(item).length;
    return count > 3 ? count - 3 : 0;
  }

  copyText(value?: string | null): void {
    if (!value) {
      return;
    }
    if (navigator?.clipboard?.writeText) {
      navigator.clipboard.writeText(value);
      return;
    }
  }

  getMethodSeverity(
    method: string,
  ): 'success' | 'secondary' | 'info' | 'warn' | 'danger' | 'contrast' | undefined | null {
    switch (method?.toUpperCase()) {
      case 'GET':
        return 'info';
      case 'POST':
        return 'success';
      case 'PUT':
      case 'PATCH':
        return 'warn';
      case 'DELETE':
        return 'danger';
      default:
        return 'info';
    }
  }

  getStatusSeverity(
    statusCode: number,
  ): 'success' | 'secondary' | 'info' | 'warn' | 'danger' | 'contrast' | undefined | null {
    if (statusCode >= 500) return 'danger';
    if (statusCode >= 400) return 'warn';
    if (statusCode >= 200) return 'success';
    return 'info';
  }

  getPrimaryRole(item: AuditTrailEntryDto): string {
    if (!item.roles || item.roles.length === 0) {
      return '-';
    }
    if (item.roles.length === 1) {
      return item.roles[0];
    }
    return `${item.roles[0]} +${item.roles.length - 1}`;
  }

  isSlow(item: AuditTrailEntryDto): boolean {
    return item.durationMs > 500;
  }

  getTraceDetails(item: AuditTrailEntryDto): unknown | null {
    const details: Record<string, string> = {};
    if (item.traceId) {
      details['traceId'] = item.traceId;
    }
    if (item.userAgent) {
      details['userAgent'] = item.userAgent;
    }
    return Object.keys(details).length > 0 ? details : null;
  }

  hasTraceData(item: AuditTrailEntryDto): boolean {
    return !!item.traceId || !!item.userAgent;
  }

  private getStatusCodeParam(filter: StatusFilter): number | undefined {
    if (!filter || filter === 'all') {
      return undefined;
    }
    const numeric = Number(filter);
    return Number.isFinite(numeric) ? numeric : undefined;
  }

  private applyLocalFilters(items: AuditTrailEntryDto[]): AuditTrailEntryDto[] {
    let filtered = [...items];
    const selectedMethods = this.methods();
    const statusFilter = this.statusFilter();
    const roles = this.roleFilters();

    if (selectedMethods.length > 0) {
      const methodSet = new Set(selectedMethods.map((m) => m.toUpperCase()));
      filtered = filtered.filter((item) => methodSet.has(item.method.toUpperCase()));
    }

    if (statusFilter === '2xx') {
      filtered = filtered.filter((item) => item.statusCode >= 200 && item.statusCode < 300);
    } else if (statusFilter === '4xx') {
      filtered = filtered.filter((item) => item.statusCode >= 400 && item.statusCode < 500);
    } else if (statusFilter === '5xx') {
      filtered = filtered.filter((item) => item.statusCode >= 500);
    } else if (!isNaN(Number(statusFilter)) && statusFilter !== 'all') {
      const code = Number(statusFilter);
      filtered = filtered.filter((item) => item.statusCode === code);
    }

    if (this.selectedUser()) {
      const needle = this.selectedUser()!.toLowerCase();
      filtered = filtered.filter((item) => {
        const email = item.userEmail?.toLowerCase() ?? '';
        const name = item.userName?.toLowerCase() ?? '';
        return email.includes(needle) || name.includes(needle);
      });
    }

    if (roles.length > 0) {
      filtered = filtered.filter((item) => item.roles?.some((role) => roles.includes(role)));
    }

    if (this.quickErrorsOnly()) {
      filtered = filtered.filter((item) => item.statusCode >= 400);
    }

    if (this.quickSlowOnly()) {
      filtered = filtered.filter((item) => item.durationMs > 500);
    }

    if (this.quickMutatingOnly()) {
      filtered = filtered.filter((item) =>
        ['POST', 'PUT', 'PATCH', 'DELETE'].includes(item.method.toUpperCase()),
      );
    }

    return filtered;
  }

  private applySort(items: AuditTrailEntryDto[]): AuditTrailEntryDto[] {
    const field = this.sortField();
    const order = this.sortOrder();
    if (!field || !order) {
      return items;
    }
    const sorted = [...items];
    sorted.sort((a, b) => {
      const aValue = this.getSortableValue(a, field);
      const bValue = this.getSortableValue(b, field);
      if (aValue < bValue) return -1 * order;
      if (aValue > bValue) return 1 * order;
      return 0;
    });
    return sorted;
  }

  private getSortableValue(item: AuditTrailEntryDto, field: string): string | number {
    switch (field) {
      case 'timestampUtc':
        return new Date(item.timestampUtc).getTime() || 0;
      case 'statusCode':
        return item.statusCode ?? 0;
      case 'durationMs':
        return item.durationMs ?? 0;
      case 'method':
        return item.method ?? '';
      case 'path':
        return item.path ?? '';
      case 'userName':
        return item.userName ?? '';
      case 'userEmail':
        return item.userEmail ?? '';
      default:
        return (item as unknown as Record<string, string | number | undefined>)[field] ?? '';
    }
  }

  private buildRowId(item: AuditTrailEntryDto, index: number): string {
    if (item.traceId) {
      return item.traceId;
    }
    return `${item.timestampUtc}-${item.method}-${item.path}-${item.statusCode}-${index}`;
  }

  private getQueryParams(item: AuditTrailEntryDto): string[] {
    const query = item.query ?? '';
    if (!query) {
      return [];
    }
    const raw = query.startsWith('?') ? query.slice(1) : query;
    if (!raw) {
      return [];
    }

    return raw
      .split('&')
      .map((pair) => {
        if (!pair) {
          return null;
        }
        const [key, ...rest] = pair.split('=');
        const value = rest.join('=');
        try {
          const decodedKey = decodeURIComponent(key);
          const decodedValue = value ? decodeURIComponent(value) : '';
          return decodedValue ? `${decodedKey}=${decodedValue}` : decodedKey;
        } catch {
          return value ? `${key}=${value}` : key;
        }
      })
      .filter((entry): entry is string => !!entry);
  }

  private formatUtc(value: Date | null, endOfDay = false): string | undefined {
    if (!value) return undefined;
    const copy = new Date(value);
    if (endOfDay) {
      copy.setHours(23, 59, 59, 999);
    }
    return copy.toISOString();
  }
}
