import { CommonModule } from '@angular/common';
import {
  AfterViewInit,
  Component,
  computed,
  DestroyRef,
  effect,
  inject,
  signal,
  untracked,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import * as L from 'leaflet';
import { MessageService } from 'primeng/api';
import { ButtonModule } from 'primeng/button';
import { CheckboxModule } from 'primeng/checkbox';
import { DatePickerModule } from 'primeng/datepicker';
import { SelectModule } from 'primeng/select';
import { TagModule } from 'primeng/tag';
import { ToastModule } from 'primeng/toast';
import { catchError, forkJoin, interval, map, of, startWith, Subscription, switchMap } from 'rxjs';

import { HelpManualComponent } from '@components';
import type { RouteMessageDto } from '@models';
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
interface MessageDriverFilterOption {
  label: string;
  value: number | null;
}
interface MessageRow extends RouteMessageDto {
  indent: number;
  isReply: boolean;
  threadKey: string;
}
interface MessageThread {
  key: string;
  routeId: number;
  routeStopId: number | null;
  driverId: number;
  driverName: string;
  latestCreatedUtc: string;
  messages: MessageRow[];
}

type DriverPosition = {
  lat: number;
  lng: number;
  mode: 'at-stop' | 'between' | 'start' | 'finished';
} | null;

@Component({
  selector: 'app-route-followup',
  imports: [
    CommonModule,
    FormsModule,
    DatePickerModule,
    SelectModule,
    ButtonModule,
    CheckboxModule,
    TagModule,
    ToastModule,
    HelpManualComponent,
  ],
  providers: [MessageService],
  templateUrl: './route-followup.page.html',
  styleUrl: './route-followup.page.css',
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
  selectedStopId = signal<number | null>(null);
  proofPhotoUrl = signal<string | null>(null);
  proofSignatureUrl = signal<string | null>(null);
  proofPhotoLoading = signal(false);
  proofSignatureLoading = signal(false);

  selectedStop = computed(() => {
    const route = this.route();
    const stopId = this.selectedStopId();
    if (!route || !stopId) return null;
    return route.stops.find((s) => s.id === stopId) ?? null;
  });

  messageDriverFilterId = signal<number | null>(null);
  messageStatusFilter = signal('');
  replyTarget = signal<RouteMessageDto | null>(null);
  replyText = signal('');
  replyCategory = signal('Info');
  sendingReply = signal(false);
  replyThreadKey = computed(() => {
    const target = this.replyTarget();
    return target ? RouteFollowupPage.threadKeyFor(target) : null;
  });
  composeTarget = signal('all');
  composeMessageText = signal('');
  composeCategory = signal('Info');
  sendingCompose = signal(false);

  private map: L.Map | null = null;
  private layerGroup: L.LayerGroup | null = null;
  private pollSub: Subscription | null = null;

  driversForSelectedOwner = computed(() => {
    const ownerId = this.selectedOwnerId();
    const all = this.driverOptions();
    if (!ownerId) return all;
    return all.filter((d) => d.ownerId === ownerId);
  });

  messageDriverOptions = computed<MessageDriverFilterOption[]>(() => {
    const seen = new Map<number, string>();
    for (const message of this.messages()) {
      if (!seen.has(message.driverId)) {
        seen.set(message.driverId, message.driverName || `Driver #${message.driverId}`);
      }
    }
    const options = Array.from(seen.entries())
      .map(([value, label]) => ({ label, value }))
      .sort((a, b) => a.label.localeCompare(b.label));
    return [{ label: 'All drivers', value: null }, ...options];
  });

  composeDriverOptions = computed(() => {
    const options = this.driversForSelectedOwner()
      .map((driver) => ({ label: driver.label, value: driver.value }))
      .sort((a, b) => a.label.localeCompare(b.label));
    return [{ label: 'All drivers', value: 'all' }, ...options];
  });

  isToday = computed(() => RouteFollowupPage.isSameDay(this.selectedDate(), new Date()));

  unreadCount = computed(() => this.messages().filter((m) => m.status === 'New').length);

  filteredMessages = computed(() => {
    const driverId = this.messageDriverFilterId();
    const status = this.messageStatusFilter();
    return this.messages().filter((message) => {
      if (driverId != null && message.driverId !== driverId) {
        return false;
      }
      if (status && message.status !== status) {
        return false;
      }
      return true;
    });
  });

  messageThreads = computed<MessageThread[]>(() => {
    const items = this.filteredMessages();
    if (items.length === 0) return [];

    const threads = new Map<string, MessageThread>();
    for (const message of items) {
      const key = RouteFollowupPage.threadKeyFor(message);
      let thread = threads.get(key);
      if (!thread) {
        thread = {
          key,
          routeId: message.routeId,
          routeStopId: message.routeStopId ?? null,
          driverId: message.driverId,
          driverName: message.driverName || `Driver #${message.driverId}`,
          latestCreatedUtc: message.createdUtc,
          messages: [],
        };
        threads.set(key, thread);
      }

      const isReply = RouteFollowupPage.isPlannerMessage(message);
      thread.messages.push({
        ...message,
        indent: isReply ? 1 : 0,
        isReply,
        threadKey: key,
      });

      if (
        RouteFollowupPage.messageTimeValue(message.createdUtc) >
        RouteFollowupPage.messageTimeValue(thread.latestCreatedUtc)
      ) {
        thread.latestCreatedUtc = message.createdUtc;
      }
    }

    for (const thread of threads.values()) {
      thread.messages.sort(
        (a, b) =>
          RouteFollowupPage.messageTimeValue(a.createdUtc) -
          RouteFollowupPage.messageTimeValue(b.createdUtc),
      );
    }

    return Array.from(threads.values()).sort(
      (a, b) =>
        RouteFollowupPage.messageTimeValue(b.latestCreatedUtc) -
        RouteFollowupPage.messageTimeValue(a.latestCreatedUtc),
    );
  });

  readonly messageStatusOptions = [
    { label: 'All statuses', value: '' },
    { label: 'New', value: 'New' },
    { label: 'Read', value: 'Read' },
    { label: 'Resolved', value: 'Resolved' },
  ];

  readonly messageCategoryOptions = [
    { label: 'Info', value: 'Info' },
    { label: 'Delay', value: 'Delay' },
    { label: 'Issue', value: 'Issue' },
    { label: 'Other', value: 'Other' },
  ];

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
        this.replyTarget.set(null);
        this.replyText.set('');
        return;
      }
      this.loadMessages(ownerId);
    });

    effect(() => {
      const driverId = this.messageDriverFilterId();
      if (driverId == null) return;
      const exists = this.messages().some((m) => m.driverId === driverId);
      if (!exists) {
        this.messageDriverFilterId.set(null);
      }
    });

    effect(() => {
      const options = this.composeDriverOptions();
      const current = this.composeTarget();
      if (options.some((o) => o.value === current)) return;
      this.composeTarget.set(options[0]?.value ?? 'all');
    });

    effect(() => {
      const target = this.replyTarget();
      if (!target) return;
      const exists = this.messages().some((m) => m.id === target.id);
      if (!exists) {
        this.replyTarget.set(null);
        this.replyText.set('');
      }
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

    effect(() => {
      const route = this.route();
      if (!route || route.stops.length === 0) {
        this.selectedStopId.set(null);
        return;
      }
      const selectedId = this.selectedStopId();
      if (selectedId && route.stops.some((stop) => stop.id === selectedId)) {
        return;
      }
      this.selectedStopId.set(route.stops[0].id);
    });

    effect(() => {
      const stop = this.selectedStop();
      this.resetProofState();
      if (!stop) return;
      if (stop.hasProofPhoto) {
        this.loadProofPhoto(stop.id);
      }
      if (stop.hasProofSignature) {
        this.loadProofSignature(stop.id);
      }
    });
  }

  ngAfterViewInit(): void {
    this.ensureMap();
    this.refreshMap();

    // Ensure we stop polling on destroy.
    this.destroyRef.onDestroy(() => {
      this.stopPolling();
      this.resetProofState();
    });
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

  onStopClick(stop: RouteStopDto): void {
    this.selectedStopId.set(stop.id);
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

  private resetProofState(): void {
    const photoUrl = untracked(() => this.proofPhotoUrl());
    const signatureUrl = untracked(() => this.proofSignatureUrl());
    this.revokeProofUrl(photoUrl);
    this.revokeProofUrl(signatureUrl);
    this.proofPhotoUrl.set(null);
    this.proofSignatureUrl.set(null);
    this.proofPhotoLoading.set(false);
    this.proofSignatureLoading.set(false);
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

  messageSenderLabel(message: RouteMessageDto): string {
    if (RouteFollowupPage.isPlannerMessage(message)) {
      return 'Planner';
    }
    return message.driverName || `Driver #${message.driverId}`;
  }

  sendPlannerMessage(): void {
    const ownerId = this.selectedOwnerId();
    if (!ownerId) {
      this.messageService.add({
        severity: 'warn',
        summary: 'Validation',
        detail: 'Select an owner before sending a message.',
      });
      return;
    }

    const messageText = this.composeMessageText().trim();
    if (!messageText) {
      this.messageService.add({
        severity: 'warn',
        summary: 'Validation',
        detail: 'Message is required.',
      });
      return;
    }

    const targetValue = this.composeTarget();
    const drivers = this.driversForSelectedOwner();
    const targetDrivers =
      targetValue === 'all' ? drivers : drivers.filter((driver) => driver.value === targetValue);

    if (targetDrivers.length === 0) {
      this.messageService.add({
        severity: 'warn',
        summary: 'Validation',
        detail: 'No drivers available for the selected owner.',
      });
      return;
    }

    const date = this.selectedDate();
    this.sendingCompose.set(true);

    const tasks = targetDrivers.map((driver) =>
      this.routesApi.getDriverDayRoute(date, driver.value, ownerId, false).pipe(
        catchError(() => of(null)),
        switchMap((route) => {
          if (!route) {
            return of({
              driverName: driver.label,
              status: 'no-route' as const,
              message: null as RouteMessageDto | null,
            });
          }
          return this.routeMessagesApi
            .createMessage({
              routeId: route.id,
              routeStopId: null,
              messageText,
              category: this.composeCategory(),
            })
            .pipe(
              map((created) => ({
                driverName: driver.label,
                status: 'sent' as const,
                message: created,
              })),
              catchError(() =>
                of({
                  driverName: driver.label,
                  status: 'failed' as const,
                  message: null,
                }),
              ),
            );
        }),
      ),
    );

    forkJoin(tasks).subscribe({
      next: (results) => {
        this.sendingCompose.set(false);
        let sent = 0;
        let noRoute = 0;
        let failed = 0;
        for (const result of results) {
          if (result.status === 'sent' && result.message) {
            sent += 1;
            this.upsertMessage(result.message);
          } else if (result.status === 'no-route') {
            noRoute += 1;
          } else {
            failed += 1;
          }
        }

        if (sent > 0) {
          this.composeMessageText.set('');
        }

        const parts: string[] = [];
        if (sent > 0) {
          parts.push(`Sent ${sent} message${sent === 1 ? '' : 's'}.`);
        }
        if (noRoute > 0) {
          parts.push(`${noRoute} driver${noRoute === 1 ? ' has' : 's have'} no route.`);
        }
        if (failed > 0) {
          parts.push(`${failed} failed to send.`);
        }

        this.messageService.add({
          severity: failed > 0 || sent === 0 ? 'warn' : 'success',
          summary: sent > 0 ? 'Messages sent' : 'No messages sent',
          detail: parts.join(' '),
        });
      },
      error: () => {
        this.sendingCompose.set(false);
        this.messageService.add({
          severity: 'error',
          summary: 'Error',
          detail: 'Failed to send messages.',
        });
      },
    });
  }

  startReply(message: RouteMessageDto): void {
    this.replyTarget.set(message);
    this.replyText.set('');
    this.replyCategory.set(message.category || 'Info');
  }

  cancelReply(): void {
    this.replyTarget.set(null);
    this.replyText.set('');
  }

  sendReply(): void {
    const target = this.replyTarget();
    if (!target) return;
    const messageText = this.replyText().trim();
    if (!messageText) {
      this.messageService.add({
        severity: 'warn',
        summary: 'Validation',
        detail: 'Message is required.',
      });
      return;
    }

    this.sendingReply.set(true);
    this.routeMessagesApi
      .createMessage({
        routeId: target.routeId,
        routeStopId: target.routeStopId ?? null,
        messageText,
        category: this.replyCategory(),
      })
      .subscribe({
        next: (created) => {
          this.sendingReply.set(false);
          this.replyText.set('');
          this.replyTarget.set(null);
          this.upsertMessage(created);
          this.messageService.add({
            severity: 'success',
            summary: 'Reply sent',
            detail: 'Driver has been notified.',
          });
        },
        error: (err) => {
          this.sendingReply.set(false);
          this.messageService.add({
            severity: 'error',
            summary: 'Error',
            detail: err?.error?.message || err?.message || 'Failed to send reply',
          });
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

  private static threadKeyFor(message: RouteMessageDto): string {
    return `${message.routeId}-${message.routeStopId ?? 'route'}`;
  }

  private static isPlannerMessage(message: RouteMessageDto): boolean {
    if (!message.plannerId) return false;
    if (message.status && message.status !== 'New') return false;
    return true;
  }

  private static messageTimeValue(utcIso?: string): number {
    if (!utcIso) return 0;
    const time = Date.parse(utcIso);
    return Number.isNaN(time) ? 0 : time;
  }

  private static isSameDay(a: Date, b: Date): boolean {
    const aa = new Date(a);
    aa.setHours(0, 0, 0, 0);
    const bb = new Date(b);
    bb.setHours(0, 0, 0, 0);
    return aa.getTime() === bb.getTime();
  }
}
