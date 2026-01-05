import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import type { PagedResultDto } from '@models/paging.model';
import type { PoleListItemDto, SetFixedDateRequest } from '@models/pole.model';
import { PolesApiService } from '@services/poles-api.service';
import { addDaysYmd, todayYmd, toYmd } from '@utils/date.utils';
import { MessageService } from 'primeng/api';
import { ButtonModule } from 'primeng/button';
import { CalendarModule } from 'primeng/calendar';
import { CheckboxModule } from 'primeng/checkbox';
import { InputTextModule } from 'primeng/inputtext';
import { MessageModule } from 'primeng/message';
import { PaginatorModule } from 'primeng/paginator';
import { TableModule } from 'primeng/table';
import { catchError, of } from 'rxjs';

@Component({
  selector: 'app-pole-list',
  imports: [
    ButtonModule,
    InputTextModule,
    TableModule,
    PaginatorModule,
    SelectModule,
    CheckboxModule,
    CalendarModule,
    MessageModule,
    FormsModule,
  ],
  providers: [MessageService],
  templateUrl: './pole-list.page.html',
  styleUrl: './pole-list.page.css',
  standalone: true,
})
export class PoleListPage {
  private readonly polesApi = inject(PolesApiService);
  private readonly messageService = inject(MessageService);

  poles = signal<PagedResultDto<PoleListItemDto> | null>(null);
  loading = signal(false);
  error = signal<string | null>(null);

  search = signal<string>('');
  status = signal<string | undefined>(undefined);
  hasFixedDate = signal<boolean | undefined>(undefined);
  fromDate = signal<Date | undefined>(undefined);
  toDate = signal<Date | undefined>(undefined);

  page = signal(1);
  pageSize = signal(50);

  statusOptions = [
    { label: 'All', value: undefined },
    { label: 'New', value: 'New' },
    { label: 'Planned', value: 'Planned' },
    { label: 'InProgress', value: 'InProgress' },
    { label: 'Done', value: 'Done' },
    { label: 'Cancelled', value: 'Cancelled' },
  ];

  constructor() {
    this.loadPoles();
  }

  loadPoles(): void {
    this.loading.set(true);
    this.error.set(null);

    const filters = {
      search: this.search() || undefined,
      status: this.status() || undefined,
      hasFixedDate: this.hasFixedDate(),
      from: this.fromDate() ? toYmd(this.fromDate()!) : undefined,
      to: this.toDate() ? toYmd(this.toDate()!) : undefined,
      page: this.page(),
      pageSize: this.pageSize(),
    };

    this.polesApi
      .getPoles(filters)
      .pipe(
        catchError((err) => {
          this.error.set(err.title || err.message || 'Failed to load poles');
          this.messageService.add({
            severity: 'error',
            summary: 'Error',
            detail: err.detail || err.title || err.message || 'Failed to load poles',
          });
          return of(null);
        }),
      )
      .subscribe((data) => {
        this.poles.set(data);
        this.loading.set(false);
      });
  }

  onPageChange(event: { page?: number; first?: number; rows?: number }): void {
    const rows = event.rows ?? this.pageSize();
    const pageNum = event.page ?? (event.first !== undefined ? Math.floor(event.first / rows) : 0);
    this.page.set(pageNum + 1);
    this.pageSize.set(rows);
    this.loadPoles();
  }

  applyFilters(): void {
    this.page.set(1);
    this.loadPoles();
  }

  fixDate(poleId: number): void {
    const tomorrow = addDaysYmd(todayYmd(), 1);
    const request: SetFixedDateRequest = {
      fixedDate: tomorrow,
    };

    this.polesApi
      .fixDate(poleId, request)
      .pipe(
        catchError((err) => {
          this.messageService.add({
            severity: 'error',
            summary: 'Error',
            detail: err.detail || err.title || err.message || 'Failed to fix date',
          });
          return of(null);
        }),
      )
      .subscribe(() => {
        this.loadPoles();
      });
  }

  unfixDate(poleId: number): void {
    this.polesApi
      .unfixDate(poleId)
      .pipe(
        catchError((err) => {
          this.messageService.add({
            severity: 'error',
            summary: 'Error',
            detail: err.detail || err.title || err.message || 'Failed to unfix date',
          });
          return of(null);
        }),
      )
      .subscribe(() => {
        this.loadPoles();
      });
  }
}
