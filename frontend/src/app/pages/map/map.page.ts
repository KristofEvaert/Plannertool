import { Component, inject, signal, AfterViewInit, OnDestroy, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ButtonModule } from 'primeng/button';
import { DropdownModule } from 'primeng/dropdown';
import { MultiSelectModule } from 'primeng/multiselect';
import { DatePickerModule } from 'primeng/datepicker';
import { ToastModule } from 'primeng/toast';
import { MessageService } from 'primeng/api';
import * as L from 'leaflet';
import { HttpClient } from '@angular/common/http';
import { ServiceTypesApiService } from '@services/service-types-api.service';
import { ServiceLocationOwnersApiService } from '@services/service-location-owners-api.service';
import { DriversApiService } from '@services/drivers-api.service';
import { DriverAvailabilityApiService } from '@services/driver-availability-api.service';
import { ServiceLocationsApiService } from '@services/service-locations-api.service';
import { RoutesApiService, type CreateRouteRequest, type CreateRouteStopRequest } from '@services/routes-api.service';
import { WeightTemplatesApiService } from '@services/weight-templates-api.service';
import { ExportsApiService } from '@services/exports-api.service';
import { AuthService } from '@services/auth.service';
import type { ServiceTypeDto } from '@models/service-type.model';
import type { ServiceLocationOwnerDto } from '@services/service-location-owners-api.service';
import type { DriverDto, DriverAvailabilityDto } from '@models/driver.model';
import type { WeightTemplateDto } from '@models/weight-template.model';
import { environment } from '@environments/environment';
import { toYmd } from '@utils/date.utils';
import { firstValueFrom } from 'rxjs';

interface ServiceLocationMapDto {
  toolId: string;
  erpId: number;
  name: string;
  address?: string;
  latitude: number;
  longitude: number;
  dueDate: string;
  priorityDate?: string;
  orderDate: string;
  serviceTypeId: number;
  status: string; // Open / Planned
  serviceMinutes: number;
  plannedDate?: string;
  plannedDriverName?: string;
}

interface ServiceLocationsMapResponseDto {
  from: string;
  to: string;
  totalCount: number;
  minOrderDate?: string;
  maxOrderDate?: string;
  items: ServiceLocationMapDto[];
}

interface DriverWithAvailability {
  driver: DriverDto;
  availability: DriverAvailabilityDto | null;
}

interface RouteWaypoint {
  type: 'driver-start' | 'location' | 'driver-end';
  name: string;
  latitude: number;
  longitude: number;
  serviceMinutes?: number;
  erpId?: number;
}

interface RouteOverride {
  address?: string;
  latitude?: number;
  longitude?: number;
}

interface RouteInfo {
  driver: DriverDto;
  waypoints: RouteWaypoint[];
  totalDistanceKm: number;
  totalTimeMinutes: number;
  roadGeometry?: [number, number][]; // [lat, lng] points for road-following polyline
  startOverride?: RouteOverride;
  endOverride?: RouteOverride;
}

interface LocationWindowInfo {
  isClosed: boolean;
  openMinute: number;
  closeMinute: number;
  label: string;
}

interface ArrivalWindow {
  startMinute: number;
  endMinute: number;
}

@Component({
  selector: 'app-map',
  imports: [
    CommonModule,
    FormsModule,
    ButtonModule,
    DropdownModule,
    MultiSelectModule,
    DatePickerModule,
    ToastModule,
  ],
  providers: [MessageService],
  templateUrl: './map.page.html',
  styleUrl: './map.page.css',
  standalone: true,
})
export class MapPage implements AfterViewInit, OnDestroy {
  private readonly serviceTypeShapes = ['circle', 'square', 'triangle', 'diamond', 'pentagon'] as const;
  private getServiceTypeShape(serviceTypeId: number): (typeof this.serviceTypeShapes)[number] {
    const ids = this.selectedServiceTypeIds();
    const idx = Math.max(0, ids.indexOf(serviceTypeId));
    return this.serviceTypeShapes[idx % this.serviceTypeShapes.length];
  }

  getLegendShapeClass(serviceTypeId: number): string {
    return `legend-shape-${this.getServiceTypeShape(serviceTypeId)}`;
  }

  private buildServiceLocationIconSvg(
    shape: (typeof this.serviceTypeShapes)[number],
    size: number,
    fill: string,
    stroke: string,
    strokeWidth: number
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

  private createServiceLocationMarker(
    item: ServiceLocationMapDto,
    fill: string,
    stroke: string,
    size: number
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
  private readonly http = inject(HttpClient);
  private readonly serviceTypesApi = inject(ServiceTypesApiService);
  private readonly ownersApi = inject(ServiceLocationOwnersApiService);
  private readonly driversApi = inject(DriversApiService);
  private readonly driverAvailabilityApi = inject(DriverAvailabilityApiService);
  private readonly serviceLocationsApi = inject(ServiceLocationsApiService);
  private readonly routesApi = inject(RoutesApiService);
  private readonly weightTemplatesApi = inject(WeightTemplatesApiService);
  private readonly exportsApi = inject(ExportsApiService);
  private readonly messageService = inject(MessageService);
  private readonly auth = inject(AuthService);

  async autoGenerateForAllDrivers(): Promise<void> {
    const ownerId = this.selectedOwnerId();
    const mapItems = this.mapData()?.items ?? [];
    const selected = this.selectedDriver();

    if (!ownerId) {
      this.messageService.add({ severity: 'warn', summary: 'Select owner', detail: 'Select an owner first.' });
      return;
    }

    if (mapItems.length === 0) {
      this.messageService.add({ severity: 'warn', summary: 'No locations', detail: 'Load the map points first.' });
      return;
    }

    if (selected && selected.availability) {
      this.autoGenerateLoading.set(true);
      try {
        const result = await this.routesApi
          .autoGenerateRoute(
            this.selectedDate(),
            selected.driver.toolId,
            ownerId,
            mapItems.map((m) => m.toolId),
            {
              time: this.weightTime(),
              distance: this.weightDistance(),
              date: this.weightDate(),
              cost: this.weightCost(),
              overtime: this.weightOvertime(),
            },
            this.enforceServiceTypeMatch(),
            this.selectedWeightTemplateId() ?? undefined
          )
          .toPromise();
        if (result) {
          this.messageService.add({
            severity: 'success',
            summary: 'Route generated',
            detail: `Updated ${selected.driver.name}'s route.`,
          });
        }
        await this.loadExistingRoutes();
        this.refreshMapDataAfterRouteSave();
      } catch (err: any) {
        this.messageService.add({
          severity: 'error',
          summary: 'Auto-generate failed',
          detail: err?.error?.message || err?.message || 'Failed to generate route',
        });
      } finally {
        this.autoGenerateLoading.set(false);
      }
      return;
    }

    if (selected && !selected.availability) {
      this.messageService.add({
        severity: 'warn',
        summary: 'Driver unavailable',
        detail: `${selected.driver.name} is not available on the selected date`,
      });
      return;
    }

    const toolIds = mapItems.map((m) => m.toolId);
    this.autoGenerateLoading.set(true);
    try {
      const result = await this.routesApi
        .autoGenerateRoutesForAll(this.selectedDate(), ownerId, toolIds, {
          time: this.weightTime(),
          distance: this.weightDistance(),
          date: this.weightDate(),
          cost: this.weightCost(),
          overtime: this.weightOvertime(),
        }, this.enforceServiceTypeMatch(), this.selectedWeightTemplateId() ?? undefined)
        .toPromise();
      const updated = result?.routes?.length ?? 0;
      const skipped = result?.skippedDrivers?.length ?? 0;
      this.messageService.add({
        severity: 'success',
        summary: 'Routes generated',
        detail: `Updated ${updated} driver(s)` + (skipped > 0 ? `, skipped ${skipped}` : ''),
      });
      await this.loadExistingRoutes();
      this.refreshMapDataAfterRouteSave(); // refresh statuses/markers
    } catch (err: any) {
      this.messageService.add({
        severity: 'error',
        summary: 'Auto-generate failed',
        detail: err?.error?.message || err?.message || 'Failed to generate routes',
      });
    } finally {
      this.autoGenerateLoading.set(false);
    }
  }

  async autoGenerateForPeriod(): Promise<void> {
    const ownerId = this.selectedOwnerId();
    const mapItems = this.mapData()?.items ?? [];

    if (!ownerId) {
      this.messageService.add({ severity: 'warn', summary: 'Select owner', detail: 'Select an owner first.' });
      return;
    }

    if (mapItems.length === 0) {
      this.messageService.add({ severity: 'warn', summary: 'No locations', detail: 'Load the map points first.' });
      return;
    }

    const start = new Date(this.fromDate());
    const end = new Date(this.toDate());
    start.setHours(0, 0, 0, 0);
    end.setHours(0, 0, 0, 0);

    if (start > end) {
      this.messageService.add({ severity: 'warn', summary: 'Invalid range', detail: 'From date must be before To date.' });
      return;
    }

    this.autoGeneratePeriodLoading.set(true);

    let totalUpdated = 0;
    let totalSkipped = 0;
    let lastProcessedDate: Date | null = null;

    try {
      for (let current = new Date(start); current <= end; current.setDate(current.getDate() + 1)) {
        const currentMap = this.mapData();
        const hasOpen = currentMap?.items?.some((item) => item.status === 'Open') ?? false;
        if (!hasOpen) {
          break;
        }

        this.selectedDate.set(new Date(current));
        await this.onDateChange();

        const toolIds = (this.mapData()?.items ?? []).map((m) => m.toolId);
        if (toolIds.length === 0) {
          break;
        }

        try {
          const result = await this.routesApi
            .autoGenerateRoutesForAll(this.selectedDate(), ownerId, toolIds, {
              time: this.weightTime(),
              distance: this.weightDistance(),
              date: this.weightDate(),
              cost: this.weightCost(),
              overtime: this.weightOvertime(),
            }, this.enforceServiceTypeMatch(), this.selectedWeightTemplateId() ?? undefined)
            .toPromise();
          totalUpdated += result?.routes?.length ?? 0;
          totalSkipped += result?.skippedDrivers?.length ?? 0;
          lastProcessedDate = new Date(current);
        } catch (err: any) {
          this.messageService.add({
            severity: 'warn',
            summary: `Auto-generate skipped ${toYmd(current)}`,
            detail: err?.error?.message || err?.message || 'Failed to generate routes for this day',
          });
        }

        await this.loadMapDataAsync(this.lastMapQuery, { reloadRoutes: false, silent: true });
        await this.loadExistingRoutesAsync();
        this.refreshLocationMarkers();
      }
    } finally {
      this.autoGeneratePeriodLoading.set(false);
    }

    if (lastProcessedDate) {
      this.selectedDate.set(new Date(lastProcessedDate));
      await this.onDateChange();
    }

    this.messageService.add({
      severity: 'success',
      summary: 'Period generation complete',
      detail: `Updated ${totalUpdated} driver(s)` + (totalSkipped > 0 ? `, skipped ${totalSkipped}` : ''),
    });
  }

  exportRoutes(): void {
    const ownerId = this.selectedOwnerId();
    if (!ownerId) {
      this.messageService.add({ severity: 'warn', summary: 'Select owner', detail: 'Select an owner first.' });
      return;
    }

    const from = this.fromDate();
    const to = this.toDate();
    const serviceTypeId = this.selectedServiceTypeIds()[0];

    if (!serviceTypeId) {
      this.messageService.add({
        severity: 'warn',
        summary: 'Select service type',
        detail: 'Select at least one service type.',
      });
      return;
    }

    this.exportsApi.exportRoutes(from, to, ownerId, serviceTypeId).subscribe({
      next: (blob) => {
        const url = window.URL.createObjectURL(blob);
        const link = document.createElement('a');
        link.href = url;
        link.download = `route-export-${toYmd(from)}-${toYmd(to)}.xlsx`;
        link.click();
        window.URL.revokeObjectURL(url);
      },
      error: (err) => {
        this.messageService.add({
          severity: 'error',
          summary: 'Export failed',
          detail: err?.error?.message || err?.message || 'Failed to export routes',
        });
      },
    });
  }

  // Data
  serviceTypes = signal<ServiceTypeDto[]>([]);
  owners = signal<ServiceLocationOwnerDto[]>([]);
  selectedServiceTypeIds = signal<number[]>([]);
  weightTime = signal(1);
  weightDistance = signal(1);
  weightDate = signal(1);
  weightCost = signal(1);
  weightOvertime = signal(1);
  weightTemplates = signal<WeightTemplateDto[]>([]);
  selectedWeightTemplateId = signal<number | null>(null);
  enforceServiceTypeMatch = signal(true);
  selectedWeightTemplate = computed(() => {
    const id = this.selectedWeightTemplateId();
    return this.weightTemplates().find((template) => template.id === id) ?? null;
  });
  activeWeightTime = computed(() => this.selectedWeightTemplate()?.weightTravelTime ?? this.weightTime());
  activeWeightDistance = computed(() => this.selectedWeightTemplate()?.weightDistance ?? this.weightDistance());
  activeWeightDate = computed(() => this.selectedWeightTemplate()?.weightDate ?? this.weightDate());
  activeWeightCost = computed(() => this.selectedWeightTemplate()?.weightCost ?? this.weightCost());
  activeWeightOvertime = computed(() => this.selectedWeightTemplate()?.weightOvertime ?? this.weightOvertime());
  selectedServiceTypesLegend = computed(() => {
    const ids = this.selectedServiceTypeIds();
    const all = this.serviceTypes();
    return ids
      .slice(0, 5)
      .map((id) => all.find((t) => t.id === id))
      .filter((t): t is ServiceTypeDto => !!t);
  });
  selectedOwnerId = signal<number | null>(null);
  
  // Date range
  fromDate = signal<Date>(new Date());
  toDate = signal<Date>(new Date(Date.now() + 60 * 24 * 60 * 60 * 1000)); // +60 days
  
  // Map data
  mapData = signal<ServiceLocationsMapResponseDto | null>(null);
  loading = signal(false);
  showPlannedLocations = signal(true);
  private lastMapQuery: {
    ownerId: number;
    serviceTypeIds: number[];
    from: string;
    to: string;
  } | null = null;
  
  // Driver availability
  selectedDate = signal<Date>(new Date());
  driversWithAvailability = signal<DriverWithAvailability[]>([]);
  loadingDrivers = signal(false);
  selectedDriver = signal<DriverWithAvailability | null>(null);
  
  // Route building - store routes per driver
  driverRoutes = signal<Map<string, RouteInfo>>(new Map());
  isBuildingRoute = signal(false);
  autoGenerateLoading = signal(false);
  autoGeneratePeriodLoading = signal(false);
  
  // Map
  private map: L.Map | null = null;
  private markers: L.Layer[] = [];
  private driverMarker: L.Marker | null = null;
  private routePolylines: Map<string, L.Polyline> = new Map();
  private routeMarkers: Map<string, L.Marker[]> = new Map();
  private locationWindowCache = new Map<string, LocationWindowInfo | null>();
  private selectionStart: L.LatLng | null = null;
  private selectionRect: L.Rectangle | null = null;
  private isAreaSelecting = false;

  // Prevent overlapping saves (rapid clicking) from causing backend concurrency errors.
  private routeSaveTimers = new Map<string, ReturnType<typeof setTimeout>>();
  private routeSaveInFlight = new Set<string>();
  private routeSavePending = new Set<string>();
  private routeSaveLatest = new Map<string, RouteInfo>();

  // UI: selected stop index (within location waypoints only) for arrow-based reordering
  selectedRouteStopIndex = signal<number | null>(null);
  startOverrideAddress = '';
  startOverrideLatitude: number | null = null;
  startOverrideLongitude: number | null = null;
  endOverrideAddress = '';
  endOverrideLatitude: number | null = null;
  endOverrideLongitude: number | null = null;
  showStartOverrideEditor = false;
  showEndOverrideEditor = false;

  async ngAfterViewInit(): Promise<void> {
    await this.loadInitialData();
    this.initMap();
    this.initAreaSelection();
    await this.loadDriversWithAvailability();
  }

  ngOnDestroy(): void {
    this.destroyMap();
  }

  private async loadInitialData(): Promise<void> {
    try {
      const [serviceTypes, owners] = await Promise.all([
        this.serviceTypesApi.getAll().toPromise(),
        this.ownersApi.getAll().toPromise(),
      ]);
      
      this.serviceTypes.set(serviceTypes || []);
      const currentOwnerId = this.auth.currentUser()?.ownerId ?? null;
      const isSuperAdmin = (this.auth.currentUser()?.roles ?? []).includes('SuperAdmin');
      const filteredOwners = !isSuperAdmin && currentOwnerId
        ? (owners || []).filter((o) => o.id === currentOwnerId)
        : owners || [];
      this.owners.set(filteredOwners);
      if (!isSuperAdmin && currentOwnerId) {
        this.selectedOwnerId.set(currentOwnerId);
      } else if (!this.selectedOwnerId() && filteredOwners.length === 1) {
        this.selectedOwnerId.set(filteredOwners[0].id);
      }
      this.loadWeightTemplates();
    } catch (error) {
      this.messageService.add({
        severity: 'error',
        summary: 'Error',
        detail: 'Failed to load initial data',
      });
    }
  }

  private loadWeightTemplates(): void {
    const ownerId = this.selectedOwnerId();
    if (!ownerId) {
      this.weightTemplates.set([]);
      this.selectedWeightTemplateId.set(null);
      return;
    }

    this.weightTemplatesApi.getAll(ownerId).subscribe({
      next: (templates) => {
        this.weightTemplates.set(templates || []);
        const selected = this.selectedWeightTemplateId();
        if (selected && !templates?.some((t) => t.id === selected)) {
          this.selectedWeightTemplateId.set(null);
        }
      },
      error: () => {
        this.weightTemplates.set([]);
      },
    });
  }

  onLoad(): void {
    const serviceTypeIds = this.selectedServiceTypeIds();
    const ownerId = this.selectedOwnerId();
    
    if (!ownerId || serviceTypeIds.length === 0) {
      this.messageService.add({
        severity: 'warn',
        summary: 'Validation',
        detail: 'Please select at least one service type and an owner',
      });
      return;
    }

    this.loading.set(true);
    const query = {
      ownerId: ownerId,
      serviceTypeIds: serviceTypeIds.slice(0, 5),
      from: toYmd(this.fromDate()),
      to: toYmd(this.toDate()),
    };

    this.loadMapData(query, { reloadRoutes: true });
  }

  private loadMapData(
    query: { ownerId: number; serviceTypeIds: number[]; from: string; to: string },
    options?: { reloadRoutes?: boolean; silent?: boolean }
  ): void {
    const params = new URLSearchParams({
      ownerId: query.ownerId.toString(),
      from: query.from,
      to: query.to,
    });
    for (const id of query.serviceTypeIds.slice(0, 5)) {
      params.append('serviceTypeIds', id.toString());
    }

    this.lastMapQuery = query;

    if (!options?.silent) {
      this.loading.set(true);
    }

    this.http.get<ServiceLocationsMapResponseDto>(`${environment.apiBaseUrl}/api/map/service-locations?${params}`)
      .subscribe({
        next: (data) => {
          this.mapData.set(data);
          if (!options?.silent) {
            this.loading.set(false);
          }
          if (options?.reloadRoutes) {
            this.loadExistingRoutes();
          }
          setTimeout(() => this.updateMap(), 50);
        },
        error: (error) => {
          console.error('Error loading service locations:', error);
          if (!options?.silent) {
            this.messageService.add({
              severity: 'error',
              summary: 'Error',
              detail: 'Failed to load service locations',
            });
            this.loading.set(false);
          }
        }
      });
  }

  private loadMapDataAsync(
    query: { ownerId: number; serviceTypeIds: number[]; from: string; to: string } | null,
    options?: { reloadRoutes?: boolean; silent?: boolean }
  ): Promise<ServiceLocationsMapResponseDto | null> {
    if (!query) {
      return Promise.resolve(this.mapData());
    }

    const params = new URLSearchParams({
      ownerId: query.ownerId.toString(),
      from: query.from,
      to: query.to,
    });
    for (const id of query.serviceTypeIds.slice(0, 5)) {
      params.append('serviceTypeIds', id.toString());
    }

    this.lastMapQuery = query;

    if (!options?.silent) {
      this.loading.set(true);
    }

    return new Promise((resolve) => {
      this.http.get<ServiceLocationsMapResponseDto>(`${environment.apiBaseUrl}/api/map/service-locations?${params}`)
        .subscribe({
          next: (data) => {
            this.mapData.set(data);
            if (!options?.silent) {
              this.loading.set(false);
            }
            if (options?.reloadRoutes) {
              this.loadExistingRoutes();
            }
            setTimeout(() => this.updateMap(), 50);
            resolve(data);
          },
          error: (error) => {
            console.error('Error loading service locations:', error);
            if (!options?.silent) {
              this.messageService.add({
                severity: 'error',
                summary: 'Error',
                detail: 'Failed to load service locations',
              });
              this.loading.set(false);
            }
            resolve(null);
          }
        });
    });
  }

  private refreshMapDataAfterRouteSave(): void {
    if (!this.lastMapQuery) {
      return;
    }
    this.loadMapData(this.lastMapQuery, { reloadRoutes: false, silent: true });
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
    L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
      attribution: '© OpenStreetMap contributors'
    }).addTo(this.map);

    // Set default view to Belgium
    this.map.setView([50.5039, 4.4699], 8);
  }

  private updateMap(): void {
    if (!this.map) {
      return;
    }

    // Clear existing service location markers (but keep driver marker and route)
    this.markers.forEach(marker => this.map!.removeLayer(marker));
    this.markers = [];
    
    // Get current route once for the entire function
    const currentRoute = this.getCurrentRoute();

    const data = this.mapData();
    if (!data || !data.items || data.items.length === 0) {
      return;
    }

    const minOrderDate = data.minOrderDate ? new Date(data.minOrderDate) : null;
    const maxOrderDate = data.maxOrderDate ? new Date(data.maxOrderDate) : null;
    const today = new Date();
    today.setHours(0, 0, 0, 0);

    const bounds: L.LatLngBoundsExpression = [];

    data.items.forEach((item) => {
      const isPlanned = item.status === 'Planned';
      if (isPlanned && !this.showPlannedLocations()) {
        return;
      }

      const orderDate = new Date(item.orderDate);
      orderDate.setHours(0, 0, 0, 0);
      
      // Calculate urgency color
      const urgencyColor = this.calculateUrgencyColor(orderDate, minOrderDate, maxOrderDate, today);
      const baseColor = isPlanned ? '#111827' : urgencyColor;

      // Check if location is in current route and get its order
      const routeWaypoint = currentRoute?.waypoints.find(
        w => w.type === 'location' && w.erpId === item.erpId
      );
      const isInRoute = !!routeWaypoint;
      const routeOrder = isInRoute && currentRoute
        ? this.getLocationNumber(
            currentRoute.waypoints.findIndex(w => w === routeWaypoint),
            currentRoute.waypoints
          )
        : null;

      // Marker shape depends on service type (max 5 shapes).
      const fill = isInRoute ? '#3b82f6' : baseColor;
      const stroke = isInRoute ? '#1e40af' : (isPlanned ? '#000' : '#333');
      const marker = this.createServiceLocationMarker(item, fill, stroke, isInRoute ? 22 : 18);

      // Don't add number label here - route markers already show the numbers
      // The route waypoint markers from updateAllRoutesDisplay() will show the numbers

      // Tooltip on hover (click is used for route building).
      const popupContent = this.createPopupContent(item);
      const tooltipContent = (isInRoute && routeOrder !== null)
        ? `${popupContent}<br><b style="color: #3b82f6;">In Route (Stop #${routeOrder})</b>`
        : popupContent;

      marker.bindTooltip(tooltipContent, {
        direction: 'top',
        opacity: 0.95,
        sticky: true,
        className: 'map-location-tooltip',
      });

      // Helper function to check if location is in any route
      const isLocationInAnyRoute = (): boolean => {
        const allRoutes = this.driverRoutes();
        for (const route of allRoutes.values()) {
          if (route.waypoints.some(w => w.type === 'location' && w.erpId === item.erpId)) {
            return true;
          }
        }
        return false;
      };
      
      // Click adds/moves (unless Ctrl is held for area selection).
      marker.on('click', (e) => {
        const ctrl = (e.originalEvent as MouseEvent | undefined)?.ctrlKey;
        if (ctrl) {
          return;
        }
        this.onLocationClick(item);
      });
      
      // Add double click handler to remove from route
      marker.on('dblclick', (e) => {
        const ctrl = (e.originalEvent as MouseEvent | undefined)?.ctrlKey;
        if (ctrl) {
          return;
        }
        e.originalEvent?.stopPropagation();
        e.originalEvent?.preventDefault();
        
        // Check if location is in any route (current or other)
        const isInAnyRoute = isLocationInAnyRoute();
        
        // Remove on double click if in any route
        if (isInAnyRoute) {
          this.toggleLocationInRoute(item);
        }
      });

      marker.addTo(this.map!);
      this.markers.push(marker);
      bounds.push([item.latitude, item.longitude] as [number, number]);
    });

    // Fit bounds to all markers (but don't if we have a route, as it might zoom out too much)
    if (bounds.length > 0 && !currentRoute) {
      this.map.fitBounds(bounds, { padding: [50, 50] });
    }
  }

  private calculateUrgencyColor(
    orderDate: Date,
    minOrderDate: Date | null,
    maxOrderDate: Date | null,
    today: Date
  ): string {
    // If overdue (orderDate < today), force dark red
    if (orderDate < today) {
      return 'rgb(139, 0, 0)'; // Dark red
    }

    // If no date range, return amber
    if (!minOrderDate || !maxOrderDate || minOrderDate.getTime() === maxOrderDate.getTime()) {
      return 'rgb(255, 140, 0)'; // Amber
    }

    // Calculate t in [0..1] where 0 = most urgent (min), 1 = least urgent (max)
    const minTime = minOrderDate.getTime();
    const maxTime = maxOrderDate.getTime();
    const orderTime = orderDate.getTime();
    
    const t = (orderTime - minTime) / (maxTime - minTime);
    const tClamped = Math.max(0, Math.min(1, t));

    // Urgency: 1 - t (1 = most urgent, 0 = least urgent)
    const urgency = 1 - tClamped;

    // Interpolate from red (255,0,0) to green (0,180,0)
    const red = Math.round(255 * urgency);
    const green = Math.round(180 * (1 - urgency));
    const blue = 0;

    return `rgb(${red},${green},${blue})`;
  }

  private createPopupContent(item: ServiceLocationMapDto): string {
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
    content += `<span>Service: ${item.serviceMinutes} min</span>`;
    return content;
  }

  private getPlannedRouteInfo(
    item: ServiceLocationMapDto
  ): { driverName: string; date: string } | null {
    if (item.plannedDriverName && item.plannedDate) {
      return {
        driverName: item.plannedDriverName,
        date: toYmd(new Date(item.plannedDate)),
      };
    }

    const routes = this.driverRoutes();
    for (const route of routes.values()) {
      if (route.waypoints.some((w) => w.type === 'location' && w.erpId === item.erpId)) {
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

  private initAreaSelection(): void {
    if (!this.map) {
      return;
    }

    // Ctrl+drag: box select locations and add to selected driver's route.
    this.map.on('mousedown', (e: L.LeafletMouseEvent) => {
      const ctrl = (e.originalEvent as MouseEvent | undefined)?.ctrlKey;
      if (!ctrl) {
        return;
      }

      const selected = this.selectedDriver();
      if (!selected || !selected.availability) {
        this.messageService.add({
          severity: 'info',
          summary: 'Select Driver First',
          detail: 'Please select an available driver first',
        });
        return;
      }

      this.isAreaSelecting = true;
      this.selectionStart = e.latlng;

      // Disable map panning while selecting.
      this.map?.dragging.disable();

      // Create rect
      if (this.selectionRect) {
        this.map?.removeLayer(this.selectionRect);
        this.selectionRect = null;
      }
      this.selectionRect = L.rectangle(L.latLngBounds(e.latlng, e.latlng), {
        color: '#2563eb',
        weight: 2,
        fillOpacity: 0.08,
        dashArray: '4 3',
        interactive: false,
      }).addTo(this.map!);
    });

    this.map.on('mousemove', (e: L.LeafletMouseEvent) => {
      if (!this.isAreaSelecting || !this.selectionStart || !this.selectionRect) {
        return;
      }
      const bounds = L.latLngBounds(this.selectionStart, e.latlng);
      this.selectionRect.setBounds(bounds);
    });

    const finishSelection = (e: L.LeafletMouseEvent) => {
      if (!this.isAreaSelecting || !this.selectionStart || !this.selectionRect) {
        return;
      }

      const bounds = this.selectionRect.getBounds();

      this.isAreaSelecting = false;
      this.selectionStart = null;

      // Re-enable panning.
      this.map?.dragging.enable();

      // Remove selection rectangle.
      this.map?.removeLayer(this.selectionRect);
      this.selectionRect = null;

      const data = this.mapData();
      if (!data?.items?.length) {
        return;
      }

      const visibleItems = data.items.filter((item) => {
        const isPlanned = item.status === 'Planned';
        if (isPlanned && !this.showPlannedLocations()) {
          return false;
        }
        return bounds.contains(L.latLng(item.latitude, item.longitude));
      });

      if (visibleItems.length === 0) {
        return;
      }

      this.addLocationsToCurrentRouteBulk(visibleItems);
    };

    this.map.on('mouseup', finishSelection);
    this.map.on('mouseout', (e: any) => {
      // Leaflet doesn't always send mouseup if cursor leaves map container
      // while dragging selection; try to finish gracefully.
      if (this.isAreaSelecting) {
        finishSelection(e as L.LeafletMouseEvent);
      }
    });
  }

  private addLocationsToCurrentRouteBulk(locations: ServiceLocationMapDto[]): void {
    const selected = this.selectedDriver();
    if (!selected) {
      return;
    }

    // Ensure route exists in memory.
    this.loadOrCreateDriverRoute(selected.driver);

    const routes = new Map(this.driverRoutes());
    const route = routes.get(selected.driver.toolId);
    if (!route) {
      return;
    }

    const allowedLocations = this.enforceServiceTypeMatch()
      ? locations.filter((loc) => this.isServiceTypeAllowed(selected.driver, loc))
      : locations;
    const blockedCount = locations.length - allowedLocations.length;
    if (blockedCount > 0) {
      this.messageService.add({
        severity: 'warn',
        summary: 'Service type mismatch',
        detail: `${blockedCount} location(s) do not match the driver's service types`,
      });
    }

    if (allowedLocations.length === 0) {
      return;
    }

    const erpIdsToAdd = new Set(allowedLocations.map(l => l.erpId));

    // Remove from other routes in UI (backend also enforces uniqueness).
    this.removeLocationsFromOtherDrivers(erpIdsToAdd, selected.driver.toolId);

    const startWaypoint = route.waypoints.find(w => w.type === 'driver-start');
    const endWaypoint = route.waypoints.find(w => w.type === 'driver-end');
    const existingErpIds = new Set(
      route.waypoints.filter(w => w.type === 'location').map(w => w.erpId)
    );

    const newLocationWaypoints: RouteWaypoint[] = [];
    for (const loc of allowedLocations) {
      if (existingErpIds.has(loc.erpId)) {
        continue;
      }
      newLocationWaypoints.push({
        type: 'location',
        name: loc.name,
        latitude: loc.latitude,
        longitude: loc.longitude,
        serviceMinutes: loc.serviceMinutes,
        erpId: loc.erpId,
      });
    }

    if (newLocationWaypoints.length === 0) {
      return;
    }

    const currentLocations = route.waypoints.filter(w => w.type === 'location');
    const combined = [...currentLocations, ...newLocationWaypoints];

    // Optimize once for the combined set.
    const optimizedLocations = this.optimizeRouteOrder(
      startWaypoint || this.getRouteStartPoint(route),
      combined,
      endWaypoint || this.getRouteEndPoint(route)
    );

    const newWaypoints: RouteWaypoint[] = [];
    if (startWaypoint) {
      newWaypoints.push(startWaypoint);
    }
    newWaypoints.push(...optimizedLocations);

    if (endWaypoint) {
      newWaypoints.push(endWaypoint);
    } else {
      const endPoint = this.getRouteEndPoint(route);
      newWaypoints.push({
        type: 'driver-end',
        name: `${route.driver.name} (Stop)`,
        latitude: endPoint.latitude,
        longitude: endPoint.longitude,
      });
    }

    route.waypoints = newWaypoints;

    this.calculateRouteMetrics(route);
    routes.set(route.driver.toolId, { ...route });
    this.driverRoutes.set(routes);

    // Persist once (backend also updates Planned/Open status)
    this.scheduleSaveRouteToBackend(route);

    this.updateAllRoutesDisplay();
    this.refreshLocationMarkers();

    this.messageService.add({
      severity: 'success',
      summary: 'Locations Added',
      detail: `Added ${newLocationWaypoints.length} location(s) to ${route.driver.name}'s route`,
    });
  }

  private removeLocationsFromOtherDrivers(locationErpIds: Set<number>, currentDriverToolId: string): void {
    const routes = new Map(this.driverRoutes());
    let updated = false;

    routes.forEach((route, driverToolId) => {
      if (driverToolId === currentDriverToolId) {
        return;
      }

      const remainingLocations = route.waypoints.filter(w =>
        w.type === 'location' && w.erpId != null && !locationErpIds.has(w.erpId)
      );

      const startWaypoint = route.waypoints.find(w => w.type === 'driver-start');
      const endWaypoint = route.waypoints.find(w => w.type === 'driver-end');

      const hadAnyRemoved = route.waypoints.some(
        w => w.type === 'location' && w.erpId != null && locationErpIds.has(w.erpId)
      );
      if (!hadAnyRemoved) {
        return;
      }

      const newWaypoints: RouteWaypoint[] = [];
      if (startWaypoint) {
        newWaypoints.push(startWaypoint);
      }
      if (remainingLocations.length > 0) {
        const optimized = this.optimizeRouteOrder(
          startWaypoint || this.getRouteStartPoint(route),
          remainingLocations,
          endWaypoint || this.getRouteEndPoint(route)
        );
        newWaypoints.push(...optimized);
      }

      if (endWaypoint) {
        newWaypoints.push(endWaypoint);
      } else if (remainingLocations.length > 0) {
        const endPoint = this.getRouteEndPoint(route);
        newWaypoints.push({
          type: 'driver-end',
          name: `${route.driver.name} (Stop)`,
          latitude: endPoint.latitude,
          longitude: endPoint.longitude,
        });
      }

      route.waypoints = newWaypoints;
      this.calculateRouteMetrics(route);
      routes.set(driverToolId, { ...route });
      updated = true;
    });

    if (updated) {
      this.driverRoutes.set(routes);
    }
  }

  onToggleShowPlannedLocations(event: Event): void {
    const checked = (event.target as HTMLInputElement | null)?.checked ?? true;
    this.showPlannedLocations.set(checked);
    this.refreshLocationMarkers();
  }

  private destroyMap(): void {
    if (this.map) {
      this.map.remove();
      this.map = null;
    }
    this.markers = [];
    this.driverMarker = null;
  }

  onDriverClick(item: DriverWithAvailability, event?: Event): void {
    // If the click originated from inside the route details panel (drag/drop area),
    // do NOT select/switch driver.
    const target = event?.target as HTMLElement | null;
    if (target?.closest?.('.driver-route-info')) {
      event?.stopPropagation?.();
      return;
    }
    
    const isAvailable = item.availability !== null;
    
    // Check if driver is available
    if (!isAvailable) {
      this.messageService.add({
        severity: 'warn',
        summary: 'Driver Unavailable',
        detail: `${item.driver.name} is not available on the selected date`,
      });
    }
    
    console.log('Driver clicked:', item.driver.name, isAvailable ? '(available)' : '(unavailable)');
    console.log('Current routes before switch:', Array.from(this.driverRoutes().keys()));
    
    this.selectedDriver.set(item);
    this.showDriverLocation(item.driver, isAvailable);
    
    // Load or create route for this driver
    if (isAvailable) {
      this.loadOrCreateDriverRoute(item.driver);
    }
    this.syncOverrideInputsFromRoute(this.getCurrentRoute());
    
    console.log('Current routes after switch:', Array.from(this.driverRoutes().keys()));
  }

  onDriversListBackgroundClick(event: MouseEvent): void {
    const target = event.target as HTMLElement | null;
    // If the click is inside a driver card, keep selection.
    if (target?.closest?.('.driver-item')) {
      return;
    }
    // Clicked empty space in the container → clear selection.
    this.selectedDriver.set(null);
    this.selectedRouteStopIndex.set(null);
    this.isBuildingRoute.set(false);
    this.syncOverrideInputsFromRoute(null);
    this.updateAllRoutesDisplay();
    this.refreshLocationMarkers();
  }

  private getCurrentRoute(): RouteInfo | null {
    const selected = this.selectedDriver();
    if (!selected) {
      return null;
    }
    const routes = this.driverRoutes();
    return routes.get(selected.driver.toolId) || null;
  }

  private syncOverrideInputsFromRoute(route: RouteInfo | null): void {
    if (!route) {
      this.startOverrideAddress = '';
      this.startOverrideLatitude = null;
      this.startOverrideLongitude = null;
      this.endOverrideAddress = '';
      this.endOverrideLatitude = null;
      this.endOverrideLongitude = null;
      this.showStartOverrideEditor = false;
      this.showEndOverrideEditor = false;
      return;
    }

    this.startOverrideAddress = route.startOverride?.address ?? '';
    this.startOverrideLatitude = route.startOverride?.latitude ?? null;
    this.startOverrideLongitude = route.startOverride?.longitude ?? null;
    this.endOverrideAddress = route.endOverride?.address ?? '';
    this.endOverrideLatitude = route.endOverride?.latitude ?? null;
    this.endOverrideLongitude = route.endOverride?.longitude ?? null;
  }

  toggleStartOverrideEditor(): void {
    this.showStartOverrideEditor = !this.showStartOverrideEditor;
  }

  toggleEndOverrideEditor(): void {
    this.showEndOverrideEditor = !this.showEndOverrideEditor;
  }

  private getRouteStartPoint(route: RouteInfo): { latitude: number; longitude: number } {
    if (route.startOverride?.latitude != null && route.startOverride?.longitude != null) {
      return { latitude: route.startOverride.latitude, longitude: route.startOverride.longitude };
    }
    return { latitude: route.driver.startLatitude, longitude: route.driver.startLongitude };
  }

  private getRouteEndPoint(route: RouteInfo): { latitude: number; longitude: number } {
    if (route.endOverride?.latitude != null && route.endOverride?.longitude != null) {
      return { latitude: route.endOverride.latitude, longitude: route.endOverride.longitude };
    }
    return { latitude: route.driver.startLatitude, longitude: route.driver.startLongitude };
  }

  private updateRouteStartEndWaypoints(route: RouteInfo): void {
    const startPoint = this.getRouteStartPoint(route);
    const endPoint = this.getRouteEndPoint(route);
    const startName = route.startOverride?.address?.trim() || `${route.driver.name} (Start)`;
    const endName = route.endOverride?.address?.trim() || `${route.driver.name} (Stop)`;

    const startIndex = route.waypoints.findIndex((w) => w.type === 'driver-start');
    const endIndex = route.waypoints.findIndex((w) => w.type === 'driver-end');

    const startWaypoint: RouteWaypoint = {
      type: 'driver-start',
      name: startName,
      latitude: startPoint.latitude,
      longitude: startPoint.longitude,
    };

    const endWaypoint: RouteWaypoint = {
      type: 'driver-end',
      name: endName,
      latitude: endPoint.latitude,
      longitude: endPoint.longitude,
    };

    if (startIndex >= 0) {
      route.waypoints[startIndex] = startWaypoint;
    } else {
      route.waypoints.unshift(startWaypoint);
    }

    if (endIndex >= 0) {
      route.waypoints[endIndex] = endWaypoint;
    } else {
      route.waypoints.push(endWaypoint);
    }
  }

  applyStartOverride(): void {
    this.applyOverride('start');
  }

  applyEndOverride(): void {
    this.applyOverride('end');
  }

  clearStartOverride(): void {
    const route = this.getCurrentRoute();
    if (!route) return;
    route.startOverride = undefined;
    this.updateRouteStartEndWaypoints(route);
    this.syncOverrideInputsFromRoute(route);
    this.calculateRouteMetrics(route);
    this.updateAllRoutesDisplay();
    this.refreshLocationMarkers();
    this.scheduleSaveRouteToBackend(route);
  }

  clearEndOverride(): void {
    const route = this.getCurrentRoute();
    if (!route) return;
    route.endOverride = undefined;
    this.updateRouteStartEndWaypoints(route);
    this.syncOverrideInputsFromRoute(route);
    this.calculateRouteMetrics(route);
    this.updateAllRoutesDisplay();
    this.refreshLocationMarkers();
    this.scheduleSaveRouteToBackend(route);
  }

  private applyOverride(kind: 'start' | 'end'): void {
    const route = this.getCurrentRoute();
    if (!route) return;

    const isStart = kind === 'start';
    const address = (isStart ? this.startOverrideAddress : this.endOverrideAddress).trim();
    const latitude = isStart ? this.startOverrideLatitude : this.endOverrideLatitude;
    const longitude = isStart ? this.startOverrideLongitude : this.endOverrideLongitude;

    const lat = typeof latitude === 'number' && Number.isFinite(latitude) ? latitude : null;
    const lng = typeof longitude === 'number' && Number.isFinite(longitude) ? longitude : null;

    if ((lat == null) !== (lng == null)) {
      this.messageService.add({
        severity: 'warn',
        summary: 'Invalid coordinates',
        detail: 'Please provide both latitude and longitude.',
      });
      return;
    }

    if (lat == null && lng == null && !address) {
      if (isStart) {
        this.clearStartOverride();
      } else {
        this.clearEndOverride();
      }
      return;
    }

    if (lat != null && lng != null) {
      if (lat < -90 || lat > 90 || lng < -180 || lng > 180) {
        this.messageService.add({
          severity: 'warn',
          summary: 'Invalid coordinates',
          detail: 'Latitude must be between -90 and 90, longitude between -180 and 180.',
        });
        return;
      }
    }

    const override: RouteOverride = {
      address: address || undefined,
      latitude: lat ?? undefined,
      longitude: lng ?? undefined,
    };

    if (isStart) {
      route.startOverride = override;
    } else {
      route.endOverride = override;
    }

    this.updateRouteStartEndWaypoints(route);
    if (lat != null && lng != null) {
      this.calculateRouteMetrics(route);
    }
    this.updateAllRoutesDisplay();
    this.refreshLocationMarkers();
    this.scheduleSaveRouteToBackend(route);

    if (address && (lat == null || lng == null)) {
      this.messageService.add({
        severity: 'info',
        summary: 'Geocoding address',
        detail: 'Resolving coordinates for the address.',
      });
    }
  }

  private loadOrCreateDriverRoute(driver: DriverDto): void {
    const routes = this.driverRoutes();
    console.log('loadOrCreateDriverRoute - Current routes:', Array.from(routes.keys()));
    console.log('loadOrCreateDriverRoute - Looking for driver:', driver.toolId);
    
    let route = routes.get(driver.toolId);
    
    if (!route) {
      console.log('loadOrCreateDriverRoute - Creating new route for driver:', driver.toolId);
      // Create new route for this driver (but don't start building automatically)
      const startWaypoint: RouteWaypoint = {
        type: 'driver-start',
        name: `${driver.name} (Start)`,
        latitude: driver.startLatitude,
        longitude: driver.startLongitude,
      };

      route = {
        driver,
        waypoints: [startWaypoint],
        totalDistanceKm: 0,
        totalTimeMinutes: 0,
      };

      // Store route for this driver - IMPORTANT: preserve existing routes
      const newRoutes = new Map(this.driverRoutes());
      newRoutes.set(driver.toolId, route);
      this.driverRoutes.set(newRoutes);
      console.log('loadOrCreateDriverRoute - Routes after adding:', Array.from(newRoutes.keys()));
    } else {
      console.log('loadOrCreateDriverRoute - Found existing route with', route.waypoints.length, 'waypoints');
    }
    
    // Always update display and refresh markers when switching drivers
    // Show all routes, not just the current one
    this.isBuildingRoute.set(true);
    this.updateAllRoutesDisplay();
    this.refreshLocationMarkers();
  }

  onLocationClick(location: ServiceLocationMapDto): void {
    console.log('onLocationClick called for:', location.name, location.erpId);
    const selected = this.selectedDriver();
    if (!selected || !selected.availability) {
      console.log('No driver selected or driver unavailable');
      this.messageService.add({
        severity: 'info',
        summary: 'Select Driver First',
        detail: 'Please select an available driver first to start building a route',
      });
      return;
    }

    console.log('Selected driver:', selected.driver.name);
    if (!this.isBuildingRoute()) {
      this.messageService.add({
        severity: 'info',
        summary: 'Route Building',
        detail: 'Click on locations to add or remove them from the route',
      });
      this.isBuildingRoute.set(true);
    }

    console.log('Calling toggleLocationInRoute');
    this.toggleLocationInRoute(location);
  }

  private startRouteBuilding(driver: DriverDto): void {
    // Check if route already exists for this driver
    const routes = this.driverRoutes();
    if (routes.has(driver.toolId)) {
      // Route already exists, just update display
      this.isBuildingRoute.set(true);
      this.updateRouteDisplay();
      this.refreshLocationMarkers();
      return;
    }

    // Initialize route with driver start position
    const startWaypoint: RouteWaypoint = {
      type: 'driver-start',
      name: `${driver.name} (Start)`,
      latitude: driver.startLatitude,
      longitude: driver.startLongitude,
    };

    const route: RouteInfo = {
      driver,
      waypoints: [startWaypoint],
      totalDistanceKm: 0,
      totalTimeMinutes: 0,
    };

    // Store route for this driver
    const newRoutes = new Map(this.driverRoutes());
    newRoutes.set(driver.toolId, route);
    this.driverRoutes.set(newRoutes);
    
    this.isBuildingRoute.set(true);
    this.updateRouteDisplay();
    this.refreshLocationMarkers();
  }

  private async toggleLocationInRoute(location: ServiceLocationMapDto): Promise<void> {
    const selected = this.selectedDriver();
    if (!selected) {
      return;
    }

    if (this.enforceServiceTypeMatch() && !this.isServiceTypeAllowed(selected.driver, location)) {
      this.messageService.add({
        severity: 'warn',
        summary: 'Service type mismatch',
        detail: `${location.name} does not match ${selected.driver.name}'s service types`,
      });
      return;
    }

    const allRoutes = this.driverRoutes();
    let locationWasInOtherRoute = false;
    let otherRouteDriverName = '';

    for (const [driverToolId, route] of allRoutes.entries()) {
      if (driverToolId !== selected.driver.toolId) {
        const isInRoute = route.waypoints.some(
          (w) => w.type === 'location' && w.erpId === location.erpId
        );
        if (isInRoute) {
          locationWasInOtherRoute = true;
          otherRouteDriverName = route.driver.name;
          break;
        }
      }
    }

    const route = allRoutes.get(selected.driver.toolId);
    if (!route) {
      return;
    }

    const existingIndex = route.waypoints.findIndex(
      (w) => w.type === 'location' && w.erpId === location.erpId
    );

    if (existingIndex !== -1) {
      const startWaypoint = route.waypoints.find((w) => w.type === 'driver-start');
      const endWaypoint = route.waypoints.find((w) => w.type === 'driver-end');
      const remainingLocations = route.waypoints.filter(
        (w, i) => w.type === 'location' && i !== existingIndex
      );

      const newWaypoints: RouteWaypoint[] = [];
      if (startWaypoint) {
        newWaypoints.push(startWaypoint);
      }

      if (remainingLocations.length > 0) {
        const optimizedLocations = this.optimizeRouteOrder(
          startWaypoint || this.getRouteStartPoint(route),
          remainingLocations,
          endWaypoint || this.getRouteEndPoint(route)
        );
        newWaypoints.push(...optimizedLocations);
      }

      if (endWaypoint) {
        newWaypoints.push(endWaypoint);
      } else if (remainingLocations.length > 0) {
        const endPoint = this.getRouteEndPoint(route);
        newWaypoints.push({
          type: 'driver-end',
          name: `${route.driver.name} (Stop)`,
          latitude: endPoint.latitude,
          longitude: endPoint.longitude,
        });
      }

      route.waypoints = newWaypoints;
      this.messageService.add({
        severity: 'info',
        summary: 'Location Removed',
        detail: `${location.name} has been removed from the route`,
      });
    } else {
      const locationWaypoint: RouteWaypoint = {
        type: 'location',
        name: location.name,
        latitude: location.latitude,
        longitude: location.longitude,
        serviceMinutes: location.serviceMinutes,
        erpId: location.erpId,
      };

      const startWaypoint = route.waypoints.find((w) => w.type === 'driver-start');
      const endWaypoint = route.waypoints.find((w) => w.type === 'driver-end');
      const locations = route.waypoints.filter((w) => w.type === 'location');
      locations.push(locationWaypoint);

      const newWaypoints: RouteWaypoint[] = [];
      if (startWaypoint) {
        newWaypoints.push(startWaypoint);
      }

      const optimizedLocations = this.optimizeRouteOrder(
        startWaypoint || this.getRouteStartPoint(route),
        locations,
        endWaypoint || this.getRouteEndPoint(route)
      );
      newWaypoints.push(...optimizedLocations);

      if (endWaypoint) {
        newWaypoints.push(endWaypoint);
      } else {
        const endPoint = this.getRouteEndPoint(route);
        newWaypoints.push({
          type: 'driver-end',
          name: `${route.driver.name} (Stop)`,
          latitude: endPoint.latitude,
          longitude: endPoint.longitude,
        });
      }

      const startMinute = selected.availability?.startMinuteOfDay ?? 0;
      const canAdd = await this.confirmLocationWindow(location, newWaypoints, startMinute);
      if (!canAdd) {
        return;
      }

      this.removeLocationFromOtherDrivers(location.erpId, selected.driver.toolId);
      const routesAfterRemoval = this.driverRoutes();
      const updatedRoute = routesAfterRemoval.get(selected.driver.toolId);
      if (!updatedRoute) {
        return;
      }

      updatedRoute.waypoints = newWaypoints;

      if (locationWasInOtherRoute) {
        this.messageService.add({
          severity: 'success',
          summary: 'Location Moved',
          detail: `${location.name} has been moved from ${otherRouteDriverName}'s route to ${updatedRoute.driver.name}'s route`,
        });
      } else {
        this.messageService.add({
          severity: 'success',
          summary: 'Location Added',
          detail: `${location.name} has been added to the route`,
        });
      }

      this.calculateRouteMetrics(updatedRoute);

      const routes = new Map(this.driverRoutes());
      const updatedRouteCopy = {
        ...updatedRoute,
        totalTimeMinutes: updatedRoute.totalTimeMinutes || 0,
        totalDistanceKm: updatedRoute.totalDistanceKm || 0,
      };
      routes.set(updatedRoute.driver.toolId, updatedRouteCopy);
      this.driverRoutes.set(routes);

      this.scheduleSaveRouteToBackend(updatedRouteCopy);
      this.updateAllRoutesDisplay();
      this.refreshLocationMarkers();
      return;
    }

    this.calculateRouteMetrics(route);

    const routes = new Map(this.driverRoutes());
    const updatedRoute = {
      ...route,
      totalTimeMinutes: route.totalTimeMinutes || 0,
      totalDistanceKm: route.totalDistanceKm || 0,
    };
    routes.set(route.driver.toolId, updatedRoute);
    this.driverRoutes.set(routes);

    this.scheduleSaveRouteToBackend(updatedRoute);
    this.updateAllRoutesDisplay();
    this.refreshLocationMarkers();
  }

  private removeLocationFromOtherDrivers(locationErpId: number, currentDriverToolId: string): void {
    const routes = new Map(this.driverRoutes());
    let updated = false;
    
    routes.forEach((route, driverToolId) => {
      if (driverToolId !== currentDriverToolId) {
        const locationIndex = route.waypoints.findIndex(
          w => w.type === 'location' && w.erpId === locationErpId
        );
        
        if (locationIndex !== -1) {
          // Remove location from this driver's route
          const startWaypoint = route.waypoints.find(w => w.type === 'driver-start');
          const endWaypoint = route.waypoints.find(w => w.type === 'driver-end');
          const remainingLocations = route.waypoints.filter(
            (w, i) => w.type === 'location' && i !== locationIndex
          );
          
          // Rebuild waypoints
          const newWaypoints: RouteWaypoint[] = [];
          if (startWaypoint) {
            newWaypoints.push(startWaypoint);
          }
          
          if (remainingLocations.length > 0) {
            const optimizedLocations = this.optimizeRouteOrder(
              startWaypoint || this.getRouteStartPoint(route),
              remainingLocations,
              endWaypoint || this.getRouteEndPoint(route)
            );
            newWaypoints.push(...optimizedLocations);
          }
          
          if (endWaypoint) {
            newWaypoints.push(endWaypoint);
          } else if (remainingLocations.length > 0) {
            const endPoint = this.getRouteEndPoint(route);
            const stopWaypoint: RouteWaypoint = {
              type: 'driver-end',
              name: `${route.driver.name} (Stop)`,
              latitude: endPoint.latitude,
              longitude: endPoint.longitude,
            };
            newWaypoints.push(stopWaypoint);
          }
          
          route.waypoints = newWaypoints;
          this.calculateRouteMetrics(route);
          routes.set(driverToolId, route);
          updated = true;
          
          // Save updated route to backend - debounced + queued per driver
          this.scheduleSaveRouteToBackend(route);
        }
      }
    });
    
    if (updated) {
      this.driverRoutes.set(routes);
      // Update all routes display to show the changes
      this.updateAllRoutesDisplay();
      this.refreshLocationMarkers();
    }
  }

  private refreshLocationMarkers(): void {
    // Re-render all location markers to update route numbers
    // This ensures number markers are properly added/removed
    const data = this.mapData();
    if (!data || data.items.length === 0 || !this.map) {
      return;
    }

    // Remove all existing location markers (these are tracked in `this.markers`).
    this.markers.forEach(marker => this.map!.removeLayer(marker));
    this.markers = [];

    const currentRoute = this.getCurrentRoute();
    const allRoutes = this.driverRoutes();
    const minOrderDate = data.minOrderDate ? new Date(data.minOrderDate) : null;
    const maxOrderDate = data.maxOrderDate ? new Date(data.maxOrderDate) : null;
    const today = new Date();
    today.setHours(0, 0, 0, 0);
    
    // Re-create all location markers with updated styling and numbers
    data.items.forEach((item) => {
      const isPlanned = item.status === 'Planned';
      if (isPlanned && !this.showPlannedLocations()) {
        return;
      }

      const orderDate = new Date(item.orderDate);
      orderDate.setHours(0, 0, 0, 0);
      
      const urgencyColor = this.calculateUrgencyColor(orderDate, minOrderDate, maxOrderDate, today);
      const baseColor = isPlanned ? '#111827' : urgencyColor;
      
      // Check if location is in current route and get its order
      const routeWaypoint = currentRoute?.waypoints.find(
        w => w.type === 'location' && w.erpId === item.erpId
      );
      const isInRoute = !!routeWaypoint;
      const routeOrder = isInRoute && currentRoute
        ? this.getLocationNumber(
            currentRoute.waypoints.findIndex(w => w === routeWaypoint),
            currentRoute.waypoints
          )
        : null;

      // Marker shape depends on service type (max 5 shapes).
      const fill = isInRoute ? '#3b82f6' : baseColor;
      const stroke = isInRoute ? '#1e40af' : (isPlanned ? '#000' : '#333');
      const marker = this.createServiceLocationMarker(item, fill, stroke, isInRoute ? 22 : 18);

      // Don't add number label here - route markers already show the numbers
      // The route waypoint markers from updateAllRoutesDisplay() will show the numbers

      // Tooltip on hover (click is used for route building).
      const popupContent = this.createPopupContent(item);
      const tooltipContent = (isInRoute && routeOrder !== null)
        ? `${popupContent}<br><b style="color: #3b82f6;">In Route (Stop #${routeOrder})</b>`
        : popupContent;

      marker.bindTooltip(tooltipContent, {
        direction: 'top',
        opacity: 0.95,
        sticky: true,
        className: 'map-location-tooltip',
      });

      // Helper function to check if location is in any route
      const isLocationInAnyRoute = (): boolean => {
        const allRoutes = this.driverRoutes();
        for (const route of allRoutes.values()) {
          if (route.waypoints.some(w => w.type === 'location' && w.erpId === item.erpId)) {
            return true;
          }
        }
        return false;
      };
      
      // Click adds/moves (unless Ctrl is held for area selection).
      marker.on('click', (e) => {
        const ctrl = (e.originalEvent as MouseEvent | undefined)?.ctrlKey;
        if (ctrl) {
          return;
        }
        this.onLocationClick(item);
      });
      
      // Add double click handler to remove from route
      marker.on('dblclick', (e) => {
        const ctrl = (e.originalEvent as MouseEvent | undefined)?.ctrlKey;
        if (ctrl) {
          return;
        }
        e.originalEvent?.stopPropagation();
        e.originalEvent?.preventDefault();
        
        // Check if location is in any route (current or other)
        const isInAnyRoute = isLocationInAnyRoute();
        
        // Remove on double click if in any route
        if (isInAnyRoute) {
          this.toggleLocationInRoute(item);
        }
      });

      if (this.map) {
        marker.addTo(this.map);
        this.markers.push(marker);
      }
    });
  }

  private optimizeRouteOrder(
    start: { latitude: number; longitude: number },
    locations: RouteWaypoint[],
    end: { latitude: number; longitude: number }
  ): RouteWaypoint[] {
    if (locations.length === 0) {
      return [];
    }

    if (locations.length === 1) {
      return locations;
    }

    // Use nearest neighbor algorithm with 2-opt improvement
    const optimized = this.nearestNeighbor(start, locations, end);
    return this.twoOptImprovement(optimized, start, end);
  }

  private nearestNeighbor(
    start: { latitude: number; longitude: number },
    locations: RouteWaypoint[],
    end: { latitude: number; longitude: number }
  ): RouteWaypoint[] {
    const result: RouteWaypoint[] = [];
    const unvisited = [...locations];
    let current = start;

    while (unvisited.length > 0) {
      // Find nearest unvisited location
      let nearestIndex = 0;
      let nearestDistance = this.calculateDistance(
        current.latitude,
        current.longitude,
        unvisited[0].latitude,
        unvisited[0].longitude
      );

      for (let i = 1; i < unvisited.length; i++) {
        const distance = this.calculateDistance(
          current.latitude,
          current.longitude,
          unvisited[i].latitude,
          unvisited[i].longitude
        );
        if (distance < nearestDistance) {
          nearestDistance = distance;
          nearestIndex = i;
        }
      }

      // Add nearest location to result
      const nearest = unvisited.splice(nearestIndex, 1)[0];
      result.push(nearest);
      current = nearest;
    }

    return result;
  }

  private twoOptImprovement(
    route: RouteWaypoint[],
    start: { latitude: number; longitude: number },
    end: { latitude: number; longitude: number }
  ): RouteWaypoint[] {
    if (route.length < 3) {
      return route;
    }

    let improved = true;
    let bestRoute = [...route];
    let bestDistance = this.calculateRouteDistance(start, bestRoute, end);

    // Try 2-opt swaps
    while (improved) {
      improved = false;
      
      for (let i = 0; i < bestRoute.length - 1; i++) {
        for (let j = i + 2; j < bestRoute.length; j++) {
          // Try reversing segment between i and j
          const newRoute = [
            ...bestRoute.slice(0, i + 1),
            ...bestRoute.slice(i + 1, j + 1).reverse(),
            ...bestRoute.slice(j + 1)
          ];
          
          const newDistance = this.calculateRouteDistance(start, newRoute, end);
          
          if (newDistance < bestDistance) {
            bestRoute = newRoute;
            bestDistance = newDistance;
            improved = true;
            break;
          }
        }
        if (improved) break;
      }
    }

    return bestRoute;
  }

  private calculateRouteDistance(
    start: { latitude: number; longitude: number },
    locations: RouteWaypoint[],
    end: { latitude: number; longitude: number }
  ): number {
    if (locations.length === 0) {
      return this.calculateDistance(start.latitude, start.longitude, end.latitude, end.longitude);
    }

    let total = 0;
    
    // Start to first location
    total += this.calculateDistance(
      start.latitude,
      start.longitude,
      locations[0].latitude,
      locations[0].longitude
    );

    // Between locations
    for (let i = 0; i < locations.length - 1; i++) {
      total += this.calculateDistance(
        locations[i].latitude,
        locations[i].longitude,
        locations[i + 1].latitude,
        locations[i + 1].longitude
      );
    }

    // Last location to end
    total += this.calculateDistance(
      locations[locations.length - 1].latitude,
      locations[locations.length - 1].longitude,
      end.latitude,
      end.longitude
    );

    return total;
  }


  private calculateRouteMetrics(route: RouteInfo): void {
    let totalDistanceKm = 0;
    let totalTimeMinutes = 0;

    for (let i = 0; i < route.waypoints.length - 1; i++) {
      const from = route.waypoints[i];
      const to = route.waypoints[i + 1];
      
      // Calculate distance using Haversine formula
      const distanceKm = this.calculateDistance(
        from.latitude,
        from.longitude,
        to.latitude,
        to.longitude
      );
      totalDistanceKm += distanceKm;

      // Estimate travel time (assuming average speed of 50 km/h)
      const travelMinutes = (distanceKm / 50) * 60;
      totalTimeMinutes += travelMinutes;

      // Add service time if it's a location waypoint
      if (to.serviceMinutes) {
        totalTimeMinutes += to.serviceMinutes;
      }
    }

    route.totalDistanceKm = totalDistanceKm || 0;
    route.totalTimeMinutes = Math.round(totalTimeMinutes) || 0;
    
    // Ensure values are never null/undefined
    if (route.totalDistanceKm == null) route.totalDistanceKm = 0;
    if (route.totalTimeMinutes == null) route.totalTimeMinutes = 0;
  }

  private estimateArrivalMinutes(
    waypoints: RouteWaypoint[],
    startMinute: number
  ): Map<number, ArrivalWindow> {
    const arrivals = new Map<number, ArrivalWindow>();
    let currentMinute = startMinute;

    for (let i = 1; i < waypoints.length; i++) {
      const from = waypoints[i - 1];
      const to = waypoints[i];
      const distanceKm = this.calculateDistance(from.latitude, from.longitude, to.latitude, to.longitude);
      const travelMinutes = Math.round((distanceKm / 50) * 60);
      currentMinute += travelMinutes;

      if (to.type === 'location' && to.erpId != null) {
        const serviceMinutes = to.serviceMinutes ?? 0;
        const start = currentMinute;
        const end = currentMinute + serviceMinutes;
        arrivals.set(to.erpId, { startMinute: start, endMinute: end });
        currentMinute = end;
      }
    }

    return arrivals;
  }

  private async confirmLocationWindow(
    location: ServiceLocationMapDto,
    waypoints: RouteWaypoint[],
    startMinute: number
  ): Promise<boolean> {
    const windowInfo = await this.getLocationWindow(location.toolId, this.selectedDate());
    if (!windowInfo) {
      return true;
    }

    const arrivals = this.estimateArrivalMinutes(waypoints, startMinute);
    const arrival = arrivals.get(location.erpId);
    if (!arrival) {
      return true;
    }

    const outside =
      windowInfo.isClosed ||
      arrival.startMinute < windowInfo.openMinute ||
      arrival.endMinute > windowInfo.closeMinute;

    if (!outside) {
      return true;
    }

    const windowLabel = windowInfo.isClosed
      ? 'Closed all day'
      : `${this.formatMinute(windowInfo.openMinute)} - ${this.formatMinute(windowInfo.closeMinute)}`;
    const plannedLabel = `${this.formatMinute(arrival.startMinute)} - ${this.formatMinute(arrival.endMinute)}`;
    const message = windowInfo.isClosed
      ? `${location.name} is closed on ${toYmd(this.selectedDate())}. Add anyway?`
      : `${location.name} is open ${windowLabel}. Planned time ${plannedLabel}. Add anyway?`;

    const proceed = window.confirm(message);
    if (!proceed) {
      this.messageService.add({
        severity: 'warn',
        summary: 'Outside opening hours',
        detail: 'Location was not added.',
      });
    } else {
      this.messageService.add({
        severity: 'warn',
        summary: 'Outside opening hours',
        detail: 'Location added with override.',
      });
    }

    return proceed;
  }

  private async getLocationWindow(
    toolId: string,
    date: Date
  ): Promise<LocationWindowInfo | null> {
    const key = `${toolId}-${toYmd(date)}`;
    if (this.locationWindowCache.has(key)) {
      return this.locationWindowCache.get(key) ?? null;
    }

    const [hours, exceptions] = await Promise.all([
      firstValueFrom(this.serviceLocationsApi.getOpeningHours(toolId)),
      firstValueFrom(this.serviceLocationsApi.getExceptions(toolId)),
    ]);

    const dateKey = toYmd(date);
    const exception = exceptions.find((ex) => toYmd(new Date(ex.date)) === dateKey);
    let windowInfo: LocationWindowInfo | null = null;

    if (exception) {
      windowInfo = this.buildWindowInfo(exception.openTime, exception.closeTime, exception.isClosed);
    } else {
      const dayOfWeek = date.getDay();
      const standard = hours.find((h) => h.dayOfWeek === dayOfWeek);
      if (standard) {
        windowInfo = this.buildWindowInfo(standard.openTime, standard.closeTime, standard.isClosed);
      }
    }

    this.locationWindowCache.set(key, windowInfo);
    return windowInfo;
  }

  private buildWindowInfo(
    openTime?: string | null,
    closeTime?: string | null,
    isClosed?: boolean
  ): LocationWindowInfo | null {
    if (isClosed) {
      return { isClosed: true, openMinute: 0, closeMinute: 0, label: 'Closed' };
    }

    const openMinute = this.parseTimeToMinutes(openTime);
    const closeMinute = this.parseTimeToMinutes(closeTime);
    if (openMinute == null || closeMinute == null || openMinute >= closeMinute) {
      return null;
    }

    return {
      isClosed: false,
      openMinute,
      closeMinute,
      label: `${openTime} - ${closeTime}`,
    };
  }

  private parseTimeToMinutes(value?: string | null): number | null {
    if (!value) {
      return null;
    }
    const [h, m] = value.split(':').map((v) => Number(v));
    if (!Number.isFinite(h) || !Number.isFinite(m)) {
      return null;
    }
    return h * 60 + m;
  }

  private formatMinute(minutes: number): string {
    const h = Math.floor(minutes / 60);
    const m = minutes % 60;
    return `${String(h).padStart(2, '0')}:${String(m).padStart(2, '0')}`;
  }

  private calculateDistance(lat1: number, lon1: number, lat2: number, lon2: number): number {
    const R = 6371; // Earth's radius in km
    const dLat = this.toRad(lat2 - lat1);
    const dLon = this.toRad(lon2 - lon1);
    const a =
      Math.sin(dLat / 2) * Math.sin(dLat / 2) +
      Math.cos(this.toRad(lat1)) *
        Math.cos(this.toRad(lat2)) *
        Math.sin(dLon / 2) *
        Math.sin(dLon / 2);
    const c = 2 * Math.atan2(Math.sqrt(a), Math.sqrt(1 - a));
    return R * c;
  }

  private toRad(degrees: number): number {
    return degrees * (Math.PI / 180);
  }

  private updateRouteDisplay(): void {
    // Display all routes, not just the current one
    this.updateAllRoutesDisplay();
  }

  private getRouteColor(driverToolId: string): string {
    // Generate a consistent color for each driver based on their toolId
    const colors = [
      '#3b82f6', // blue
      '#ef4444', // red
      '#10b981', // green
      '#f59e0b', // amber
      '#8b5cf6', // purple
      '#ec4899', // pink
      '#06b6d4', // cyan
      '#84cc16', // lime
      '#f97316', // orange
      '#6366f1', // indigo
    ];
    
    // Use a simple hash to consistently assign colors
    let hash = 0;
    for (let i = 0; i < driverToolId.length; i++) {
      hash = driverToolId.charCodeAt(i) + ((hash << 5) - hash);
    }
    const index = Math.abs(hash) % colors.length;
    return colors[index];
  }

  private updateAllRoutesDisplay(): void {
    if (!this.map) {
      return;
    }

    const allRoutes = this.driverRoutes();
    console.log('updateAllRoutesDisplay - Displaying', allRoutes.size, 'routes');

    // Remove all existing route polylines and markers
    this.routePolylines.forEach((polyline, driverToolId) => {
      this.map!.removeLayer(polyline);
    });
    this.routePolylines.clear();

    this.routeMarkers.forEach((markers, driverToolId) => {
      markers.forEach(marker => this.map!.removeLayer(marker));
    });
    this.routeMarkers.clear();

    // Draw all routes
    allRoutes.forEach((route, driverToolId) => {
      if (route.waypoints.length >= 2) {
        const color = this.getRouteColor(driverToolId);
        const latlngs = (route.roadGeometry && route.roadGeometry.length > 1)
          ? route.roadGeometry
          : route.waypoints.map(w => [w.latitude, w.longitude] as [number, number]);
        
        // Create polyline with driver-specific color
        const polyline = L.polyline(latlngs, {
          color: color,
          weight: 4,
          opacity: 0.7,
          smoothFactor: 1,
        }).addTo(this.map!);
        this.routePolylines.set(driverToolId, polyline);

        // Add numbered markers for each waypoint
        const markers: L.Marker[] = [];
        route.waypoints.forEach((waypoint, index) => {
          let icon: L.Icon | L.DivIcon;
          
          if (waypoint.type === 'driver-start') {
            icon = L.divIcon({
              className: 'route-marker route-marker-start',
              html: `<p class="route-number" style="color: red; padding-left: 10px; padding-top: 3px;">S</p>`,
              iconSize: [24, 24],
              iconAnchor: [12, 12],
            });
          } else if (waypoint.type === 'driver-end') {
            icon = L.divIcon({
              className: 'route-marker route-marker-end',
              html: `<p class="route-number" style="color: red; padding-left: 10px; padding-top: 3px;">E</p>`,
              iconSize: [24, 24],
              iconAnchor: [12, 12],
            });
          } else {
            // Get the location number in the route (excluding start/end)
            const locationIndex = route.waypoints
              .slice(0, index)
              .filter(w => w.type === 'location').length + 1;
            icon = L.divIcon({
              className: 'route-marker route-marker-location',
              html: `<p class="route-number" style="color: red; padding-left: 10px; padding-top: 3px;">${locationIndex}</p>`,
              iconSize: [24, 24],
              iconAnchor: [12, 12],
            });
          }

          const marker = L.marker([waypoint.latitude, waypoint.longitude], { 
            icon,
            interactive: true
          });
          marker.bindPopup(`<b>${waypoint.name}</b><br><span style="color: ${color};">Route: ${route.driver.name}</span>`);
          if (waypoint.type === 'location') {
            marker.on('dblclick', (e) => {
              e.originalEvent?.stopPropagation();
              this.removeWaypointFromRoute(driverToolId, waypoint);
            });
          }
          marker.addTo(this.map!);
          markers.push(marker);
        });
        this.routeMarkers.set(driverToolId, markers);
      }
    });
  }

  private isServiceTypeAllowed(driver: DriverDto, location: ServiceLocationMapDto): boolean {
    const driverServiceTypes = driver.serviceTypeIds ?? [];
    if (driverServiceTypes.length === 0) {
      return false;
    }
    return driverServiceTypes.includes(location.serviceTypeId);
  }

  private removeWaypointFromRoute(driverToolId: string, waypoint: RouteWaypoint): void {
    const routes = new Map(this.driverRoutes());
    const route = routes.get(driverToolId);
    if (!route) return;

    const before = route.waypoints.length;
    route.waypoints = route.waypoints.filter((w) => {
      if (w.type !== 'location') return true;
      if (waypoint.erpId != null && w.erpId != null) {
        return w.erpId !== waypoint.erpId;
      }
      return !(Math.abs(w.latitude - waypoint.latitude) < 1e-5 && Math.abs(w.longitude - waypoint.longitude) < 1e-5);
    });

    if (route.waypoints.length === before) return;

    this.calculateRouteMetrics(route);
    routes.set(driverToolId, { ...route });
    this.driverRoutes.set(routes);
    this.updateAllRoutesDisplay();
    this.refreshLocationMarkers();
    this.scheduleSaveRouteToBackend(route);
  }

  private updateRouteDisplayForRoute(route: RouteInfo | null): void {
    // This method is now deprecated - we always show all routes
    // But keep it for backward compatibility
    this.updateAllRoutesDisplay();
  }


  trackByDriverId(index: number, item: DriverWithAvailability): string {
    return item.driver.toolId;
  }

  isDriverOverbooked(item: DriverWithAvailability): boolean {
    if (!item.availability) {
      return false;
    }

    const route = this.driverRoutes().get(item.driver.toolId);
    if (!route) {
      return false;
    }

    const totalMinutes = Number(route.totalTimeMinutes) || 0;
    return totalMinutes > item.availability.availableMinutes;
  }

  private showDriverLocation(driver: DriverDto, isAvailable: boolean): void {
    if (!this.map) {
      console.error('Map not initialized');
      this.messageService.add({
        severity: 'warn',
        summary: 'Map not ready',
        detail: 'Please wait for the map to load',
      });
      return;
    }

    // Remove previous driver marker
    if (this.driverMarker) {
      this.map.removeLayer(this.driverMarker);
      this.driverMarker = null;
    }

    // Create blinking star icon - red if unavailable, yellow if available
    const starClass = isAvailable ? 'driver-star-marker driver-star-available' : 'driver-star-marker driver-star-unavailable';
    const starHtml = isAvailable 
      ? '<div class="star-icon">⭐</div>' 
      : '<div class="star-icon star-red">⭐</div>'; // Red star for unavailable
    const starIcon = L.divIcon({
      className: starClass,
      html: starHtml,
      iconSize: [32, 32],
      iconAnchor: [16, 16],
    });

    // Create marker at driver's start location
    this.driverMarker = L.marker(
      [driver.startLatitude, driver.startLongitude],
      { icon: starIcon }
    );

    // Add click handler to complete route if building
    if (isAvailable && this.isBuildingRoute()) {
      this.driverMarker.on('click', () => {
        const route = this.getCurrentRoute();
        if (route && route.driver.toolId === driver.toolId) {
          this.completeRoute();
        }
      });
    }

    // Create popup content
    let popupContent = `<b>${driver.name}</b><br>`;
    if (driver.startAddress) {
      popupContent += `Address: ${driver.startAddress}<br>`;
    }
    popupContent += `Start Location<br>`;
    popupContent += `Lat: ${Number(driver.startLatitude).toFixed(6)}, Lng: ${Number(driver.startLongitude).toFixed(6)}<br>`;
    if (!isAvailable) {
      popupContent += `<br><span style="color: #dc2626; font-weight: 600;">Not available on selected date</span>`;
    } else if (this.isBuildingRoute()) {
      popupContent += `<br><span style="color: #3b82f6; font-weight: 600;">Click to complete route</span>`;
    }
    
    this.driverMarker.bindPopup(popupContent);
    this.driverMarker.addTo(this.map);

    // Center map on driver location with less zoom
    this.map.setView([driver.startLatitude, driver.startLongitude], 9);
    
    // Open popup
    this.driverMarker.openPopup();
  }

  async onDateChange(): Promise<void> {
    // Routes are per driver per day — clear current in-memory routes immediately
    this.driverRoutes.set(new Map());
    this.updateAllRoutesDisplay();
    this.refreshLocationMarkers();
    this.locationWindowCache.clear();

    // Reload availability for the new day first (async)
    await this.loadDriversWithAvailability();

    // Reload routes for the new day (if owner + map context selected)
    if (this.selectedOwnerId() && this.mapData()) {
      this.loadExistingRoutes();
    }
  }

  onServiceTypeOrOwnerChange(): void {
    // Reset driver selection and availability when owner changes
    this.driversWithAvailability.set([]);
    this.selectedDriver.set(null);
    this.loadingDrivers.set(true);
    this.loadDriversWithAvailability().finally(() => this.loadingDrivers.set(false));
    this.loadWeightTemplates();

    // When service types or owner changes, reload routes if owner + map context selected
    if (this.selectedOwnerId() && this.mapData()) {
      this.loadExistingRoutes();
    }
  }

  private loadExistingRoutes(): void {
    const ownerId = this.selectedOwnerId();
    const date = this.selectedDate();
    
    if (!ownerId) {
      return;
    }

    // Clear current routes so we don't show routes from a different day while loading/failing
    this.driverRoutes.set(new Map());
    this.updateAllRoutesDisplay();
    this.refreshLocationMarkers();

    const drivers = this.driversWithAvailability();
    if (drivers.length === 0) {
      return;
    }

    // Load routes for all drivers (no serviceTypeId - routes are identified by date, driver, owner only)
    const routePromises = drivers.map(driver => 
      this.routesApi.getRoutes(date, driver.driver.toolId, ownerId).toPromise()
    );

    Promise.all(routePromises).then(routeArrays => {
      const routes = new Map<string, RouteInfo>();
      
      routeArrays.forEach((routeArray, index) => {
        if (!routeArray || routeArray.length === 0) {
          return;
        }
        
        const driver = drivers[index];
        if (!driver) {
          return;
        }

        // For now, take the first route (there should only be one per driver per day)
        const routeDto = routeArray[0];
        if (!routeDto) {
          return;
        }

        const startOverride =
          routeDto.startLatitude != null && routeDto.startLongitude != null
            ? {
                address: routeDto.startAddress || undefined,
                latitude: routeDto.startLatitude,
                longitude: routeDto.startLongitude,
              }
            : undefined;
        const endOverride =
          routeDto.endLatitude != null && routeDto.endLongitude != null
            ? {
                address: routeDto.endAddress || undefined,
                latitude: routeDto.endLatitude,
                longitude: routeDto.endLongitude,
              }
            : undefined;

        // Convert RouteDto to RouteInfo
        const waypoints: RouteWaypoint[] = [];
        
        // Add driver start waypoint
        waypoints.push({
          type: 'driver-start',
          name: startOverride?.address?.trim() || `${driver.driver.name} (Start)`,
          latitude: startOverride?.latitude ?? driver.driver.startLatitude,
          longitude: startOverride?.longitude ?? driver.driver.startLongitude,
        });

        // Add location waypoints from stops
        const mapData = this.mapData();
        routeDto.stops
          .sort((a, b) => a.sequence - b.sequence)
          .forEach(stop => {
            // Find the service location by ToolId (preferred) or coordinates
            let serviceLocation = null;
            if (stop.serviceLocationToolId) {
              serviceLocation = mapData?.items.find(item => item.toolId === stop.serviceLocationToolId);
            }
            
            // Fallback to coordinate matching if ToolId not available
            if (!serviceLocation) {
              serviceLocation = mapData?.items.find(item => {
                const latDiff = Math.abs(item.latitude - stop.latitude);
                const lonDiff = Math.abs(item.longitude - stop.longitude);
                return latDiff < 0.0001 && lonDiff < 0.0001; // Very close coordinates
              });
            }

            if (serviceLocation) {
              waypoints.push({
                type: 'location',
                name: serviceLocation.name,
                latitude: stop.latitude,
                longitude: stop.longitude,
                serviceMinutes: stop.serviceMinutes,
                erpId: serviceLocation.erpId,
              });
            } else {
              // If we can't find the service location, still add the waypoint
              waypoints.push({
                type: 'location',
                name: stop.name || `Stop ${stop.sequence}`,
                latitude: stop.latitude,
                longitude: stop.longitude,
                serviceMinutes: stop.serviceMinutes,
              });
            }
          });

        // Add driver end waypoint
        waypoints.push({
          type: 'driver-end',
          name: endOverride?.address?.trim() || `${driver.driver.name} (Stop)`,
          latitude: endOverride?.latitude ?? driver.driver.startLatitude,
          longitude: endOverride?.longitude ?? driver.driver.startLongitude,
        });

        const routeInfo: RouteInfo = {
          driver: driver.driver,
          waypoints: waypoints,
          totalDistanceKm: routeDto.totalKm,
          totalTimeMinutes: routeDto.totalMinutes,
          roadGeometry: routeDto.geometry && routeDto.geometry.length > 1
            ? routeDto.geometry.map(p => [Number(p.lat), Number(p.lng)] as [number, number])
            : undefined,
          startOverride,
          endOverride,
        };

        routes.set(driver.driver.toolId, routeInfo);
      });

      this.driverRoutes.set(routes);
      this.updateAllRoutesDisplay();
      this.refreshLocationMarkers();
      this.syncOverrideInputsFromRoute(this.getCurrentRoute());
    }).catch(error => {
      console.error('Error loading existing routes:', error);
      // Routes are per day; if loading fails, keep map clean (no stale routes)
      this.driverRoutes.set(new Map());
      this.updateAllRoutesDisplay();
      this.refreshLocationMarkers();
      this.syncOverrideInputsFromRoute(this.getCurrentRoute());
    });
  }

  private async loadExistingRoutesAsync(): Promise<void> {
    const ownerId = this.selectedOwnerId();
    const date = this.selectedDate();

    if (!ownerId) {
      return;
    }

    this.driverRoutes.set(new Map());
    this.updateAllRoutesDisplay();
    this.refreshLocationMarkers();

    const drivers = this.driversWithAvailability();
    if (drivers.length === 0) {
      return;
    }

    const routePromises = drivers.map(driver =>
      this.routesApi.getRoutes(date, driver.driver.toolId, ownerId).toPromise()
    );

    try {
      const routeArrays = await Promise.all(routePromises);
      const routes = new Map<string, RouteInfo>();

      routeArrays.forEach((routeArray, index) => {
        if (!routeArray || routeArray.length === 0) {
          return;
        }

        const driver = drivers[index];
        if (!driver) {
          return;
        }

        const routeDto = routeArray[0];
        if (!routeDto) {
          return;
        }

        const startOverride =
          routeDto.startLatitude != null && routeDto.startLongitude != null
            ? {
                address: routeDto.startAddress || undefined,
                latitude: routeDto.startLatitude,
                longitude: routeDto.startLongitude,
              }
            : undefined;
        const endOverride =
          routeDto.endLatitude != null && routeDto.endLongitude != null
            ? {
                address: routeDto.endAddress || undefined,
                latitude: routeDto.endLatitude,
                longitude: routeDto.endLongitude,
              }
            : undefined;

        const waypoints: RouteWaypoint[] = [];
        waypoints.push({
          type: 'driver-start',
          name: startOverride?.address?.trim() || `${driver.driver.name} (Start)`,
          latitude: startOverride?.latitude ?? driver.driver.startLatitude,
          longitude: startOverride?.longitude ?? driver.driver.startLongitude,
        });

        const mapData = this.mapData();
        routeDto.stops
          .sort((a, b) => a.sequence - b.sequence)
          .forEach(stop => {
            let serviceLocation = null;
            if (stop.serviceLocationToolId) {
              serviceLocation = mapData?.items.find(item => item.toolId === stop.serviceLocationToolId);
            }

            if (!serviceLocation) {
              serviceLocation = mapData?.items.find(item => {
                const latDiff = Math.abs(item.latitude - stop.latitude);
                const lonDiff = Math.abs(item.longitude - stop.longitude);
                return latDiff < 0.0001 && lonDiff < 0.0001;
              });
            }

            if (serviceLocation) {
              waypoints.push({
                type: 'location',
                name: serviceLocation.name,
                latitude: stop.latitude,
                longitude: stop.longitude,
                serviceMinutes: stop.serviceMinutes,
                erpId: serviceLocation.erpId,
              });
            } else {
              waypoints.push({
                type: 'location',
                name: stop.name || `Stop ${stop.sequence}`,
                latitude: stop.latitude,
                longitude: stop.longitude,
                serviceMinutes: stop.serviceMinutes,
              });
            }
          });

        waypoints.push({
          type: 'driver-end',
          name: endOverride?.address?.trim() || `${driver.driver.name} (Stop)`,
          latitude: endOverride?.latitude ?? driver.driver.startLatitude,
          longitude: endOverride?.longitude ?? driver.driver.startLongitude,
        });

        const routeInfo: RouteInfo = {
          driver: driver.driver,
          waypoints: waypoints,
          totalDistanceKm: routeDto.totalKm,
          totalTimeMinutes: routeDto.totalMinutes,
          roadGeometry: routeDto.geometry && routeDto.geometry.length > 1
            ? routeDto.geometry.map(p => [Number(p.lat), Number(p.lng)] as [number, number])
            : undefined,
          startOverride,
          endOverride,
        };

        routes.set(driver.driver.toolId, routeInfo);
      });

      this.driverRoutes.set(routes);
      this.updateAllRoutesDisplay();
      this.refreshLocationMarkers();
      this.syncOverrideInputsFromRoute(this.getCurrentRoute());
    } catch (error) {
      console.error('Error loading existing routes:', error);
      this.driverRoutes.set(new Map());
      this.updateAllRoutesDisplay();
      this.refreshLocationMarkers();
      this.syncOverrideInputsFromRoute(this.getCurrentRoute());
    }
  }

  private async loadDriversWithAvailability(): Promise<void> {
    this.loadingDrivers.set(true);
    try {
      const date = this.selectedDate();
      const dateYmd = toYmd(date);
      const ownerId = this.selectedOwnerId();
      
      if (!ownerId) {
        this.driversWithAvailability.set([]);
        return;
      }
      
      // Get all active drivers
      const drivers = await this.driversApi.getDrivers(false).toPromise();
      const filteredDrivers = (drivers || []).filter(d => d.ownerId === ownerId);
      
      if (!filteredDrivers || filteredDrivers.length === 0) {
        this.driversWithAvailability.set([]);
        return;
      }

      // Fetch availability for each driver for the selected date
      const driversWithAvail = await Promise.all(
        filteredDrivers.map(async (driver) => {
          try {
            const availabilities = await this.driverAvailabilityApi
              .getAvailability(driver.toolId, dateYmd, dateYmd)
              .toPromise();
            
            const availability = availabilities && availabilities.length > 0 
              ? availabilities[0] 
              : null;
            
            return {
              driver,
              availability
            } as DriverWithAvailability;
          } catch (error) {
            console.error(`Error loading availability for driver ${driver.name}:`, error);
            return {
              driver,
              availability: null
            } as DriverWithAvailability;
          }
        })
      );

      // Filter out drivers with no availability or availableMinutes === 0
      const availableDrivers = driversWithAvail.filter(
        (item) => item.availability != null && item.availability.availableMinutes > 0
      );

      this.driversWithAvailability.set(availableDrivers);
    } catch (error) {
      console.error('Error loading drivers with availability:', error);
      this.messageService.add({
        severity: 'error',
        summary: 'Error',
        detail: 'Failed to load drivers',
      });
    } finally {
      this.loadingDrivers.set(false);
    }
  }

  formatTimeFromMinutes(minutes: number): string {
    const hours = Math.floor(minutes / 60);
    const mins = minutes % 60;
    return `${String(hours).padStart(2, '0')}:${String(mins).padStart(2, '0')}`;
  }

  formatAvailability(availability: DriverAvailabilityDto | null): string {
    if (!availability) {
      return 'Not available';
    }
    const start = this.formatTimeFromMinutes(availability.startMinuteOfDay);
    const end = this.formatTimeFromMinutes(availability.endMinuteOfDay);
    return `${start} - ${end}`;
  }

  getAvailableHours(availability: DriverAvailabilityDto | null): string {
    if (!availability) {
      return '0';
    }
    const hours = Math.floor(availability.availableMinutes / 60);
    const minutes = availability.availableMinutes % 60;
    if (minutes === 0) {
      return `${hours}h`;
    }
    return `${hours}h ${minutes}m`;
  }

  formatRouteTime(minutes: number): string {
    const hours = Math.floor(minutes / 60);
    const mins = minutes % 60;
    if (hours === 0) {
      return `${mins}m`;
    }
    if (mins === 0) {
      return `${hours}h`;
    }
    return `${hours}h ${mins}m`;
  }

  getTotalServiceMinutes(route: RouteInfo): number {
    return route.waypoints
      .filter(w => w.type === 'location')
      .reduce((sum, w) => sum + (w.serviceMinutes || 0), 0);
  }

  getDriveMinutes(route: RouteInfo): number {
    // Backend `totalTimeMinutes` includes travel + service.
    // Google Maps shows *travel only*, so compute drive minutes for comparison.
    const service = this.getTotalServiceMinutes(route);
    const total = Number(route.totalTimeMinutes) || 0;
    return Math.max(0, total - service);
  }

  getLocationNumber(index: number, waypoints: RouteWaypoint[]): number {
    // Count how many location waypoints come before this one
    let locationCount = 0;
    for (let i = 0; i < index; i++) {
      if (waypoints[i].type === 'location') {
        locationCount++;
      }
    }
    return locationCount + 1;
  }

  getLocationCount(waypoints: RouteWaypoint[]): number {
    return waypoints.filter(w => w.type === 'location').length;
  }

  getStartWaypoint(route: RouteInfo): RouteWaypoint | undefined {
    return route.waypoints.find((w) => w.type === 'driver-start');
  }

  getEndWaypoint(route: RouteInfo): RouteWaypoint | undefined {
    return route.waypoints.find((w) => w.type === 'driver-end');
  }

  getLocationWaypoints(route: RouteInfo): RouteWaypoint[] {
    return route.waypoints.filter((w) => w.type === 'location');
  }

  toggleSelectedRouteStop(index: number): void {
    const current = this.selectedRouteStopIndex();
    this.selectedRouteStopIndex.set(current === index ? null : index);
  }

  moveSelectedStop(direction: 'up' | 'down'): void {
    const route = this.getCurrentRouteForDisplay();
    const selectedIndex = this.selectedRouteStopIndex();
    if (!route || selectedIndex == null) {
      return;
    }

    const start = this.getStartWaypoint(route);
    const end = this.getEndWaypoint(route);
    const locations = this.getLocationWaypoints(route);

    const targetIndex = direction === 'up' ? selectedIndex - 1 : selectedIndex + 1;
    if (targetIndex < 0 || targetIndex >= locations.length) {
      return;
    }

    // Swap
    const tmp = locations[selectedIndex];
    locations[selectedIndex] = locations[targetIndex];
    locations[targetIndex] = tmp;

    // Rebuild waypoints: start + reordered locations + end
    const newWaypoints: RouteWaypoint[] = [];
    if (start) newWaypoints.push(start);
    newWaypoints.push(...locations);
    if (end) newWaypoints.push(end);

    route.waypoints = newWaypoints;
    route.roadGeometry = undefined;

    // Keep selection on the moved item
    this.selectedRouteStopIndex.set(targetIndex);

    this.calculateRouteMetrics(route);

    const routes = new Map(this.driverRoutes());
    routes.set(route.driver.toolId, { ...route });
    this.driverRoutes.set(routes);

    this.updateAllRoutesDisplay();
    this.refreshLocationMarkers();

    // Persist new order + recalc on backend (OSRM)
    this.scheduleSaveRouteToBackend(route);
  }

  completeRoute(): void {
    const route = this.getCurrentRoute();
    if (!route) {
      return;
    }

    // Ensure driver end position is there (it should already be added automatically)
    const hasEnd = route.waypoints.some(w => w.type === 'driver-end');
    if (!hasEnd && route.waypoints.length > 1) {
      const endPoint = this.getRouteEndPoint(route);
      const endWaypoint: RouteWaypoint = {
        type: 'driver-end',
        name: `${route.driver.name} (Stop)`,
        latitude: endPoint.latitude,
        longitude: endPoint.longitude,
      };
      route.waypoints.push(endWaypoint);
      this.calculateRouteMetrics(route);
      
      // Save route back to map
      const routes = new Map(this.driverRoutes());
      routes.set(route.driver.toolId, { ...route });
      this.driverRoutes.set(routes);
      
      this.updateRouteDisplay();
    }
    
    this.isBuildingRoute.set(false);
    
    const locationCount = route.waypoints.filter(w => w.type === 'location').length;
    this.messageService.add({
      severity: 'success',
      summary: 'Route Completed',
      detail: `Route created with ${locationCount} location(s). Total time: ${this.formatRouteTime(route.totalTimeMinutes)}`,
    });
  }

  private saveRouteToBackend(route: RouteInfo): void {
    const ownerId = Number(this.selectedOwnerId());
    const date = this.selectedDate();

    if (!ownerId) {
      // Can't save without owner - should be set before building routes
      return;
    }

    // Convert waypoints to stops (only location waypoints, exclude start/end)
    const locationWaypoints = route.waypoints.filter(w => w.type === 'location');
    const stops: CreateRouteStopRequest[] = [];

    for (let i = 0; i < locationWaypoints.length; i++) {
      const waypoint = locationWaypoints[i];
      const routeStart = this.getRouteStartPoint(route);
      const prevWaypoint = i === 0 
        ? { latitude: Number(routeStart.latitude), longitude: Number(routeStart.longitude) }
        : locationWaypoints[i - 1];

      const distanceKm = this.calculateDistance(
        Number(prevWaypoint.latitude),
        Number(prevWaypoint.longitude),
        Number(waypoint.latitude),
        Number(waypoint.longitude)
      );
      const travelMinutes = Math.round((distanceKm / 50) * 60); // 50 km/h average

      // Find the service location by erpId to get toolId
      const mapData = this.mapData();
      const serviceLocation = mapData?.items.find(item => item.erpId === waypoint.erpId);

      stops.push({
        sequence: i + 1, // Sequence starts at 1
        serviceLocationToolId: serviceLocation?.toolId,
        latitude: Number(waypoint.latitude),
        longitude: Number(waypoint.longitude),
        serviceMinutes: waypoint.serviceMinutes || 20,
        travelKmFromPrev: Number(distanceKm),
        travelMinutesFromPrev: travelMinutes
      });
    }

    // Ensure metrics are calculated before saving
    this.calculateRouteMetrics(route);

    // Validate route has metrics
    if (route.totalTimeMinutes == null || route.totalDistanceKm == null) {
      console.error('Route metrics not calculated:', {
        totalTimeMinutes: route.totalTimeMinutes,
        totalDistanceKm: route.totalDistanceKm,
        waypoints: route.waypoints.length
      });
      this.messageService.add({
        severity: 'error',
        summary: 'Error',
        detail: 'Route metrics not calculated. Please try again.',
      });
      return;
    }

    const totalMinutes = Number.isFinite(route.totalTimeMinutes) ? Math.round(route.totalTimeMinutes) : 0;
    const totalKm = Number.isFinite(route.totalDistanceKm) ? route.totalDistanceKm : 0;

    const request: CreateRouteRequest = {
      date: toYmd(date),
      ownerId: ownerId,
      // serviceTypeId removed - routes are identified by date, driver, owner only
      driverToolId: route.driver.toolId,
      totalMinutes: totalMinutes,
      totalKm: totalKm,
      startAddress: route.startOverride?.address,
      startLatitude: route.startOverride?.latitude,
      startLongitude: route.startOverride?.longitude,
      endAddress: route.endOverride?.address,
      endLatitude: route.endOverride?.latitude,
      endLongitude: route.endOverride?.longitude,
      weightTemplateId: this.selectedWeightTemplateId() ?? undefined,
      stops: stops
    };

    console.log('Saving route:', {
      totalMinutes: request.totalMinutes,
      totalKm: request.totalKm,
      stopsCount: request.stops.length
    });

    // Use debounced queued saving to avoid overlapping requests
    this.scheduleSaveRouteToBackend(route);
  }

  private scheduleSaveRouteToBackend(route: RouteInfo): void {
    const driverToolId = route.driver.toolId;
    this.routeSaveLatest.set(driverToolId, { ...route, waypoints: [...route.waypoints] });

    const existingTimer = this.routeSaveTimers.get(driverToolId);
    if (existingTimer) {
      clearTimeout(existingTimer);
    }

    // Debounce: wait a bit so rapid clicks produce a single save
    const timer = setTimeout(() => this.flushRouteSave(driverToolId), 400);
    this.routeSaveTimers.set(driverToolId, timer);
  }

  private flushRouteSave(driverToolId: string): void {
    if (this.routeSaveInFlight.has(driverToolId)) {
      this.routeSavePending.add(driverToolId);
      return;
    }

    const latest = this.routeSaveLatest.get(driverToolId);
    if (!latest) {
      return;
    }

    this.routeSaveInFlight.add(driverToolId);

    this.routesApi.upsertRoute(this.buildRouteSaveRequest(latest)).subscribe({
      next: (saved) => {
        // Update UI totals from backend (road routing)
        const routes = new Map(this.driverRoutes());
        const current = routes.get(driverToolId);
        if (current) {
          current.totalDistanceKm = Number(saved.totalKm) || 0;
          current.totalTimeMinutes = Number(saved.totalMinutes) || 0;
          current.startOverride =
            saved.startLatitude != null && saved.startLongitude != null
              ? {
                  address: saved.startAddress || undefined,
                  latitude: saved.startLatitude,
                  longitude: saved.startLongitude,
                }
              : undefined;
          current.endOverride =
            saved.endLatitude != null && saved.endLongitude != null
              ? {
                  address: saved.endAddress || undefined,
                  latitude: saved.endLatitude,
                  longitude: saved.endLongitude,
                }
              : undefined;
          this.updateRouteStartEndWaypoints(current);
          if (saved.geometry && saved.geometry.length > 1) {
            current.roadGeometry = saved.geometry.map((p) => [Number(p.lat), Number(p.lng)] as [number, number]);
          }
          routes.set(driverToolId, { ...current });
          this.driverRoutes.set(routes);
          this.updateAllRoutesDisplay();
          this.refreshLocationMarkers();
          if (this.getCurrentRoute()?.driver.toolId === driverToolId) {
            this.syncOverrideInputsFromRoute(current);
          }
        }

        // Backend updates ServiceLocation.Status (Planned/Open) during upsert.
        // Refresh map data so planned markers turn back to urgency colors when removed.
        this.refreshMapDataAfterRouteSave();
      },
      error: (err) => {
        console.error('Failed to save route:', err);
        const errorMessage =
          err?.error?.detail || err?.error?.title || err?.message || 'Failed to save route to backend';
        this.messageService.add({
          severity: 'error',
          summary: 'Error',
          detail: errorMessage,
        });
      },
      complete: () => {
        this.routeSaveInFlight.delete(driverToolId);
        if (this.routeSavePending.has(driverToolId)) {
          this.routeSavePending.delete(driverToolId);
          // Immediately save the newest version
          this.flushRouteSave(driverToolId);
        }
      },
    });
  }

  private buildRouteSaveRequest(route: RouteInfo): CreateRouteRequest {
    const ownerId = Number(this.selectedOwnerId());
    const date = this.selectedDate();

    // Convert waypoints to stops (only location waypoints, exclude start/end)
    const locationWaypoints = route.waypoints.filter((w) => w.type === 'location');
    const stops: CreateRouteStopRequest[] = [];

    for (let i = 0; i < locationWaypoints.length; i++) {
      const waypoint = locationWaypoints[i];
      const routeStart = this.getRouteStartPoint(route);
      const prevWaypoint =
        i === 0
          ? { latitude: Number(routeStart.latitude), longitude: Number(routeStart.longitude) }
          : locationWaypoints[i - 1];

      const distanceKm = this.calculateDistance(
        Number(prevWaypoint.latitude),
        Number(prevWaypoint.longitude),
        Number(waypoint.latitude),
        Number(waypoint.longitude)
      );
      const travelMinutes = Math.round((distanceKm / 50) * 60);

      const mapData = this.mapData();
      const serviceLocation = mapData?.items.find((item) => item.erpId === waypoint.erpId);

      stops.push({
        sequence: i + 1,
        serviceLocationToolId: serviceLocation?.toolId,
        latitude: Number(waypoint.latitude),
        longitude: Number(waypoint.longitude),
        serviceMinutes: waypoint.serviceMinutes || 20,
        travelKmFromPrev: Number(distanceKm),
        travelMinutesFromPrev: travelMinutes,
      });
    }

    this.calculateRouteMetrics(route);
    const totalMinutes = Number.isFinite(route.totalTimeMinutes) ? Math.round(route.totalTimeMinutes) : 0;
    const totalKm = Number.isFinite(route.totalDistanceKm) ? route.totalDistanceKm : 0;

    return {
      date: toYmd(date),
      ownerId: ownerId,
      driverToolId: route.driver.toolId,
      totalMinutes: totalMinutes,
      totalKm: totalKm,
      startAddress: route.startOverride?.address,
      startLatitude: route.startOverride?.latitude,
      startLongitude: route.startOverride?.longitude,
      endAddress: route.endOverride?.address,
      endLatitude: route.endOverride?.latitude,
      endLongitude: route.endOverride?.longitude,
      weightTemplateId: this.selectedWeightTemplateId() ?? undefined,
      stops: stops,
    };
  }

  clearRoute(): void {
    const selected = this.selectedDriver();
    if (!selected) {
      return;
    }

    const ownerId = this.selectedOwnerId();
    if (!ownerId) {
      return;
    }
    
    // Remove route for selected driver only
    const routes = new Map(this.driverRoutes());
    const hadRoute = routes.has(selected.driver.toolId);
    routes.delete(selected.driver.toolId);
    this.driverRoutes.set(routes);
    this.syncOverrideInputsFromRoute(this.getCurrentRoute());
    
    this.isBuildingRoute.set(false);
    
    // Update all routes display (will remove the cleared route)
    this.updateAllRoutesDisplay();
    this.refreshLocationMarkers();
    
    if (hadRoute) {
      this.routesApi.deleteDriverDayRoute(this.selectedDate(), selected.driver.toolId, ownerId).subscribe({
        next: () => {
          this.loadExistingRoutes();
          this.refreshMapDataAfterRouteSave();
        },
        error: (err) => {
          this.messageService.add({
            severity: 'error',
            summary: 'Delete failed',
            detail: err?.error?.message || err?.message || 'Failed to delete route',
          });
        },
      });
      this.messageService.add({
        severity: 'info',
        summary: 'Route Cleared',
        detail: 'Route has been cleared',
      });
    }
  }

  clearAllRoutesForDay(): void {
    const ownerId = this.selectedOwnerId();
    if (!ownerId) {
      return;
    }

    const hadRoutes = this.driverRoutes().size > 0;
    this.driverRoutes.set(new Map());
    this.isBuildingRoute.set(false);
    this.syncOverrideInputsFromRoute(null);
    this.updateAllRoutesDisplay();
    this.refreshLocationMarkers();

    if (hadRoutes) {
      this.routesApi.deleteDayRoutes(this.selectedDate(), ownerId).subscribe({
        next: (result) => {
          this.loadExistingRoutes();
          this.refreshMapDataAfterRouteSave();
          if (result.skippedFixed > 0) {
            this.messageService.add({
              severity: 'warn',
              summary: 'Some routes skipped',
              detail: `${result.skippedFixed} fixed route(s) were not deleted`,
            });
          }
        },
        error: (err) => {
          this.messageService.add({
            severity: 'error',
            summary: 'Delete failed',
            detail: err?.error?.message || err?.message || 'Failed to delete day routes',
          });
        },
      });
      this.messageService.add({
        severity: 'info',
        summary: 'Routes Cleared',
        detail: 'All routes for this day have been cleared',
      });
    }
  }

  getCurrentRouteForDisplay(): RouteInfo | null {
    return this.getCurrentRoute();
  }
}

