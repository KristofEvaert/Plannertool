import {
  AfterViewInit,
  Component,
  computed,
  effect,
  inject,
  input,
  model,
  OnDestroy,
  output,
} from '@angular/core';
import {
  CacheEntry,
  LocationHoursDisplay,
  MarkerColorKey,
  RouteInfo,
  ServiceLocationExceptionDto,
  ServiceLocationMapDto,
  ServiceLocationOpeningHoursDto,
  ServiceLocationsMapResponseDto,
  ServiceTypeDto,
} from '@app/_models';
import { ServiceLocationsApiService } from '@services/service-locations-api.service';
import { toYmd } from '@utils/date.utils';
import * as L from 'leaflet';
import { firstValueFrom } from 'rxjs';

@Component({
  selector: 'app-map-with-legend',
  imports: [],
  templateUrl: './map-with-legend.html',
  styles: `
    :host {
      display: flex;
      flex-direction: column;
      gap: 1em;
    }
  `,
})
export class MapWithLegendComponent implements AfterViewInit, OnDestroy {
  readonly showPlannedLocations = model(true);

  readonly mapData = input.required<ServiceLocationsMapResponseDto | null>();
  readonly serviceTypes = input.required<ServiceTypeDto[]>();
  readonly selectedServiceTypeIds = input.required<number[]>();
  readonly selectedDate = input.required<Date>();
  readonly driverRoutes = input<Map<string, RouteInfo>>(new Map());
  readonly selectedDriverToolId = input<string | null>(null);

  readonly locationClick = output<ServiceLocationMapDto>();
  readonly locationDoubleClick = output<ServiceLocationMapDto>();
  readonly mapReady = output<L.Map>();

  readonly selectedServiceTypesLegend = computed(() => {
    const ids = this.selectedServiceTypeIds();
    const all = this.serviceTypes();
    return ids
      .slice(0, 5)
      .map((id) => all.find((t) => t.id === id))
      .filter((t): t is ServiceTypeDto => !!t);
  });

  private map: L.Map | null = null;
  private markers: L.Layer[] = [];

  readonly urgencyLegend: { key: MarkerColorKey; label: string }[] = [
    { key: 'green', label: 'Due in > 4 weeks' },
    { key: 'yellow', label: 'Due in 2–4 weeks' },
    { key: 'orange', label: 'Due in < 2 weeks' },
    { key: 'red', label: 'Overdue' },
    { key: 'white', label: 'Closed today' },
    { key: 'black', label: 'Already planned' },
  ];

  private readonly markerColors: Record<MarkerColorKey, string> = {
    green: '#16a34a',
    yellow: '#facc15',
    orange: '#f59e0b',
    red: '#dc2626',
    white: '#ffffff',
    black: '#111827',
  };

  private readonly serviceTypeShapes = [
    'circle',
    'square',
    'triangle',
    'diamond',
    'pentagon',
  ] as const;

  private locationHoursCache = new Map<string, CacheEntry<LocationHoursDisplay>>();
  private locationHoursRequests = new Map<string, Promise<LocationHoursDisplay>>();
  private readonly hoursCacheTtlMs = 30_000;

  private readonly serviceLocationsApi = inject(ServiceLocationsApiService);
  private lastMapData: ServiceLocationsMapResponseDto | null = null;

  constructor() {
    effect(() => {
      // Dependencies to trigger update
      const data = this.mapData();
      this.showPlannedLocations();
      this.selectedDate();
      this.driverRoutes();
      this.selectedDriverToolId();

      const isDataChange = data !== this.lastMapData;
      this.lastMapData = data;

      this.updateMap({ fitBounds: isDataChange });
    });
  }

  ngAfterViewInit(): void {
    this.initMap();
  }

  ngOnDestroy(): void {
    if (this.map) {
      this.map.remove();
      this.map = null;
    }
  }

  getLegendSwatchStyle(key: MarkerColorKey): Record<string, string> {
    const color = this.markerColors[key];
    return {
      backgroundColor: color,
      borderColor: key === 'black' ? '#ffffff' : '#000000',
    };
  }

  getLegendShapeClass(serviceTypeId: number): string {
    return `legend-shape-${this.getServiceTypeShape(serviceTypeId)}`;
  }

  onToggleShowPlannedLocations(event: Event): void {
    const checked = (event.target as HTMLInputElement | null)?.checked ?? true;
    this.showPlannedLocations.set(checked);
    // The effect will handle the update
  }

  refreshLocationMarkers(): void {
    this.updateMap({ fitBounds: false });
  }

  private getServiceTypeShape(serviceTypeId: number): (typeof this.serviceTypeShapes)[number] {
    const ids = this.selectedServiceTypeIds();
    const idx = Math.max(0, ids.indexOf(serviceTypeId));
    return this.serviceTypeShapes[idx % this.serviceTypeShapes.length];
  }

  private initMap(): void {
    if (this.map) {
      return;
    }

    const mapElement = document.getElementById('serviceLocationsMap');
    if (!mapElement) {
      console.error('Map element not found');
      return;
    }

    this.map = L.map('serviceLocationsMap');
    L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png').addTo(this.map);

    // Set default view to Belgium
    this.map.setView([51, 4.5], 9);

    this.mapReady.emit(this.map);

    // Initial update in case data was already ready
    this.updateMap({ fitBounds: true });
  }

  private updateMap(options: { fitBounds: boolean } = { fitBounds: false }): void {
    if (!this.map) {
      return;
    }

    // Clear existing service location markers (but keep driver marker and route)
    this.markers.forEach((marker) => this.map!.removeLayer(marker));
    this.markers = [];

    const data = this.mapData();
    if (!data || !data.items || data.items.length === 0) {
      return;
    }

    const currentRoute = this.getCurrentRoute();
    const selectedDay = this.selectedDate();
    const bounds: L.LatLngBoundsExpression = [];

    data.items.forEach((item) => {
      const isPlanned = item.status === 'Planned';
      if (isPlanned && !this.showPlannedLocations()) {
        return;
      }

      const hoursInfo =
        this.getCachedLocationHours(item.toolId, selectedDay) ??
        this.getHoursPlaceholderDisplay(selectedDay);
      const colorKey = this.getMarkerColorKey(item, selectedDay, hoursInfo);
      const fill = this.markerColors[colorKey];
      const stroke = this.getMarkerStrokeColor(colorKey);

      // Check if location is in current route and get its order
      const routeWaypoint = currentRoute?.waypoints.find(
        (w) => w.type === 'location' && w.toolId === item.toolId,
      );
      const isInRoute = !!routeWaypoint;
      const routeOrder =
        isInRoute && currentRoute
          ? this.getLocationNumber(
              currentRoute.waypoints.findIndex((w) => w === routeWaypoint),
              currentRoute.waypoints,
            )
          : null;

      // Marker shape depends on service type (max 5 shapes).
      const marker = this.createServiceLocationMarker(item, fill, stroke, isInRoute ? 22 : 18);

      // Tooltip on hover (click is used for route building).
      const tooltipContent = this.buildLocationTooltip(item, isInRoute, routeOrder, hoursInfo);

      marker.bindTooltip(tooltipContent, {
        direction: 'top',
        opacity: 0.95,
        sticky: true,
        className: 'map-location-tooltip',
      });
      marker.on('tooltipopen', () => {
        this.getLocationHoursDisplay(item.toolId, this.selectedDate(), true).then((display) => {
          marker.setTooltipContent(this.buildLocationTooltip(item, isInRoute, routeOrder, display));
          const updatedKey = this.getMarkerColorKey(item, this.selectedDate(), display);
          if (updatedKey !== colorKey) {
            const updatedFill = this.markerColors[updatedKey];
            const updatedStroke = this.getMarkerStrokeColor(updatedKey);
            const size = isInRoute ? 22 : 18;
            const shape = this.getServiceTypeShape(item.serviceTypeId);
            const svg = this.buildServiceLocationIconSvg(
              shape,
              size,
              updatedFill,
              updatedStroke,
              2,
            );
            marker.setIcon(
              L.divIcon({
                className: 'service-location-shape-marker',
                html: svg,
                iconSize: [size, size],
                iconAnchor: [size / 2, size / 2],
              }),
            );
          }
        });
      });

      if (!hoursInfo.isLoading && !hoursInfo.isClosed) {
        // no-op if cached and open
      } else {
        this.getLocationHoursDisplay(item.toolId, selectedDay).then((display) => {
          const updatedKey = this.getMarkerColorKey(item, selectedDay, display);
          if (updatedKey !== colorKey) {
            const updatedFill = this.markerColors[updatedKey];
            const updatedStroke = this.getMarkerStrokeColor(updatedKey);
            const shape = this.getServiceTypeShape(item.serviceTypeId);
            const svg = this.buildServiceLocationIconSvg(
              shape,
              isInRoute ? 22 : 18,
              updatedFill,
              updatedStroke,
              2,
            );
            marker.setIcon(
              L.divIcon({
                className: 'service-location-shape-marker',
                html: svg,
                iconSize: [isInRoute ? 22 : 18, isInRoute ? 22 : 18],
                iconAnchor: [(isInRoute ? 22 : 18) / 2, (isInRoute ? 22 : 18) / 2],
              }),
            );
          }
        });
      }

      // Click adds/moves (unless Ctrl is held for area selection).
      marker.on('click', (e) => {
        const ctrl = (e.originalEvent as MouseEvent | undefined)?.ctrlKey;
        if (ctrl) {
          return;
        }
        this.locationClick.emit(item);
      });

      // Add double click handler to remove from route
      marker.on('dblclick', (e) => {
        const ctrl = (e.originalEvent as MouseEvent | undefined)?.ctrlKey;
        if (ctrl) {
          return;
        }
        e.originalEvent?.stopPropagation();
        e.originalEvent?.preventDefault();

        this.locationDoubleClick.emit(item);
      });

      marker.addTo(this.map!);
      this.markers.push(marker);
      bounds.push([item.latitude, item.longitude] as [number, number]);
    });

    // Fit bounds only if we don't have a route (prevent zooming out when route is focused)
    if (options.fitBounds && bounds.length > 0 && !currentRoute) {
      this.map.fitBounds(bounds, { padding: [50, 50] });
    }
  }

  private getCurrentRoute(): RouteInfo | null {
    const toolId = this.selectedDriverToolId();
    if (!toolId) return null;
    return this.driverRoutes().get(toolId) ?? null;
  }

  private getLocationNumber(index: number, waypoints: any[]): number {
    let count = 0;
    for (let i = 0; i <= index; i++) {
      if (waypoints[i].type === 'location') {
        count++;
      }
    }
    return count;
  }

  private getMarkerColorKey(
    item: ServiceLocationMapDto,
    selectedDay: Date,
    hoursInfo?: LocationHoursDisplay,
  ): MarkerColorKey {
    if (item.status === 'Planned') {
      return 'black';
    }

    if (hoursInfo?.isClosed) {
      return 'white';
    }

    if (!item.dueDate) {
      return 'green';
    }

    const dueDate = new Date(item.dueDate);
    if (Number.isNaN(dueDate.getTime())) {
      return 'green';
    }

    const remainingDays = Math.floor(
      (this.toStartOfDay(dueDate).getTime() - this.toStartOfDay(selectedDay).getTime()) /
        86_400_000,
    );

    if (remainingDays < 0) {
      return 'red';
    }
    if (remainingDays <= 13) {
      return 'orange';
    }
    if (remainingDays <= 27) {
      return 'yellow';
    }
    return 'green';
  }

  private getMarkerStrokeColor(colorKey: MarkerColorKey): string {
    if (colorKey === 'white') {
      return '#111827';
    }
    if (colorKey === 'black') {
      return '#000000';
    }
    return '#1f2937';
  }

  private createServiceLocationMarker(
    item: ServiceLocationMapDto,
    fill: string,
    stroke: string,
    size: number,
  ): L.Marker {
    const shape = this.getServiceTypeShape(item.serviceTypeId);
    const svg = this.buildServiceLocationIconSvg(shape, size, fill, stroke, 2);
    return L.marker([item.latitude, item.longitude], {
      icon: L.divIcon({
        className: 'service-location-shape-marker',
        html: svg,
        iconSize: [size, size],
        iconAnchor: [size / 2, size / 2],
      }),
      keyboard: false,
    });
  }

  private buildServiceLocationIconSvg(
    shape: (typeof this.serviceTypeShapes)[number],
    size: number,
    fill: string,
    stroke: string,
    strokeWidth: number,
  ): string {
    const s = size;
    const half = s / 2;
    const pad = Math.max(1, strokeWidth);
    const min = pad;
    const max = s - pad;

    const svgOpen = `<svg xmlns="http://www.w3.org/2000/svg" width="${s}" height="${s}" viewBox="0 0 ${s} ${s}">`;
    const svgClose = `</svg>`;

    switch (shape) {
      case 'circle':
        return `${svgOpen}<circle cx="${half}" cy="${half}" r="${half - pad}" fill="${fill}" stroke="${stroke}" stroke-width="${strokeWidth}" />${svgClose}`;
      case 'square':
        return `${svgOpen}<rect x="${min}" y="${min}" width="${max - min}" height="${max - min}" rx="2" ry="2" fill="${fill}" stroke="${stroke}" stroke-width="${strokeWidth}" />${svgClose}`;
      case 'triangle': {
        const p1 = `${half},${min}`;
        const p2 = `${max},${max}`;
        const p3 = `${min},${max}`;
        return `${svgOpen}<polygon points="${p1} ${p2} ${p3}" fill="${fill}" stroke="${stroke}" stroke-width="${strokeWidth}" />${svgClose}`;
      }
      case 'diamond': {
        const p1 = `${half},${min}`;
        const p2 = `${max},${half}`;
        const p3 = `${half},${max}`;
        const p4 = `${min},${half}`;
        return `${svgOpen}<polygon points="${p1} ${p2} ${p3} ${p4}" fill="${fill}" stroke="${stroke}" stroke-width="${strokeWidth}" />${svgClose}`;
      }
      case 'pentagon': {
        // simple regular-ish pentagon
        const p1 = `${half},${min}`;
        const p2 = `${max},${half * 0.9}`;
        const p3 = `${half * 0.82},${max}`;
        const p4 = `${half * 0.18},${max}`;
        const p5 = `${min},${half * 0.9}`;
        return `${svgOpen}<polygon points="${p1} ${p2} ${p3} ${p4} ${p5}" fill="${fill}" stroke="${stroke}" stroke-width="${strokeWidth}" />${svgClose}`;
      }
    }
  }

  private buildLocationTooltip(
    item: ServiceLocationMapDto,
    isInRoute: boolean,
    routeOrder: number | null,
    hoursInfo?: LocationHoursDisplay,
  ): string {
    const popupContent = this.createPopupContent(item, hoursInfo);
    return isInRoute && routeOrder !== null
      ? `${popupContent}<br><b style="color: #3b82f6;">In Route (Stop #${routeOrder})</b>`
      : popupContent;
  }

  private createPopupContent(
    item: ServiceLocationMapDto,
    hoursInfo?: LocationHoursDisplay,
  ): string {
    const address = item.address?.trim();
    const planned = this.getPlannedRouteInfo(item);
    const plannedDriverName = planned?.driverName;
    const plannedDate = planned?.date;

    // Hover tooltip content (avoid click popups since clicking is used for route building).
    let content = `<b>${item.name}</b><br>`;
    content += `<span class="map-location-address">Address: ${address || 'N/A'}</span><br>`;
    if (plannedDriverName) {
      content += `<span>Driver: ${plannedDriverName}</span><br>`;
    }
    if (plannedDate) {
      content += `<span>Planned: ${plannedDate}</span><br>`;
    }
    if (hoursInfo) {
      const hoursClass = hoursInfo.isClosed
        ? 'map-location-hours map-location-hours-closed'
        : 'map-location-hours';
      const loadingClass = hoursInfo.isLoading ? ' map-location-hours-loading' : '';
      content += `<span class="${hoursClass}${loadingClass}">${hoursInfo.label}</span><br>`;
    }
    content += `<span>Service: ${item.serviceMinutes} min</span>`;
    return content;
  }

  private getPlannedRouteInfo(
    item: ServiceLocationMapDto,
  ): { driverName: string; date: string } | null {
    if (item.plannedDriverName && item.plannedDate) {
      return {
        driverName: item.plannedDriverName,
        date: toYmd(new Date(item.plannedDate)),
      };
    }

    const routes = this.driverRoutes();
    for (const route of routes.values()) {
      if (route.waypoints.some((w) => w.type === 'location' && w.toolId === item.toolId)) {
        return {
          driverName: route.driver.name,
          date: toYmd(this.selectedDate()),
        };
      }
    }

    if (item.plannedDriverName) {
      return {
        driverName: item.plannedDriverName,
        date: item.plannedDate ? toYmd(new Date(item.plannedDate)) : '',
      };
    }

    return null;
  }

  private toStartOfDay(value: Date): Date {
    const date = new Date(value);
    date.setHours(0, 0, 0, 0);
    return date;
  }

  // Location Hours Logic
  private getLocationHoursKey(toolId: string, date: Date): string {
    return `${toolId}-${toYmd(date)}`;
  }

  private getDayLabel(date: Date): string {
    const labels = ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat'];
    return labels[date.getDay()] ?? '';
  }

  private getHoursPlaceholderDisplay(date: Date): LocationHoursDisplay {
    const dayLabel = this.getDayLabel(date);
    return {
      label: `Hours (${dayLabel}): Loading...`,
      isClosed: false,
      isLoading: true,
    };
  }

  private formatHourRanges(
    openTime?: string | null,
    closeTime?: string | null,
    openTime2?: string | null,
    closeTime2?: string | null,
  ): string | null {
    const ranges: string[] = [];
    if (openTime && closeTime) {
      ranges.push(`${openTime}-${closeTime}`);
    }
    if (openTime2 && closeTime2) {
      ranges.push(`${openTime2}-${closeTime2}`);
    }
    return ranges.length > 0 ? ranges.join(', ') : null;
  }

  private buildLocationHoursDisplay(
    hours: ServiceLocationOpeningHoursDto[],
    exceptions: ServiceLocationExceptionDto[],
    date: Date,
  ): LocationHoursDisplay {
    const dayLabel = this.getDayLabel(date);
    const prefix = `Hours (${dayLabel}): `;
    const dateKey = toYmd(date);
    const exception = exceptions.find((ex) => toYmd(new Date(ex.date)) === dateKey);

    if (exception) {
      if (exception.isClosed) {
        return { label: `${prefix}Closed`, isClosed: true };
      }
      const ranges = this.formatHourRanges(exception.openTime, exception.closeTime);
      return {
        label: `${prefix}${ranges ?? 'Open all day'}`,
        isClosed: false,
      };
    }

    const dayOfWeek = date.getDay();
    const standard = hours.find((h) => h.dayOfWeek === dayOfWeek);
    if (!standard) {
      return { label: `${prefix}Open all day`, isClosed: false };
    }

    if (standard.isClosed) {
      return { label: `${prefix}Closed`, isClosed: true };
    }

    const ranges = this.formatHourRanges(
      standard.openTime,
      standard.closeTime,
      standard.openTime2,
      standard.closeTime2,
    );

    return {
      label: `${prefix}${ranges ?? 'Open all day'}`,
      isClosed: false,
    };
  }

  private isCacheFresh(fetchedAt: number): boolean {
    return Date.now() - fetchedAt < this.hoursCacheTtlMs;
  }

  private getCachedLocationHours(toolId: string, date: Date): LocationHoursDisplay | null {
    const key = this.getLocationHoursKey(toolId, date);
    const cached = this.locationHoursCache.get(key);
    if (!cached || !this.isCacheFresh(cached.fetchedAt)) {
      return null;
    }
    return cached.value;
  }

  private async getLocationHoursDisplay(
    toolId: string,
    date: Date,
    forceRefresh = false,
  ): Promise<LocationHoursDisplay> {
    const key = this.getLocationHoursKey(toolId, date);
    const cached = this.locationHoursCache.get(key);
    if (!forceRefresh && cached && this.isCacheFresh(cached.fetchedAt)) {
      return cached.value;
    }

    const inFlight = this.locationHoursRequests.get(key);
    if (inFlight) {
      return inFlight;
    }

    const request = (async () => {
      try {
        const [hours, exceptions] = await Promise.all([
          firstValueFrom(this.serviceLocationsApi.getOpeningHours(toolId)),
          firstValueFrom(this.serviceLocationsApi.getExceptions(toolId)),
        ]);
        return this.buildLocationHoursDisplay(hours, exceptions, date);
      } catch {
        const dayLabel = this.getDayLabel(date);
        return { label: `Hours (${dayLabel}): Unavailable`, isClosed: false };
      }
    })();

    this.locationHoursRequests.set(key, request);
    request
      .then((display) => {
        this.locationHoursCache.set(key, { value: display, fetchedAt: Date.now() });
      })
      .finally(() => {
        this.locationHoursRequests.delete(key);
      });

    return request;
  }
}
