import { CommonModule } from '@angular/common';
import {
  AfterViewInit,
  ChangeDetectionStrategy,
  Component,
  computed,
  DestroyRef,
  effect,
  inject,
  signal,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import * as L from 'leaflet';
import { MessageService } from 'primeng/api';
import { ButtonModule } from 'primeng/button';
import { DatePickerModule } from 'primeng/datepicker';
import { SelectModule } from 'primeng/select';
import { TableModule } from 'primeng/table';
import { TagModule } from 'primeng/tag';
import { ToastModule } from 'primeng/toast';
import { catchError, interval, of, startWith, Subscription, switchMap } from 'rxjs';

import { HelpManualComponent } from '@components/help-manual/help-manual.component';
import type { RouteMessageDto } from '@models/route-message.model';
import { DriversApiService } from '@services/drivers-api.service';
import { RouteMessagesApiService } from '@services/route-messages-api.service';
import { RouteMessagesHubService } from '@services/route-messages-hub.service';
import { RoutesApiService, type RouteDto, type RouteStopDto } from '@services/routes-api.service';
import {
  ServiceLocationOwnersApiService,
  type ServiceLocationOwnerDto,
} from '@services/service-location-owners-api.service';

interface DriverOption {
  label: string;
  value: string;
  ownerId: number;
}
interface OwnerOption {
  label: string;
  value: number;
}

type DriverPosition = {
  lat: number;
  lng: number;
  mode: 'at-stop' | 'between' | 'start' | 'finished';
} | null;

@Component({
  selector: 'app-route-followup',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    DatePickerModule,
    SelectModule,
    ButtonModule,
    TagModule,
    TableModule,
    ToastModule,
    HelpManualComponent,
  ],
  providers: [MessageService],
  templateUrl: './route-followup.page.html',
  styleUrl: './route-followup.page.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class RouteFollowupPage implements AfterViewInit {
  private readonly driversApi = inject(DriversApiService);
  private readonly routesApi = inject(RoutesApiService);
  private readonly routeMessagesApi = inject(RouteMessagesApiService);
  private readonly routeMessagesHub = inject(RouteMessagesHubService);
  private readonly ownersApi = inject(ServiceLocationOwnersApiService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly messageService = inject(MessageService);

  readonly maxDate = new Date();

  loading = signal(false);
  error = signal<string | null>(null);

  selectedDate = signal<Date>(new Date());
  selectedOwnerId = signal<number | null>(null);
  selectedDriverToolId = signal<string | null>(null);

  ownerOptions = signal<OwnerOption[]>([]);
  driverOptions = signal<DriverOption[]>([]);

  route = signal<RouteDto | null>(null);
  messages = signal<RouteMessageDto[]>([]);

  private map: L.Map | null = null;
  private layerGroup: L.LayerGroup | null = null;
  private pollSub: Subscription | null = null;

  driversForSelectedOwner = computed(() => {
    const ownerId = this.selectedOwnerId();
    const all = this.driverOptions();
    if (!ownerId) return all;
    return all.filter((d) => d.ownerId === ownerId);
  });

  isToday = computed(() => RouteFollowupPage.isSameDay(this.selectedDate(), new Date()));

  unreadCount = computed(() => this.messages().filter((m) => m.status === 'New').length);

  driverPosition = computed<DriverPosition>(() => {
    const route = this.route();
    if (!route || route.stops.length === 0) return null;

    const stops = [...route.stops].sort((a, b) => a.sequence - b.sequence);

    // If there is an arrivedAt but no completedAt, we assume the driver is at that stop.
    const activeStop = stops.find((s) => !!s.arrivedAtUtc && !s.completedAtUtc);
    if (activeStop) {
      return { lat: activeStop.latitude, lng: activeStop.longitude, mode: 'at-stop' };
    }

    const completed = stops.filter((s) => !!s.completedAtUtc || s.status === 'Completed');
    const lastCompleted = completed.length > 0 ? completed[completed.length - 1] : null;
    const nextStop = lastCompleted
      ? stops.find((s) => s.sequence > lastCompleted.sequence && !s.completedAtUtc)
      : stops.find((s) => !s.completedAtUtc);

    if (!lastCompleted && nextStop) {
      const startLat = route.startLatitude ?? route.driverStartLatitude;
      const startLng = route.startLongitude ?? route.driverStartLongitude;
      if (startLat != null && startLng != null) {
        return { lat: startLat, lng: startLng, mode: 'start' };
      }
      return { lat: nextStop.latitude, lng: nextStop.longitude, mode: 'start' };
    }

    if (lastCompleted && nextStop) {
      // No GPS available: show an approximate position between the last completed and next stop.
      return {
        lat: (lastCompleted.latitude + nextStop.latitude) / 2,
        lng: (lastCompleted.longitude + nextStop.longitude) / 2,
        mode: 'between',
      };
    }

    if (lastCompleted) {
      return { lat: lastCompleted.latitude, lng: lastCompleted.longitude, mode: 'finished' };
    }

    return null;
  });

  constructor() {
    this.loadLookups();

    this.routeMessagesHub.connect();
    const hubSub = this.routeMessagesHub.messages$.subscribe((message) => {
      this.upsertMessage(message);
      this.messageService.add({
        severity: 'info',
        summary: 'Driver message',
        detail: `${message.driverName || `Driver #${message.driverId}`}: ${message.messageText}`,
      });
    });

    this.destroyRef.onDestroy(() => {
      hubSub.unsubscribe();
      this.routeMessagesHub.disconnect();
    });

    // Keep selected driver valid when owner changes.
    effect(() => {
      const drivers = this.driversForSelectedOwner();
      const current = this.selectedDriverToolId();
      if (current && drivers.some((d) => d.value === current)) return;
      this.selectedDriverToolId.set(drivers[0]?.value ?? null);
    });

    effect(() => {
      const ownerId = this.selectedOwnerId();
      if (!ownerId) {
        this.messages.set([]);
        return;
      }
      this.loadMessages(ownerId);
    });

    // Auto-load when selection changes.
    effect(() => {
      const date = this.selectedDate();
      const ownerId = this.selectedOwnerId();
      const driverToolId = this.selectedDriverToolId();
      this.error.set(null);

      if (!ownerId || !driverToolId) {
        this.route.set(null);
        this.stopPolling();
        this.refreshMap();
        return;
      }

      this.loadRoute(date, driverToolId, ownerId, { enablePollingIfToday: true });
    });
  }

  ngAfterViewInit(): void {
    this.ensureMap();
    this.refreshMap();

    // Ensure we stop polling on destroy.
    this.destroyRef.onDestroy(() => this.stopPolling());
  }

  private loadLookups(): void {
    this.ownersApi.getAll(true).subscribe({
      next: (owners: ServiceLocationOwnerDto[]) => {
        const opts = owners
          .map((o) => ({ label: o.name, value: o.id }))
          .sort((a, b) => a.label.localeCompare(b.label));
        this.ownerOptions.set(opts);
        if (!this.selectedOwnerId() && opts.length > 0) {
          this.selectedOwnerId.set(opts[0].value);
        }
      },
    });

    this.driversApi.getDrivers(true).subscribe({
      next: (drivers) => {
        const opts = drivers
          .filter((d) => !!d.toolId)
          .map((d) => ({
            label: `${d.name}${d.ownerName ? ` (${d.ownerName})` : ''}`,
            value: d.toolId,
            ownerId: d.ownerId,
          }))
          .sort((a, b) => a.label.localeCompare(b.label));
        this.driverOptions.set(opts);
        if (!this.selectedDriverToolId() && opts.length > 0) {
          this.selectedDriverToolId.set(opts[0].value);
        }
      },
    });
  }

  private loadMessages(ownerId: number): void {
    this.routeMessagesApi.getMessages(ownerId).subscribe({
      next: (items) => {
        this.messages.set(items);
      },
      error: () => {
        this.messages.set([]);
      },
    });
  }

  private upsertMessage(message: RouteMessageDto): void {
    this.messages.update((items) => {
      if (items.some((m) => m.id === message.id)) {
        return items;
      }
      return [message, ...items];
    });
  }

  manualReload(): void {
    const ownerId = this.selectedOwnerId();
    const driverToolId = this.selectedDriverToolId();
    if (!ownerId || !driverToolId) return;
    this.loadRoute(this.selectedDate(), driverToolId, ownerId, { enablePollingIfToday: true });
  }

  private loadRoute(
    date: Date,
    driverToolId: string,
    ownerId: number,
    options?: { enablePollingIfToday?: boolean },
  ): void {
    this.loading.set(true);
    this.routesApi.getDriverDayRoute(date, driverToolId, ownerId, true).subscribe({
      next: (route) => {
        this.loading.set(false);
        this.route.set(route);
        this.refreshMap();

        if (options?.enablePollingIfToday && this.isToday()) {
          this.startPolling(date, driverToolId, ownerId);
        } else {
          this.stopPolling();
        }
      },
      error: (err) => {
        this.loading.set(false);
        this.error.set(err?.message ?? 'Failed to load route');
        this.route.set(null);
        this.refreshMap();
        this.stopPolling();
      },
    });
  }

  private startPolling(date: Date, driverToolId: string, ownerId: number): void {
    // Restart if already polling.
    this.stopPolling();

    this.pollSub = interval(20_000)
      .pipe(
        startWith(0),
        switchMap(() =>
          this.routesApi
            .getDriverDayRoute(date, driverToolId, ownerId, true)
            .pipe(catchError(() => of(null))),
        ),
      )
      .subscribe((route) => {
        if (!route) return;
        this.route.set(route);
        this.refreshMap();
      });
  }

  private stopPolling(): void {
    this.pollSub?.unsubscribe();
    this.pollSub = null;
  }

  private ensureMap(): void {
    if (this.map) return;
    const container = document.getElementById('route-followup-map');
    if (!container) return;

    this.map = L.map('route-followup-map', {
      zoomControl: true,
      preferCanvas: true,
      zoomAnimation: false,
      fadeAnimation: false,
      markerZoomAnimation: false,
      inertia: false,
    }).setView([51.0, 4.0], 8);

    L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
      maxZoom: 19,
      attribution: '&copy; OpenStreetMap contributors',
    }).addTo(this.map);

    this.layerGroup = L.layerGroup().addTo(this.map);
  }

  private refreshMap(): void {
    this.ensureMap();
    if (!this.map || !this.layerGroup) return;

    this.layerGroup.clearLayers();

    const route = this.route();
    if (!route || route.stops.length === 0) {
      this.map.invalidateSize();
      return;
    }

    const points = (route.geometry ?? []).map((p) => L.latLng(p.lat, p.lng));
    const fallback = route.stops.map((s) => L.latLng(s.latitude, s.longitude));
    const linePoints = points.length > 0 ? points : fallback;

    const polyline = L.polyline(linePoints, { color: '#2563eb', weight: 4, opacity: 0.9 });
    polyline.addTo(this.layerGroup);

    for (const stop of route.stops) {
      const color =
        stop.status === 'Completed' ? '#16a34a' : stop.status === 'Arrived' ? '#f97316' : '#2563eb';
      const icon = L.divIcon({
        className: 'route-followup-stop-marker',
        html: `<div class="route-followup-stop-marker-inner" style="border-color:${color};color:${color}">${stop.sequence}</div>`,
        iconSize: [28, 28],
        iconAnchor: [14, 14],
      });
      L.marker([stop.latitude, stop.longitude], { icon }).addTo(this.layerGroup);
    }

    const pos = this.driverPosition();
    if (pos) {
      const icon = L.divIcon({
        className: 'route-followup-driver-marker',
        html: `<div class="route-followup-driver-marker-inner">Driver</div>`,
        iconSize: [46, 24],
        iconAnchor: [23, 12],
      });
      const marker = L.marker([pos.lat, pos.lng], { icon });
      marker.addTo(this.layerGroup);
    }

    const bounds = polyline.getBounds();
    if (bounds.isValid()) {
      this.map.fitBounds(bounds.pad(0.15), { animate: false });
    }
    this.map.invalidateSize();
  }

  stopDurationMinutes(stop: RouteStopDto): number | null {
    if (stop.actualServiceMinutes != null) return stop.actualServiceMinutes;
    if (!stop.arrivedAtUtc || !stop.completedAtUtc) return null;
    const a = new Date(stop.arrivedAtUtc);
    const c = new Date(stop.completedAtUtc);
    const minutes = Math.round((c.getTime() - a.getTime()) / 60000);
    if (Number.isFinite(minutes) && minutes >= 0) return minutes;
    // Fallback to planned service minutes when timestamps are not usable.
    return stop.serviceMinutes ?? null;
  }

  timeLabel(utcIso?: string): string {
    if (!utcIso) return '—';
    const d = new Date(utcIso);
    if (Number.isNaN(d.getTime())) return '—';
    return d.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
  }

  severityForStopStatus(
    status?: string,
  ): 'success' | 'secondary' | 'info' | 'warn' | 'danger' | 'contrast' | undefined | null {
    if (status === 'Completed') return 'success';
    if (status === 'Arrived') return 'warn';
    return 'info';
  }

  messageStatusSeverity(
    status?: string,
  ): 'success' | 'secondary' | 'info' | 'warn' | 'danger' | 'contrast' | undefined | null {
    if (status === 'Resolved') return 'success';
    if (status === 'Read') return 'info';
    return 'warn';
  }

  markMessageRead(message: RouteMessageDto): void {
    if (message.status !== 'New') return;
    this.routeMessagesApi.markRead(message.id).subscribe({
      next: () => {
        this.messages.update((items) =>
          items.map((m) => (m.id === message.id ? { ...m, status: 'Read' } : m)),
        );
      },
    });
  }

  markMessageResolved(message: RouteMessageDto): void {
    if (message.status === 'Resolved') return;
    this.routeMessagesApi.markResolved(message.id).subscribe({
      next: () => {
        this.messages.update((items) =>
          items.map((m) => (m.id === message.id ? { ...m, status: 'Resolved' } : m)),
        );
      },
    });
  }

  messageTimeLabel(utcIso?: string): string {
    if (!utcIso) return '-';
    const d = new Date(utcIso);
    if (Number.isNaN(d.getTime())) return '-';
    return d.toLocaleString([], {
      hour: '2-digit',
      minute: '2-digit',
      month: 'short',
      day: '2-digit',
    });
  }

  private static isSameDay(a: Date, b: Date): boolean {
    const aa = new Date(a);
    aa.setHours(0, 0, 0, 0);
    const bb = new Date(b);
    bb.setHours(0, 0, 0, 0);
    return aa.getTime() === bb.getTime();
  }
}
