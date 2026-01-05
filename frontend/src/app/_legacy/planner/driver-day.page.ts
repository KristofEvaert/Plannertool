import { Component, computed, effect, inject, input, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { ButtonModule } from 'primeng/button';
import { MessageModule } from 'primeng/message';
import { ToastModule } from 'primeng/toast';
import { TagModule } from 'primeng/tag';
import { MessageService } from 'primeng/api';
import { catchError, of } from 'rxjs';
import { PlanApiService } from '@services/plan-api.service';
import type { DriverDayDto } from '@models/plan.model';
import {
  LeafletMapComponent,
  type MapMarker,
  type PolylinePoint,
} from '@components/map/leaflet-map.component';

@Component({
  selector: 'app-driver-day',
  imports: [RouterLink, ButtonModule, MessageModule, ToastModule, TagModule, LeafletMapComponent],
  providers: [MessageService],
  templateUrl: './driver-day.page.html',
  styleUrl: './driver-day.page.css',
  standalone: true,
})
export class DriverDayPage {
  date = input.required<string>();
  driverId = input.required<number>();

  private readonly planApi = inject(PlanApiService);
  private readonly messageService = inject(MessageService);
  private readonly router = inject(Router);

  driverDay = signal<DriverDayDto | null>(null);
  loading = signal(false);
  error = signal<string | null>(null);
  selectedStopSequence = signal<number | undefined>(undefined);

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
    this.selectedStopSequence.set(undefined);

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

  onStopClick(sequence: number): void {
    this.selectedStopSequence.set(sequence);
  }

  navigateToPole(latitude: number, longitude: number): void {
    // Open Google Maps with navigation to the pole location
    const url = `https://www.google.com/maps/dir/?api=1&destination=${latitude},${longitude}`;
    window.open(url, '_blank');
  }

  navigateToStart(): void {
    const day = this.driverDay();
    if (day && day.startLatitude && day.startLongitude) {
      this.navigateToPole(day.startLatitude, day.startLongitude);
    }
  }

  mapMarkers = computed<MapMarker[]>(() => {
    const day = this.driverDay();
    if (!day) {
      return [];
    }

    const markers: MapMarker[] = [];

    // Add start position marker
    if (day.startLatitude && day.startLongitude) {
      markers.push({
        id: 'start',
        lat: day.startLatitude,
        lon: day.startLongitude,
        label: 'Start',
        kind: 'planned' as const,
        driverId: day.driverId,
      });
    }

    // Add stop markers
    if (day.stops) {
      for (const stop of day.stops) {
        markers.push({
          id: `stop-${stop.sequence}`,
          lat: stop.latitude,
          lon: stop.longitude,
          label: `${stop.sequence}. ${stop.serial}`,
          kind: 'planned' as const,
          driverId: day.driverId,
        });
      }
    }

    return markers;
  });

  polylinePoints = computed<PolylinePoint[]>(() => {
    const day = this.driverDay();
    if (!day) {
      return [];
    }

    const points: PolylinePoint[] = [];

    // Add start position as first point if valid
    if (
      day.startLatitude &&
      day.startLongitude &&
      !isNaN(day.startLatitude) &&
      !isNaN(day.startLongitude) &&
      day.startLatitude !== 0 &&
      day.startLongitude !== 0
    ) {
      points.push({
        lat: day.startLatitude,
        lon: day.startLongitude,
      });
    }

    // Add all stops with valid coordinates
    if (day.stops) {
      for (const stop of day.stops) {
        if (
          stop.latitude &&
          stop.longitude &&
          !isNaN(stop.latitude) &&
          !isNaN(stop.longitude) &&
          stop.latitude !== 0 &&
          stop.longitude !== 0
        ) {
          points.push({
            lat: stop.latitude,
            lon: stop.longitude,
          });
        }
      }
    }

    // Add return to start position if we have stops and valid start coordinates
    if (
      day.stops &&
      day.stops.length > 0 &&
      day.startLatitude &&
      day.startLongitude &&
      !isNaN(day.startLatitude) &&
      !isNaN(day.startLongitude) &&
      day.startLatitude !== 0 &&
      day.startLongitude !== 0
    ) {
      points.push({
        lat: day.startLatitude,
        lon: day.startLongitude,
      });
    }

    return points;
  });

  selectedMarkerId = computed<string | undefined>(() => {
    const sequence = this.selectedStopSequence();
    if (sequence === undefined) {
      return undefined;
    }
    return `stop-${sequence}`;
  });

  centerOnMarkerId = computed<string | undefined>(() => {
    return this.selectedMarkerId();
  });

  onMarkerClicked(markerId: string): void {
    // Extract sequence from markerId (format: "stop-{sequence}")
    const match = markerId.match(/^stop-(\d+)$/);
    if (match) {
      const sequence = parseInt(match[1], 10);
      this.selectedStopSequence.set(sequence);
    }
  }
}
