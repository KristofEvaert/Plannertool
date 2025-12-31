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
import { ToastModule } from 'primeng/toast';
import { MessageService } from 'primeng/api';
import { CheckboxModule } from 'primeng/checkbox';
import { DriversApiService } from '@services/drivers-api.service';
import {
  RoutesApiService,
  type RouteDto,
  type RouteStopDto,
  type UpdateRouteStopRequest,
} from '@services/routes-api.service';
import { RouteMessagesApiService } from '@services/route-messages-api.service';
import { RouteChangeNotificationsApiService } from '@services/route-change-notifications-api.service';
import {
  ServiceLocationOwnersApiService,
  type ServiceLocationOwnerDto,
} from '@services/service-location-owners-api.service';
import { ServiceLocationsApiService } from '@services/service-locations-api.service';
import { HelpManualComponent } from '@components/help-manual/help-manual.component';
import { AuthService } from '@services/auth.service';
import type { RouteChangeNotificationDto } from '@models/route-change-notification.model';

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
    ToastModule,
    CheckboxModule,
    HelpManualComponent,
  ],
  providers: [MessageService],
  templateUrl: './driver.page.html',
  styleUrl: './driver.page.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class DriverPage {
  private readonly driversApi = inject(DriversApiService);
  private readonly routesApi = inject(RoutesApiService);
  private readonly routeMessagesApi = inject(RouteMessagesApiService);
  private readonly routeChangeNotificationsApi = inject(RouteChangeNotificationsApiService);
  private readonly ownersApi = inject(ServiceLocationOwnersApiService);
  private readonly serviceLocationsApi = inject(ServiceLocationsApiService);
  private readonly zone = inject(NgZone);
  private readonly auth = inject(AuthService);
  private readonly messageService = inject(MessageService);

  private map: L.Map | null = null;
  private routeLayerGroup: L.LayerGroup | null = null;

  loading = signal(false);
  error = signal<string | null>(null);

  selectedDriverToolId = signal<string | null>(null);
  selectedOwnerId = signal<number | null>(null);
  selectedDate = signal<Date>(new Date());

  route = signal<RouteDto | null>(null);
  selectedStopId = signal<number | null>(null);
  routeChangeNotifications = signal<RouteChangeNotificationDto[]>([]);

  driverOptions = signal<DriverOption[]>([]);
  ownerOptions = signal<OwnerOption[]>([]);

  // Manual edit fields (stable references to avoid DatePicker loops)
  editingArrivedAt = signal<Date | null>(null);
  editingCompletedAt = signal<Date | null>(null);

  routeMessageText = signal('');
  routeMessageCategory = signal('Info');
  stopMessageText = signal('');
  stopMessageCategory = signal('Info');
  sendingMessage = signal(false);

  readonly issueOptions = [
    { label: 'None', value: '' },
    { label: 'Customer not present', value: 'CustomerNotPresent' },
    { label: 'Access denied', value: 'AccessDenied' },
    { label: 'Device missing', value: 'DeviceMissing' },
    { label: 'Traffic delay', value: 'TrafficDelay' },
    { label: 'Other', value: 'Other' },
  ];

  readonly messageCategoryOptions = [
    { label: 'Info', value: 'Info' },
    { label: 'Delay', value: 'Delay' },
    { label: 'Issue', value: 'Issue' },
    { label: 'Other', value: 'Other' },
  ];

  selectedStop = computed(() => {
    const route = this.route();
    const stopId = this.selectedStopId();
    if (!route || !stopId) return null;
    return route.stops.find((s) => s.id === stopId) ?? null;
  });

  activeRouteChangeNotifications = computed(() =>
    this.routeChangeNotifications().filter((n) => !n.acknowledgedUtc)
  );

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

  arriveStop(stop: RouteStopDto): void {
    const nowUtc = new Date().toISOString();
    this.routesApi.arriveStop(stop.id, nowUtc).subscribe({
      next: (updated) => this.applyStopUpdate(updated),
      error: (err) => {
        this.error.set(err?.message ?? 'Failed to mark arrival');
      },
    });
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
        this.loadRouteNotifications(route?.id ?? null);
        this.loading.set(false);
        this.scheduleMapRefresh();
      },
      error: (err) => {
        this.loading.set(false);
        this.error.set(err?.message ?? 'Failed to load route');
      },
    });
  }

  private loadRouteNotifications(routeId: number | null): void {
    if (!routeId) {
      this.routeChangeNotifications.set([]);
      return;
    }
    this.routeChangeNotificationsApi.getNotifications(routeId, false).subscribe({
      next: (items) => {
        this.routeChangeNotifications.set(items);
      },
      error: () => {
        this.routeChangeNotifications.set([]);
      },
    });
  }

  acknowledgeNotification(notification: RouteChangeNotificationDto): void {
    this.routeChangeNotificationsApi.acknowledge(notification.id).subscribe({
      next: () => {
        this.routeChangeNotifications.update((items) =>
          items.map((n) => (n.id === notification.id ? { ...n, acknowledgedUtc: new Date().toISOString() } : n))
        );
      },
      error: (err) => {
        this.messageService.add({
          severity: 'error',
          summary: 'Error',
          detail: err?.error?.message || err?.message || 'Failed to acknowledge change',
        });
      },
    });
  }

  departStop(stop: RouteStopDto): void {
    const nowUtc = new Date().toISOString();
    this.routesApi.departStop(stop.id, nowUtc).subscribe({
      next: (updated) => this.applyStopUpdate(updated),
      error: (err) => {
        this.error.set(err?.message ?? 'Failed to mark departure');
      },
    });
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

  saveDriverNote(stop: RouteStopDto, note: string | null | undefined): void {
    this.updateStop(stop.id, { driverNote: note ?? '' });
  }

  saveIssueCode(stop: RouteStopDto, issueCode: string | null | undefined): void {
    this.updateStop(stop.id, { issueCode: issueCode ?? '' });
  }

  saveFollowUpRequired(stop: RouteStopDto, required: boolean | null | undefined): void {
    this.updateStop(stop.id, { followUpRequired: !!required });
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
        this.applyStopUpdate(updated);
      },
      error: (err) => {
        this.error.set(err?.message ?? 'Failed to update stop');
      },
    });
  }

  private applyStopUpdate(updated: RouteStopDto): void {
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

    if (updated.status === 'Completed' && updated.serviceLocationToolId) {
      this.serviceLocationsApi.markDone(updated.serviceLocationToolId).subscribe({
        error: (err) => {
          this.error.set(err?.message ?? 'Failed to mark service location as completed');
        },
      });
    }
  }

  sendRouteMessage(): void {
    const route = this.route();
    if (!route) return;
    const messageText = this.routeMessageText().trim();
    if (!messageText) {
      this.messageService.add({
        severity: 'warn',
        summary: 'Validation',
        detail: 'Message is required.',
      });
      return;
    }
    this.sendingMessage.set(true);
    this.routeMessagesApi
      .createMessage({
        routeId: route.id,
        routeStopId: null,
        messageText,
        category: this.routeMessageCategory(),
      })
      .subscribe({
        next: () => {
          this.sendingMessage.set(false);
          this.routeMessageText.set('');
          this.messageService.add({
            severity: 'success',
            summary: 'Message sent',
            detail: 'Planner has been notified.',
          });
        },
        error: (err) => {
          this.sendingMessage.set(false);
          this.messageService.add({
            severity: 'error',
            summary: 'Error',
            detail: err?.error?.message || err?.message || 'Failed to send message',
          });
        },
      });
  }

  sendStopMessage(stop: RouteStopDto): void {
    const messageText = this.stopMessageText().trim();
    if (!messageText) {
      this.messageService.add({
        severity: 'warn',
        summary: 'Validation',
        detail: 'Message is required.',
      });
      return;
    }
    this.sendingMessage.set(true);
    this.routeMessagesApi
      .createMessage({
        routeId: this.route()!.id,
        routeStopId: stop.id,
        messageText,
        category: this.stopMessageCategory(),
      })
      .subscribe({
        next: () => {
          this.sendingMessage.set(false);
          this.stopMessageText.set('');
          this.messageService.add({
            severity: 'success',
            summary: 'Message sent',
            detail: 'Planner has been notified.',
          });
        },
        error: (err) => {
          this.sendingMessage.set(false);
          this.messageService.add({
            severity: 'error',
            summary: 'Error',
            detail: err?.error?.message || err?.message || 'Failed to send message',
          });
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


