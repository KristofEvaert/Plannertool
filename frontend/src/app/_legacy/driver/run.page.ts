import { Component, effect, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import type { DriverDayDto } from '@models/plan.model';
import type { AddStopNoteRequest } from '@models/routes.model';
import { PlanApiService } from '@services/plan-api.service';
import { RoutesApiService } from '@services/routes-api.service';
import { MessageService } from 'primeng/api';
import { ButtonModule } from 'primeng/button';
import { MessageModule } from 'primeng/message';
import { TextareaModule } from 'primeng/textarea';
import { catchError, of } from 'rxjs';

@Component({
  selector: 'app-driver-run',
  imports: [ButtonModule, TextareaModule, MessageModule, FormsModule],
  providers: [MessageService],
  templateUrl: './run.page.html',
  styleUrl: './run.page.css',
  standalone: true,
})
export class DriverRunPage {
  date = input.required<string>();
  driverId = input.required<number>();

  private readonly planApi = inject(PlanApiService);
  private readonly routesApi = inject(RoutesApiService);
  private readonly messageService = inject(MessageService);
  private readonly router = inject(Router);

  driverDay = signal<DriverDayDto | null>(null);
  loading = signal(false);
  error = signal<string | null>(null);
  stopNotes = signal<Map<number, string>>(new Map());

  constructor() {
    effect(() => {
      const dateValue = this.date();
      const driverIdValue = this.driverId();
      if (dateValue && driverIdValue) {
        this.loadDriverDay(dateValue, driverIdValue);
      }
    });
  }

  loadDriverDay(date: string, driverId: number): void {
    this.loading.set(true);
    this.error.set(null);

    this.planApi
      .getDriverDay(date, driverId)
      .pipe(
        catchError((err) => {
          this.error.set(err.title || err.message || 'Failed to load driver day');
          this.messageService.add({
            severity: 'error',
            summary: 'Error',
            detail: err.detail || err.title || err.message || 'Failed to load driver day',
          });
          return of(null);
        }),
      )
      .subscribe((data) => {
        this.driverDay.set(data);
        this.loading.set(false);
      });
  }

  startRoute(): void {
    const day = this.driverDay();
    if (!day?.routeId) {
      this.messageService.add({
        severity: 'error',
        summary: 'Error',
        detail: 'No route available',
      });
      return;
    }

    this.loading.set(true);
    this.routesApi
      .startRoute(day.routeId)
      .pipe(
        catchError((err) => {
          this.messageService.add({
            severity: 'error',
            summary: 'Error',
            detail: err.detail || err.title || err.message || 'Failed to start route',
          });
          return of(null);
        }),
      )
      .subscribe(() => {
        this.loading.set(false);
        this.loadDriverDay(this.date(), this.driverId());
      });
  }

  arriveStop(stopId: number): void {
    const day = this.driverDay();
    if (!day?.routeId) {
      return;
    }

    this.routesApi
      .arriveStop(day.routeId, stopId)
      .pipe(
        catchError((err) => {
          this.messageService.add({
            severity: 'error',
            summary: 'Error',
            detail: err.detail || err.title || err.message || 'Failed to mark arrival',
          });
          return of(null);
        }),
      )
      .subscribe(() => {
        this.loadDriverDay(this.date(), this.driverId());
      });
  }

  completeStop(stopId: number): void {
    const day = this.driverDay();
    if (!day?.routeId) {
      return;
    }

    this.routesApi
      .completeStop(day.routeId, stopId)
      .pipe(
        catchError((err) => {
          this.messageService.add({
            severity: 'error',
            summary: 'Error',
            detail: err.detail || err.title || err.message || 'Failed to complete stop',
          });
          return of(null);
        }),
      )
      .subscribe(() => {
        this.loadDriverDay(this.date(), this.driverId());
      });
  }

  addNote(stopId: number): void {
    const day = this.driverDay();
    const note = this.stopNotes().get(stopId);
    if (!day?.routeId || !note) {
      return;
    }

    const request: AddStopNoteRequest = { note };
    this.routesApi
      .addStopNote(day.routeId, stopId, request)
      .pipe(
        catchError((err) => {
          this.messageService.add({
            severity: 'error',
            summary: 'Error',
            detail: err.detail || err.title || err.message || 'Failed to add note',
          });
          return of(null);
        }),
      )
      .subscribe(() => {
        const notes = new Map(this.stopNotes());
        notes.set(stopId, '');
        this.stopNotes.set(notes);
        this.loadDriverDay(this.date(), this.driverId());
      });
  }

  getNote(stopId: number): string {
    return this.stopNotes().get(stopId) || '';
  }

  setNote(stopId: number, note: string): void {
    const notes = new Map(this.stopNotes());
    notes.set(stopId, note);
    this.stopNotes.set(notes);
  }
}
