import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  NgZone,
  signal,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import * as L from 'leaflet';
import { SelectModule } from 'primeng/select';
import { DatePickerModule } from 'primeng/datepicker';
import { ButtonModule } from 'primeng/button';
import { InputNumberModule } from 'primeng/inputnumber';
import { InputTextarea } from 'primeng/inputtextarea';
import { TagModule } from 'primeng/tag';
import { DriversApiService } from '@services/drivers-api.service';
import {
  RoutesApiService,
  type RouteDto,
  type RouteStopDto,
  type UpdateRouteStopRequest,
} from '@services/routes-api.service';
import {
  ServiceLocationOwnersApiService,
  type ServiceLocationOwnerDto,
} from '@services/service-location-owners-api.service';
import { ServiceLocationsApiService } from '@services/service-locations-api.service';
import { AuthService } from '@services/auth.service';

type DriverOption = { label: string; value: string };
type OwnerOption = { label: string; value: number };

@Component({
  selector: 'app-driver-page',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    SelectModule,
    DatePickerModule,
    ButtonModule,
    InputNumberModule,
    InputTextarea,
    TagModule,
  ],
  templateUrl: './driver.page.html',
  styleUrl: './driver.page.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class DriverPage {
  private readonly driversApi = inject(DriversApiService);
  private readonly routesApi = inject(RoutesApiService);
  private readonly ownersApi = inject(ServiceLocationOwnersApiService);
  private readonly serviceLocationsApi = inject(ServiceLocationsApiService);
  private readonly zone = inject(NgZone);
  private readonly auth = inject(AuthService);

  private map: L.Map | null = null;
  private routeLayerGroup: L.LayerGroup | null = null;

  loading = signal(false);
  error = signal<string | null>(null);

  selectedDriverToolId = signal<string | null>(null);
  selectedOwnerId = signal<number | null>(null);
  selectedDate = signal<Date>(new Date());

  route = signal<RouteDto | null>(null);
  selectedStopId = signal<number | null>(null);

  driverOptions = signal<DriverOption[]>([]);
  ownerOptions = signal<OwnerOption[]>([]);

  // Manual edit fields (stable references to avoid DatePicker loops)
  editingArrivedAt = signal<Date | null>(null);
  editingCompletedAt = signal<Date | null>(null);

  selectedStop = computed(() => {
    const route = this.route();
    const stopId = this.selectedStopId();
    if (!route || !stopId) return null;
    return route.stops.find((s) => s.id === stopId) ?? null;
  });

  constructor() {
    this.loadLookups();

    effect(() => {
      const user = this.auth.currentUser();
      if (user) {
        const isDriver = user.roles.includes('Driver');
        if (isDriver && user.driverToolId) {
          this.selectedDriverToolId.set(user.driverToolId);
        }
        if (isDriver && user.driverOwnerId) {
          this.selectedOwnerId.set(user.driverOwnerId);
        }
      }
    });

    // Keep manual edit fields in sync with the currently selected stop.
    effect(() => {
      const stop = this.selectedStop();
      if (!stop) {
        this.editingArrivedAt.set(null);
        this.editingCompletedAt.set(null);
        return;
      }

      this.editingArrivedAt.set(DriverPage.parseIsoDate(stop.arrivedAtUtc) ?? new Date());
      this.editingCompletedAt.set(DriverPage.parseIsoDate(stop.completedAtUtc));
    });

    effect((onCleanup) => {
      const driverToolId = this.selectedDriverToolId();
      const ownerId = this.selectedOwnerId();
      const date = this.selectedDate();

      this.route.set(null);
      this.selectedStopId.set(null);
      this.error.set(null);

      if (!driverToolId || !ownerId) return;

      this.loading.set(true);
      const sub = this.loadRoute(driverToolId, ownerId, date);

      onCleanup(() => sub.unsubscribe());
    });
  }

  private static parseIsoDate(utcIso?: string): Date | null {
    if (!utcIso) return null;
    const d = new Date(utcIso);
    return Number.isNaN(d.getTime()) ? null : d;
  }

  private loadLookups(): void {
    const currentUser = this.auth.currentUser();
    const isDriver = currentUser?.roles.includes('Driver');
    if (isDriver && currentUser?.driverToolId) {
      this.driverOptions.set([{ label: currentUser.displayName || currentUser.email, value: currentUser.driverToolId }]);
      this.selectedDriverToolId.set(currentUser.driverToolId);
    } else {
      this.driversApi.getDrivers(true).subscribe({
        next: (drivers) => {
          const opts = drivers
            .filter((d) => !!d.toolId)
            .map((d) => ({ label: d.name, value: d.toolId }))
            .sort((a, b) => a.label.localeCompare(b.label));
          this.driverOptions.set(opts);
          if (!this.selectedDriverToolId() && opts.length > 0) {
            this.selectedDriverToolId.set(opts[0].value);
          }
        },
      });
    }

    if (isDriver && currentUser?.driverOwnerId) {
      this.ownerOptions.set([{ label: 'My Owner', value: currentUser.driverOwnerId }]);
      this.selectedOwnerId.set(currentUser.driverOwnerId);
    } else {
      this.ownersApi.getAll(true).subscribe({
        next: (owners: ServiceLocationOwnerDto[]) => {
          const opts = owners.map((o) => ({ label: o.name, value: o.id }));
          this.ownerOptions.set(opts);
          if (!this.selectedOwnerId() && opts.length > 0) {
            this.selectedOwnerId.set(opts[0].value);
          }
        },
      });
    }
  }

  onStopClick(stop: RouteStopDto): void {
    this.selectedStopId.set(stop.id);

    if (!this.map) return;
    // Avoid Leaflet animation loops inside Angular.
    this.zone.runOutsideAngular(() => {
      this.map?.setView([stop.latitude, stop.longitude], Math.max(this.map!.getZoom(), 13), {
        animate: false,
      });
    });
  }

  startStop(stop: RouteStopDto): void {
    const nowUtc = new Date().toISOString();
    this.updateStop(stop.id, { arrivedAtUtc: nowUtc });
  }

  async markNotVisited(): Promise<void> {
    const stop = this.selectedStop();
    if (!stop) return;
    if (!stop.note?.trim()) {
      this.error.set('Reason is required for not visited.');
      return;
    }
    this.error.set(null);
    try {
      const dto = await this.routesApi
        .updateRouteStop(stop.id, {
          status: 'NotVisited',
          note: stop.note.trim(),
        })
        .toPromise();
      if (dto) {
        this.reloadRoute();
        this.selectedStopId.set(dto.id);
      }
    } catch (err: any) {
      this.error.set(err?.error?.message || err?.message || 'Failed to update stop');
    }
  }

  private reloadRoute(): void {
    const driverToolId = this.selectedDriverToolId();
    const ownerId = this.selectedOwnerId();
    const date = this.selectedDate();
    if (!driverToolId || !ownerId) return;
    this.loading.set(true);
    this.loadRoute(driverToolId, ownerId, date);
  }

  private loadRoute(driverToolId: string, ownerId: number, date: Date) {
    return this.routesApi.getDriverDayRoute(date, driverToolId, ownerId, true).subscribe({
      next: (route) => {
        this.route.set(route);
        this.loading.set(false);
        this.scheduleMapRefresh();
      },
      error: (err) => {
        this.loading.set(false);
        this.error.set(err?.message ?? 'Failed to load route');
      },
    });
  }

  finishStop(stop: RouteStopDto): void {
    const nowUtc = new Date().toISOString();
    const duration = this.computeDurationMinutes(stop.arrivedAtUtc, nowUtc);
    const patch: UpdateRouteStopRequest = { completedAtUtc: nowUtc };
    if (duration != null) {
      patch.actualServiceMinutes = duration;
    }
    this.updateStop(stop.id, patch);
  }

  saveDuration(stop: RouteStopDto, minutes: number | null | undefined): void {
    const value = minutes ?? null;
    if (value == null) return;
    this.updateStop(stop.id, { actualServiceMinutes: value });
  }

  stopDurationMinutes(stop: RouteStopDto): number | null {
    if (stop.actualServiceMinutes != null) return stop.actualServiceMinutes;
    return this.computeDurationMinutes(stop.arrivedAtUtc, stop.completedAtUtc);
  }

  saveNote(stop: RouteStopDto, note: string | null | undefined): void {
    this.updateStop(stop.id, { note: note ?? '' });
  }

  saveArrivedAt(stop: RouteStopDto, arrivedAt: Date | null | undefined): void {
    if (!arrivedAt) return;
    this.updateStop(stop.id, { arrivedAtUtc: arrivedAt.toISOString() });
  }

  saveCompletedAt(stop: RouteStopDto, completedAt: Date | null | undefined): void {
    if (!completedAt) return;
    this.updateStop(stop.id, { completedAtUtc: completedAt.toISOString() });
  }

  private updateStop(routeStopId: number, patch: UpdateRouteStopRequest): void {
    this.routesApi.updateRouteStop(routeStopId, patch).subscribe({
      next: (updated) => {
        const route = this.route();
        if (!route) return;

        // Ensure local stop carries a computed duration when timestamps exist.
        const finalUpdated: RouteStopDto = (() => {
          const maybeDuration = this.computeDurationMinutes(updated.arrivedAtUtc, updated.completedAtUtc);
          if (maybeDuration != null && updated.actualServiceMinutes == null) {
            return { ...updated, actualServiceMinutes: maybeDuration };
          }
          return updated;
        })();

        const nextStops = route.stops.map((s) => (s.id === finalUpdated.id ? { ...s, ...finalUpdated } : s));
        this.route.set({ ...route, stops: nextStops });
        this.scheduleMapRefresh();

        // Ensure the actual ServiceLocation is marked Done when the driver completes a stop.
        // The backend also attempts to do this, but this extra call guarantees it even if other
        // planning operations are occurring in parallel.
        const shouldMarkDone =
          (patch.completedAtUtc != null || patch.actualServiceMinutes != null) &&
          updated.status === 'Completed' &&
          !!updated.serviceLocationToolId;

        if (shouldMarkDone) {
          this.serviceLocationsApi.markDone(updated.serviceLocationToolId!).subscribe({
            // No-op: service location list views will reflect it on next refresh.
            error: (err) => {
              // Don't block driver UX on a secondary update.
              this.error.set(err?.message ?? 'Failed to mark service location as completed');
            },
          });
        }
      },
      error: (err) => {
        this.error.set(err?.message ?? 'Failed to update stop');
      },
    });
  }

  private scheduleMapRefresh(): void {
    // Leaflet needs the container to be in the DOM *and* have a computed size.
    // Using a macrotask ensures Angular has painted the updated view.
    this.zone.runOutsideAngular(() => {
      setTimeout(() => {
        this.refreshMap();
        this.map?.invalidateSize();
      }, 0);
    });
  }

  private ensureMap(): void {
    if (this.map) return;

    const container = document.getElementById('driver-map');
    if (!container) return;

    this.zone.runOutsideAngular(() => {
      this.map = L.map('driver-map', {
        zoomControl: true,
        preferCanvas: true,
        // Prevent continuous requestAnimationFrame loops from animations/inertia.
        zoomAnimation: false,
        fadeAnimation: false,
        markerZoomAnimation: false,
        inertia: false,
      }).setView([51.0, 4.0], 8);

      L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
        maxZoom: 19,
        attribution: '&copy; OpenStreetMap contributors',
      }).addTo(this.map);

      this.routeLayerGroup = L.layerGroup().addTo(this.map);
    });
  }

  private refreshMap(): void {
    this.ensureMap();
    if (!this.map || !this.routeLayerGroup) return;

    this.routeLayerGroup.clearLayers();

    const route = this.route();
    if (!route || route.stops.length === 0) {
      // Keep the basemap visible even if no route is loaded.
      return;
    }

    const points = (route.geometry ?? []).map((p) => L.latLng(p.lat, p.lng));
    const fallback = route.stops.map((s) => L.latLng(s.latitude, s.longitude));
    const linePoints = points.length > 0 ? points : fallback;

    const polyline = L.polyline(linePoints, { color: '#2563eb', weight: 4, opacity: 0.9 });
    polyline.addTo(this.routeLayerGroup);

    for (const stop of route.stops) {
      const isSelected = this.selectedStopId() === stop.id;
      const color =
        stop.status === 'Completed' ? '#16a34a' : stop.status === 'Arrived' ? '#f97316' : '#2563eb';

      const icon = L.divIcon({
        className: 'driver-stop-marker',
        html: `<div class="driver-stop-marker-inner" style="border-color:${color};background:${
          isSelected ? color : 'white'
        };color:${isSelected ? 'white' : color}">${stop.sequence}</div>`,
        iconSize: [28, 28],
        iconAnchor: [14, 14],
      });

      const marker = L.marker([stop.latitude, stop.longitude], { icon });
      marker.on('click', () => this.onStopClick(stop));
      marker.addTo(this.routeLayerGroup);
    }

    const bounds = polyline.getBounds();
    if (bounds.isValid()) {
      // No animation to avoid RAF loops / "stuck" feeling.
      this.map.fitBounds(bounds.pad(0.15), { animate: false });
    }
  }

  severityForStopStatus(status?: string): 'success' | 'warning' | 'info' {
    if (status === 'Completed') return 'success';
    if (status === 'Arrived') return 'warning';
    if (status === 'NotVisited') return 'warning';
    return 'info';
  }

  onArrivedAtEditChange(value: Date | null): void {
    this.editingArrivedAt.set(value);
    const stop = this.selectedStop();
    if (!stop || !value) return;
    const arrivedIso = value.toISOString();
    const completedIso = this.editingCompletedAt()?.toISOString() ?? stop.completedAtUtc ?? null;
    const duration = this.computeDurationMinutes(arrivedIso, completedIso);
    const patch: UpdateRouteStopRequest = { arrivedAtUtc: arrivedIso };
    if (duration != null) {
      patch.actualServiceMinutes = duration;
    }
    this.updateStop(stop.id, patch);
  }

  onCompletedAtEditChange(value: Date | null): void {
    this.editingCompletedAt.set(value);
    const stop = this.selectedStop();
    if (!stop || !value) return;
    const completedIso = value.toISOString();
    const arrivedIso = this.editingArrivedAt()?.toISOString() ?? stop.arrivedAtUtc ?? null;
    const duration = this.computeDurationMinutes(arrivedIso, completedIso);
    const patch: UpdateRouteStopRequest = { completedAtUtc: completedIso };
    if (duration != null) {
      patch.actualServiceMinutes = duration;
    }
    this.updateStop(stop.id, patch);
  }

  openInGoogleMaps(stop: RouteStopDto): void {
    const lat = Number(stop.latitude);
    const lng = Number(stop.longitude);
    if (!Number.isFinite(lat) || !Number.isFinite(lng)) return;
    const url = `https://www.google.com/maps/dir/?api=1&destination=${encodeURIComponent(
      `${lat},${lng}`
    )}`;
    window.open(url, '_blank');
  }

  private computeDurationMinutes(
    arrivedAt?: string | null,
    completedAt?: string | null
  ): number | null {
    if (!arrivedAt || !completedAt) return null;
    const a = new Date(arrivedAt);
    const c = new Date(completedAt);
    if (Number.isNaN(a.getTime()) || Number.isNaN(c.getTime())) return null;
    const minutes = Math.round(Math.abs(c.getTime() - a.getTime()) / 60000);
    return Number.isFinite(minutes) ? minutes : null;
  }
}


