import { CommonModule } from '@angular/common';
import {
  AfterViewInit,
  Component,
  computed,
  DestroyRef,
  effect,
  ElementRef,
  inject,
  NgZone,
  signal,
  untracked,
  ViewChild,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { HelpManualComponent } from '@components';
import type { RouteChangeNotificationDto, RouteMessageDto } from '@models';
import { AuthService } from '@services/auth.service';
import { DriversApiService } from '@services/drivers-api.service';
import { RouteChangeNotificationsApiService } from '@services/route-change-notifications-api.service';
import { RouteMessagesApiService } from '@services/route-messages-api.service';
import { RouteMessagesHubService } from '@services/route-messages-hub.service';
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
import { toYmd } from '@utils/date.utils';
import * as L from 'leaflet';
import { MessageService } from 'primeng/api';
import { ButtonModule } from 'primeng/button';
import { CheckboxModule } from 'primeng/checkbox';
import { DatePickerModule } from 'primeng/datepicker';
import { InputNumberModule } from 'primeng/inputnumber';
import { SelectModule } from 'primeng/select';
import { TagModule } from 'primeng/tag';
import { TextareaModule } from 'primeng/textarea';
import { ToastModule } from 'primeng/toast';
import { firstValueFrom } from 'rxjs';

interface DriverOption {
  label: string;
  value: string;
}
interface OwnerOption {
  label: string;
  value: number;
}

@Component({
  selector: 'app-driver-page',
  imports: [
    CommonModule,
    FormsModule,
    SelectModule,
    DatePickerModule,
    ButtonModule,
    InputNumberModule,
    TextareaModule,
    TagModule,
    ToastModule,
    CheckboxModule,
    HelpManualComponent,
  ],
  providers: [MessageService],
  templateUrl: './driver.page.html',
  styleUrl: './driver.page.css',
})
export class DriverPage implements AfterViewInit {
  private readonly driversApi = inject(DriversApiService);
  private readonly routesApi = inject(RoutesApiService);
  private readonly routeMessagesApi = inject(RouteMessagesApiService);
  private readonly routeMessagesHub = inject(RouteMessagesHubService);
  private readonly routeChangeNotificationsApi = inject(RouteChangeNotificationsApiService);
  private readonly ownersApi = inject(ServiceLocationOwnersApiService);
  private readonly serviceLocationsApi = inject(ServiceLocationsApiService);
  private readonly zone = inject(NgZone);
  private readonly auth = inject(AuthService);
  private readonly messageService = inject(MessageService);
  private readonly destroyRef = inject(DestroyRef);

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
  routeMessages = signal<RouteMessageDto[]>([]);

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

  proofPhotoUrl = signal<string | null>(null);
  proofSignatureUrl = signal<string | null>(null);
  proofPhotoLoading = signal(false);
  proofSignatureLoading = signal(false);
  signatureHasInk = signal(false);
  cameraActive = signal(false);
  cameraLoading = signal(false);
  cameraError = signal<string | null>(null);

  private signatureDrawing = false;
  private signatureContext: CanvasRenderingContext2D | null = null;
  private signatureCanvasRef: ElementRef<HTMLCanvasElement> | null = null;
  private cameraStream: MediaStream | null = null;
  private cameraVideoRef: ElementRef<HTMLVideoElement> | null = null;

  @ViewChild('signatureCanvas')
  set signatureCanvas(value: ElementRef<HTMLCanvasElement> | undefined) {
    this.signatureCanvasRef = value ?? null;
    this.initSignatureCanvas();
  }

  @ViewChild('cameraVideo')
  set cameraVideo(value: ElementRef<HTMLVideoElement> | undefined) {
    this.cameraVideoRef = value ?? null;
    if (this.cameraVideoRef && this.cameraStream) {
      this.cameraVideoRef.nativeElement.srcObject = this.cameraStream;
      this.cameraVideoRef.nativeElement.play().catch(() => undefined);
    }
  }

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

  routeMessagesForRoute = computed(() =>
    [...this.routeMessages()]
      .filter((message) => !message.routeStopId)
      .sort((a, b) => a.createdUtc.localeCompare(b.createdUtc)),
  );

  routeMessagesForSelectedStop = computed(() => {
    const stop = this.selectedStop();
    if (!stop) return [];
    return [...this.routeMessages()]
      .filter((message) => message.routeStopId === stop.id)
      .sort((a, b) => a.createdUtc.localeCompare(b.createdUtc));
  });

  canEditStops = computed(() => {
    const route = this.route();
    if (!route?.date) return false;
    const routeDate = route.date.split('T')[0];
    return routeDate === toYmd(new Date());
  });

  activeRouteChangeNotifications = computed(() =>
    this.routeChangeNotifications().filter((n) => !n.acknowledgedUtc),
  );

  constructor() {
    this.loadLookups();

    this.routeMessagesHub.connect();
    const hubSub = this.routeMessagesHub.messages$.subscribe((message) => {
      const route = this.route();
      if (!route || message.routeId !== route.id) {
        return;
      }
      this.upsertRouteMessage(message);
    });

    this.destroyRef.onDestroy(() => {
      hubSub.unsubscribe();
      this.routeMessagesHub.disconnect();
      this.resetProofState();
    });

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

    effect(() => {
      const stop = this.selectedStop();
      this.resetProofState();
      this.signatureHasInk.set(false);

      if (!stop) {
        return;
      }

      if (stop.hasProofPhoto) {
        this.loadProofPhoto(stop.id);
      }

      if (stop.hasProofSignature) {
        this.loadProofSignature(stop.id);
      }
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

  ngAfterViewInit(): void {
    this.initSignatureCanvas();
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
      this.driverOptions.set([
        { label: currentUser.displayName || currentUser.email, value: currentUser.driverToolId },
      ]);
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
      const dto = await firstValueFrom(
        this.routesApi.updateRouteStop(stop.id, {
          status: 'NotVisited',
          note: stop.note.trim(),
        }),
      );
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
        if (route?.id) {
          this.loadRouteMessages(route.id);
        } else {
          this.routeMessages.set([]);
        }
        this.loading.set(false);
        this.scheduleMapRefresh();
      },
      error: (err) => {
        this.loading.set(false);
        this.error.set(err?.message ?? 'Failed to load route');
        this.routeMessages.set([]);
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

  private loadRouteMessages(routeId: number): void {
    const currentUser = this.auth.currentUser();
    const isDriver = currentUser?.roles.includes('Driver') ?? false;
    if (!isDriver) {
      const ownerId = this.selectedOwnerId();
      if (!ownerId) {
        this.routeMessages.set([]);
        return;
      }
      this.routeMessagesApi.getMessages(ownerId, undefined, routeId).subscribe({
        next: (items) => {
          this.routeMessages.set(items);
        },
        error: () => {
          this.routeMessages.set([]);
        },
      });
      return;
    }

    this.routeMessagesApi.getDriverMessages(routeId).subscribe({
      next: (items) => {
        this.routeMessages.set(items);
      },
      error: () => {
        this.routeMessages.set([]);
      },
    });
  }

  private upsertRouteMessage(message: RouteMessageDto): void {
    this.routeMessages.update((items) => {
      if (items.some((m) => m.id === message.id)) {
        return items;
      }
      return [...items, message];
    });
  }

  acknowledgeNotification(notification: RouteChangeNotificationDto): void {
    this.routeChangeNotificationsApi.acknowledge(notification.id).subscribe({
      next: () => {
        this.routeChangeNotifications.update((items) =>
          items.map((n) =>
            n.id === notification.id ? { ...n, acknowledgedUtc: new Date().toISOString() } : n,
          ),
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

  saveChecklistItem(stop: RouteStopDto, index: number, checked: boolean | null | undefined): void {
    const items = stop.checklistItems ?? [];
    if (index < 0 || index >= items.length) return;
    const nextItems = items.map((item, idx) =>
      idx === index ? { ...item, isChecked: !!checked } : item,
    );
    this.updateStop(stop.id, { checklistItems: nextItems });
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
      const maybeDuration = this.computeDurationMinutes(
        updated.arrivedAtUtc,
        updated.completedAtUtc,
      );
      if (maybeDuration != null && updated.actualServiceMinutes == null) {
        return { ...updated, actualServiceMinutes: maybeDuration };
      }
      return updated;
    })();

    const nextStops = route.stops.map((s) =>
      s.id === finalUpdated.id ? { ...s, ...finalUpdated } : s,
    );
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
        next: (created) => {
          this.sendingMessage.set(false);
          this.routeMessageText.set('');
          this.upsertRouteMessage(created);
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
        next: (created) => {
          this.sendingMessage.set(false);
          this.stopMessageText.set('');
          this.upsertRouteMessage(created);
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

  severityForStopStatus(
    status?: string,
  ): 'success' | 'secondary' | 'info' | 'warn' | 'danger' | 'contrast' | undefined | null {
    if (status === 'Completed') return 'success';
    if (status === 'Arrived') return 'warn';
    if (status === 'NotVisited') return 'warn';
    return 'info';
  }

  messageTimeLabel(utcIso?: string): string {
    if (!utcIso) return '—';
    const d = new Date(utcIso);
    if (Number.isNaN(d.getTime())) return '—';
    return d.toLocaleString([], {
      hour: '2-digit',
      minute: '2-digit',
      month: 'short',
      day: '2-digit',
    });
  }

  messageSenderLabel(message: RouteMessageDto): string {
    return DriverPage.isPlannerMessage(message) ? 'Planner' : 'You';
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
      `${lat},${lng}`,
    )}`;
    window.open(url, '_blank');
  }

  onProofPhotoSelected(event: Event): void {
    const stop = this.selectedStop();
    if (!stop || !this.canEditStops()) return;
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    input.value = '';
    if (!file) return;

    this.uploadProofPhoto(stop.id, file);
  }

  async startCamera(): Promise<void> {
    if (!this.canEditStops()) return;
    if (!navigator.mediaDevices?.getUserMedia) {
      this.cameraError.set('Camera access is not available in this browser.');
      return;
    }

    if (this.cameraStream) {
      this.cameraActive.set(true);
      return;
    }

    this.cameraError.set(null);
    this.cameraLoading.set(true);
    try {
      const stream = await navigator.mediaDevices.getUserMedia({
        video: { facingMode: { ideal: 'environment' } },
        audio: false,
      });
      this.cameraStream = stream;
      this.cameraActive.set(true);
      const video = this.cameraVideoRef?.nativeElement;
      if (video) {
        video.srcObject = stream;
        await video.play().catch(() => undefined);
      }
    } catch (err) {
      this.cameraError.set('Unable to access the camera.');
    } finally {
      this.cameraLoading.set(false);
    }
  }

  stopCamera(): void {
    if (this.cameraStream) {
      for (const track of this.cameraStream.getTracks()) {
        track.stop();
      }
    }
    this.cameraStream = null;
    if (this.cameraVideoRef) {
      this.cameraVideoRef.nativeElement.srcObject = null;
    }
    this.cameraActive.set(false);
    this.cameraLoading.set(false);
  }

  capturePhotoFromCamera(): void {
    const stop = this.selectedStop();
    if (!stop || !this.canEditStops()) return;
    const video = this.cameraVideoRef?.nativeElement;
    if (!video || video.videoWidth === 0 || video.videoHeight === 0) {
      this.cameraError.set('Camera is not ready yet.');
      return;
    }

    const canvas = document.createElement('canvas');
    canvas.width = video.videoWidth;
    canvas.height = video.videoHeight;
    const ctx = canvas.getContext('2d');
    if (!ctx) return;
    ctx.drawImage(video, 0, 0, canvas.width, canvas.height);

    canvas.toBlob(
      (blob) => {
        if (!blob) {
          this.messageService.add({
            severity: 'error',
            summary: 'Error',
            detail: 'Failed to capture photo.',
          });
          return;
        }

        const file = new File([blob], 'camera-photo.jpg', { type: 'image/jpeg' });
        this.uploadProofPhoto(stop.id, file, true);
      },
      'image/jpeg',
      0.9,
    );
  }

  saveSignature(): void {
    const stop = this.selectedStop();
    if (!stop || !this.canEditStops()) return;
    if (!this.signatureHasInk()) {
      this.messageService.add({
        severity: 'warn',
        summary: 'Validation',
        detail: 'Add a signature before saving.',
      });
      return;
    }

    const canvas = this.signatureCanvasRef?.nativeElement;
    if (!canvas) return;

    canvas.toBlob((blob) => {
      if (!blob) {
        this.messageService.add({
          severity: 'error',
          summary: 'Error',
          detail: 'Failed to capture signature.',
        });
        return;
      }

      const file = new File([blob], 'signature.png', { type: 'image/png' });
      this.proofSignatureLoading.set(true);
      this.routesApi.uploadRouteStopProofSignature(stop.id, file).subscribe({
        next: (updated) => {
          this.proofSignatureLoading.set(false);
          this.applyStopUpdate(updated);
          this.loadProofSignature(updated.id);
          this.clearSignature();
        },
        error: (err) => {
          this.proofSignatureLoading.set(false);
          this.messageService.add({
            severity: 'error',
            summary: 'Error',
            detail: err?.error?.message || err?.message || 'Failed to upload signature',
          });
        },
      });
    }, 'image/png');
  }

  private uploadProofPhoto(routeStopId: number, file: File, stopCameraAfter = false): void {
    this.proofPhotoLoading.set(true);
    this.routesApi.uploadRouteStopProofPhoto(routeStopId, file).subscribe({
      next: (updated) => {
        this.proofPhotoLoading.set(false);
        this.applyStopUpdate(updated);
        this.loadProofPhoto(updated.id);
        if (stopCameraAfter) {
          this.stopCamera();
        }
      },
      error: (err) => {
        this.proofPhotoLoading.set(false);
        this.messageService.add({
          severity: 'error',
          summary: 'Error',
          detail: err?.error?.message || err?.message || 'Failed to upload photo',
        });
      },
    });
  }

  clearSignature(): void {
    this.clearSignatureCanvas();
    this.signatureHasInk.set(false);
  }

  startSignature(event: PointerEvent): void {
    if (!this.canEditStops()) return;
    if (!this.signatureContext || !this.signatureCanvasRef) return;
    const point = this.getSignaturePoint(event);
    this.signatureContext.beginPath();
    this.signatureContext.moveTo(point.x, point.y);
    this.signatureDrawing = true;
    this.signatureHasInk.set(true);
    this.signatureCanvasRef.nativeElement.setPointerCapture(event.pointerId);
  }

  moveSignature(event: PointerEvent): void {
    if (!this.signatureDrawing || !this.signatureContext) return;
    const point = this.getSignaturePoint(event);
    this.signatureContext.lineTo(point.x, point.y);
    this.signatureContext.stroke();
  }

  endSignature(event?: PointerEvent): void {
    if (!this.signatureDrawing) return;
    this.signatureDrawing = false;
    if (event && this.signatureCanvasRef) {
      this.signatureCanvasRef.nativeElement.releasePointerCapture(event.pointerId);
    }
  }

  private initSignatureCanvas(): void {
    const canvas = this.signatureCanvasRef?.nativeElement;
    if (!canvas) return;
    const rect = canvas.getBoundingClientRect();
    if (rect.width === 0 || rect.height === 0) return;

    const ratio = window.devicePixelRatio || 1;
    canvas.width = Math.round(rect.width * ratio);
    canvas.height = Math.round(rect.height * ratio);

    const ctx = canvas.getContext('2d');
    if (!ctx) return;
    ctx.setTransform(ratio, 0, 0, ratio, 0, 0);
    ctx.lineWidth = 2;
    ctx.lineCap = 'round';
    ctx.strokeStyle = '#111827';
    this.signatureContext = ctx;
    this.clearSignatureCanvas();
  }

  private clearSignatureCanvas(): void {
    const canvas = this.signatureCanvasRef?.nativeElement;
    if (!canvas || !this.signatureContext) return;
    this.signatureContext.save();
    this.signatureContext.setTransform(1, 0, 0, 1, 0, 0);
    this.signatureContext.clearRect(0, 0, canvas.width, canvas.height);
    this.signatureContext.restore();
  }

  private getSignaturePoint(event: PointerEvent): { x: number; y: number } {
    const canvas = this.signatureCanvasRef?.nativeElement;
    if (!canvas) return { x: 0, y: 0 };
    const rect = canvas.getBoundingClientRect();
    return {
      x: event.clientX - rect.left,
      y: event.clientY - rect.top,
    };
  }

  private resetProofState(): void {
    const photoUrl = untracked(() => this.proofPhotoUrl());
    const signatureUrl = untracked(() => this.proofSignatureUrl());
    this.revokeProofUrl(photoUrl);
    this.revokeProofUrl(signatureUrl);
    this.proofPhotoUrl.set(null);
    this.proofSignatureUrl.set(null);
    this.proofPhotoLoading.set(false);
    this.proofSignatureLoading.set(false);
    this.cameraError.set(null);
    this.stopCamera();
    this.clearSignatureCanvas();
  }

  private loadProofPhoto(routeStopId: number): void {
    this.proofPhotoLoading.set(true);
    this.routesApi.getRouteStopProofPhoto(routeStopId).subscribe({
      next: (blob) => {
        this.proofPhotoLoading.set(false);
        this.setProofPhotoUrl(blob);
      },
      error: () => {
        this.proofPhotoLoading.set(false);
      },
    });
  }

  private loadProofSignature(routeStopId: number): void {
    this.proofSignatureLoading.set(true);
    this.routesApi.getRouteStopProofSignature(routeStopId).subscribe({
      next: (blob) => {
        this.proofSignatureLoading.set(false);
        this.setProofSignatureUrl(blob);
      },
      error: () => {
        this.proofSignatureLoading.set(false);
      },
    });
  }

  private setProofPhotoUrl(blob: Blob): void {
    this.revokeProofUrl(this.proofPhotoUrl());
    this.proofPhotoUrl.set(URL.createObjectURL(blob));
  }

  private setProofSignatureUrl(blob: Blob): void {
    this.revokeProofUrl(this.proofSignatureUrl());
    this.proofSignatureUrl.set(URL.createObjectURL(blob));
  }

  private revokeProofUrl(url: string | null): void {
    if (url) {
      URL.revokeObjectURL(url);
    }
  }

  private computeDurationMinutes(
    arrivedAt?: string | null,
    completedAt?: string | null,
  ): number | null {
    if (!arrivedAt || !completedAt) return null;
    const a = new Date(arrivedAt);
    const c = new Date(completedAt);
    if (Number.isNaN(a.getTime()) || Number.isNaN(c.getTime())) return null;
    const minutes = Math.round(Math.abs(c.getTime() - a.getTime()) / 60000);
    return Number.isFinite(minutes) ? minutes : null;
  }

  private static isPlannerMessage(message: RouteMessageDto): boolean {
    if (!message.plannerId) return false;
    if (message.status && message.status !== 'New') return false;
    return true;
  }
}
