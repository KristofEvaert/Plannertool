import { Component, computed, effect, inject, input, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { ButtonModule } from 'primeng/button';
import { MessageModule } from 'primeng/message';
import { ToastModule } from 'primeng/toast';
import { MessageService } from 'primeng/api';
import { catchError, of } from 'rxjs';
import { PlanApiService } from '@services/plan-api.service';
import { ImportApiService } from '@services/import-api.service';
import type { DayOverviewDto, GeneratePlanRequest } from '@models/plan.model';
import { addDaysYmd } from '@utils/date.utils';
import {
  LeafletMapComponent,
  type MapMarker,
  type RoutePolyline,
} from '@components/map/leaflet-map.component';

@Component({
  selector: 'app-day-overview',
  imports: [RouterLink, ButtonModule, MessageModule, ToastModule, LeafletMapComponent],
  providers: [MessageService],
  templateUrl: './day-overview.page.html',
  styleUrl: './day-overview.page.css',
  standalone: true,
})
export class DayOverviewPage {
  date = input.required<string>();

  private readonly planApi = inject(PlanApiService);
  private readonly importApi = inject(ImportApiService);
  private readonly messageService = inject(MessageService);
  private readonly router = inject(Router);

  overview = signal<DayOverviewDto | null>(null);
  loading = signal(false);
  error = signal<string | null>(null);
  selectedPoleId = signal<string | undefined>(undefined);
  private currentLoadingDate: string | null = null;
  horizonDays = signal<number>(14); // Default horizon days

  constructor() {
    // Load overview when date changes (only trigger on date change, not on overview change)
    effect(() => {
      const dateValue = this.date();
      if (dateValue && dateValue !== this.currentLoadingDate) {
        // Prevent multiple simultaneous loads for the same date
        this.currentLoadingDate = dateValue;
        // Don't reset overview immediately - keep showing old data while loading
        // Only reset selectedPoleId
        this.selectedPoleId.set(undefined);
        // Load overview for the current date (will update overview when data arrives)
        this.loadOverview(dateValue);
      }
    });
  }

  loadOverview(date: string): void {
    // Only load if not already loading for this date
    if (this.loading() && this.currentLoadingDate === date) {
      return;
    }

    this.loading.set(true);
    this.error.set(null);
    this.currentLoadingDate = date;

    this.planApi
      .getDay(date, this.horizonDays())
      .pipe(
        catchError((err) => {
          // Only update if this is still the current date we're loading
          if (this.currentLoadingDate === date) {
            this.loading.set(false);
            this.error.set(err.title || err.message || 'Failed to load overview');
            this.messageService.add({
              severity: 'error',
              summary: 'Error',
              detail: err.detail || err.title || err.message || 'Failed to load overview',
            });
          }
          return of(null);
        })
      )
      .subscribe((data) => {
        // Only update if this is still the current date we're loading
        if (this.currentLoadingDate === date) {
          this.loading.set(false);
          // Update overview directly - no need for setTimeout
          this.overview.set(data);
          // Update horizonDays from response
          if (data?.horizonDays) {
            this.horizonDays.set(data.horizonDays);
          }
        }
      });
  }

  importPoles(): void {
    this.loading.set(true);
    this.importApi
      .importPoles(14)
      .pipe(
        catchError((err) => {
          this.loading.set(false);
          this.messageService.add({
            severity: 'error',
            summary: 'Import Error',
            detail: err.detail || err.title || err.message || 'Failed to import poles',
          });
          return of(null);
        })
      )
      .subscribe((result) => {
        this.loading.set(false);
        if (result) {
          this.messageService.add({
            severity: 'success',
            summary: 'Import Complete',
            detail: `Imported ${result.imported} new poles, updated ${result.updated}`,
          });
          // Reset overview first to force refresh
          this.overview.set(null);
          this.selectedPoleId.set(undefined);
          // Small delay to ensure UI updates, then reload
          setTimeout(() => {
            this.loadOverview(this.date());
          }, 100);
        }
      });
  }

  generate(): void {
    const dateValue = this.date();
    const horizon = this.horizonDays();
    const request: GeneratePlanRequest = {
      fromDate: dateValue,
      days: horizon,
    };

    this.loading.set(true);
    this.planApi
      .generate(request)
      .pipe(
        catchError((err) => {
          this.loading.set(false);
          this.messageService.add({
            severity: 'error',
            summary: 'Generate Error',
            detail: err.detail || err.title || err.message || 'Failed to generate plan',
          });
          return of(null);
        })
      )
      .subscribe((result) => {
        this.loading.set(false);
        if (result) {
          const detail = `Generated ${result.generatedDays || 0} days (skipped ${result.skippedLockedDays || 0} locked). Planned: ${result.plannedPolesCount || 0}, Remaining: ${result.unplannedPolesCount || 0}`;
          this.messageService.add({
            severity: 'success',
            summary: 'Plan Generated',
            detail: detail,
          });
          // Force refresh by clearing current loading date and reloading with same horizon
          this.currentLoadingDate = null;
          this.overview.set(null);
          this.selectedPoleId.set(undefined);
          // Small delay to ensure UI updates, then reload
          setTimeout(() => {
            this.loadOverview(dateValue);
          }, 100);
        }
      });
  }

  lockDay(): void {
    const dateValue = this.date();
    this.loading.set(true);
    this.planApi
      .lockDay(dateValue)
      .pipe(
        catchError((err) => {
          this.loading.set(false);
          this.messageService.add({
            severity: 'error',
            summary: 'Lock Error',
            detail: err.detail || err.title || err.message || 'Failed to lock day',
          });
          return of(null);
        })
      )
      .subscribe(() => {
        this.loading.set(false);
        // Reset and reload to ensure UI updates
        this.overview.set(null);
        setTimeout(() => {
          this.loadOverview(dateValue);
        }, 100);
      });
  }

  unlockDay(): void {
    const dateValue = this.date();
    this.loading.set(true);
    this.planApi
      .unlockDay(dateValue)
      .pipe(
        catchError((err) => {
          this.loading.set(false);
          this.messageService.add({
            severity: 'error',
            summary: 'Unlock Error',
            detail: err.detail || err.title || err.message || 'Failed to unlock day',
          });
          return of(null);
        })
      )
      .subscribe(() => {
        this.loading.set(false);
        // Reset and reload to ensure UI updates
        this.overview.set(null);
        setTimeout(() => {
          this.loadOverview(dateValue);
        }, 100);
      });
  }

  navigateToPrevDay(): void {
    const prevDate = addDaysYmd(this.date(), -1);
    this.router.navigate(['/planner/day', prevDate]);
  }

  navigateToNextDay(): void {
    const nextDate = addDaysYmd(this.date(), 1);
    this.router.navigate(['/planner/day', nextDate]);
  }

  // Route colors - different color for each driver
  private readonly routeColors = [
    '#3388ff', // Blue
    '#ff3388', // Pink
    '#33ff88', // Green
    '#ff8833', // Orange
    '#8833ff', // Purple
    '#ff3333', // Red
    '#33ff33', // Light Green
    '#3333ff', // Dark Blue
  ];

  mapMarkers = computed<MapMarker[]>(() => {
    const overview = this.overview();
    if (!overview) {
      return [];
    }

    const markers: MapMarker[] = [];

    // Add markers for planned route stops
    for (const driver of overview.drivers) {
      if (driver.routeId && driver.stops && driver.stops.length > 0) {
        // Add start position marker
        markers.push({
          id: `route-${driver.driverId}-start`,
          lat: driver.startLatitude,
          lon: driver.startLongitude,
          label: `${driver.driverName}: Start`,
          kind: 'planned',
          driverId: driver.driverId,
        });
        // Add stop markers
        for (const stop of driver.stops) {
          markers.push({
            id: `route-${driver.driverId}-stop-${stop.sequence}`,
            lat: stop.latitude,
            lon: stop.longitude,
            label: `${driver.driverName}: ${stop.serial} (#${stop.sequence})`,
            kind: 'planned',
            driverId: driver.driverId,
          });
        }
      }
    }

    // Add unplanned poles
    for (const pole of overview.unplannedPoles) {
      markers.push({
        id: `pole-${pole.poleId}`,
        lat: pole.latitude,
        lon: pole.longitude,
        label: pole.serial,
        kind: 'unplanned',
      });
    }

    return markers;
  });

  routePolylines = computed<RoutePolyline[]>(() => {
    const overview = this.overview();
    if (!overview) {
      return [];
    }

    const routes: RoutePolyline[] = [];

    // Create a polyline for each driver's route
    for (let i = 0; i < overview.drivers.length; i++) {
      const driver = overview.drivers[i];
      if (driver.routeId && driver.stops && driver.stops.length > 0) {
        const color = this.routeColors[i % this.routeColors.length];
        // Include start position as first point, then all stops
        // Validate coordinates before adding
        const points: { lat: number; lon: number }[] = [];
        
        // Add start position if valid
        if (driver.startLatitude && driver.startLongitude && 
            !isNaN(driver.startLatitude) && !isNaN(driver.startLongitude) &&
            driver.startLatitude !== 0 && driver.startLongitude !== 0) {
          points.push({ lat: driver.startLatitude, lon: driver.startLongitude });
        }
        
        // Add all stops with valid coordinates
        for (const stop of driver.stops) {
          if (stop.latitude && stop.longitude && 
              !isNaN(stop.latitude) && !isNaN(stop.longitude) &&
              stop.latitude !== 0 && stop.longitude !== 0) {
            points.push({ lat: stop.latitude, lon: stop.longitude });
          }
        }
        
        // Add return to start position if we have stops
        if (driver.stops.length > 0 && 
            driver.startLatitude && driver.startLongitude && 
            !isNaN(driver.startLatitude) && !isNaN(driver.startLongitude) &&
            driver.startLatitude !== 0 && driver.startLongitude !== 0) {
          points.push({ lat: driver.startLatitude, lon: driver.startLongitude });
        }
        
        // Only add route if we have at least 2 points (to draw a line)
        if (points.length >= 2) {
          routes.push({
            id: `route-${driver.driverId}`,
            color,
            points,
          });
        }
      }
    }

    return routes;
  });

  onMarkerClicked(markerId: string): void {
    this.selectedPoleId.set(markerId);
  }

  onPoleClick(poleId: number): void {
    this.selectedPoleId.set(`pole-${poleId}`);
  }
}

