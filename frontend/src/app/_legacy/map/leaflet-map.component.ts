import {
  afterNextRender,
  Component,
  effect,
  ElementRef,
  input,
  OnDestroy,
  OnInit,
  output,
  viewChild,
} from '@angular/core';
import type * as L from 'leaflet';

export interface MapMarker {
  id: string;
  lat: number;
  lon: number;
  label: string;
  kind: 'unplanned' | 'planned';
  driverId?: number;
}

export interface PolylinePoint {
  lat: number;
  lon: number;
}

export interface RoutePolyline {
  id: string;
  color: string;
  points: PolylinePoint[];
}

@Component({
  selector: 'app-leaflet-map',
  imports: [],
  templateUrl: './leaflet-map.component.html',
  styleUrl: './leaflet-map.component.css',
  standalone: true,
})
export class LeafletMapComponent implements OnInit, OnDestroy {
  markers = input.required<MapMarker[]>();
  selectedMarkerId = input<string | undefined>(undefined);
  polyline = input<PolylinePoint[]>([]);
  routes = input<RoutePolyline[]>([]);
  centerOnMarkerId = input<string | undefined>(undefined);

  markerClicked = output<string>();

  mapContainer = viewChild.required<ElementRef<HTMLDivElement>>('mapContainer');

  private map: L.Map | null = null;
  private markerLayer: L.LayerGroup | null = null;
  private polylineLayer: L.Polyline | null = null;
  private routeLayers = new Map<string, L.Polyline>();
  private markerInstances = new Map<string, L.Marker>();
  private currentMarkers: MapMarker[] = [];

  private mapInitialized = false;

  constructor() {
    // Initialize map after view is ready
    afterNextRender(async () => {
      await this.initMap();
      this.mapInitialized = true;
      // Trigger initial updates
      const markers = this.markers();
      if (markers.length > 0) {
        await this.updateMarkers(markers);
      }
      const polylinePoints = this.polyline();
      if (polylinePoints.length > 0) {
        await this.updatePolyline(polylinePoints);
      }
      const routes = this.routes();
      if (routes.length > 0) {
        await this.updateRoutes(routes);
      }
    });

    // Set up effects in constructor (injection context)
    // They will trigger once map is initialized
    effect(() => {
      if (this.mapInitialized) {
        const markers = this.markers();
        // Use setTimeout to ensure map is ready
        setTimeout(() => {
          if (this.mapInitialized) {
            this.updateMarkers(markers);
          }
        }, 0);
      }
    });
    effect(() => {
      if (this.mapInitialized) {
        const selectedId = this.selectedMarkerId();
        this.updateSelectedMarker(selectedId);
      }
    });
    effect(() => {
      if (this.mapInitialized) {
        const polylinePoints = this.polyline();
        this.updatePolyline(polylinePoints);
      }
    });
    effect(() => {
      if (this.mapInitialized) {
        const routes = this.routes();
        // Always update routes, even if empty (to clear old routes)
        // Use setTimeout to ensure map is ready
        setTimeout(() => {
          if (this.mapInitialized) {
            this.updateRoutes(routes);
          }
        }, 0);
      }
    });
    effect(() => {
      if (this.mapInitialized) {
        const centerOnId = this.centerOnMarkerId();
        if (centerOnId) {
          this.centerOnMarker(centerOnId);
        }
      }
    });
  }

  ngOnInit(): void {
    // Effects are now in constructor
  }

  ngOnDestroy(): void {
    if (this.map) {
      this.map.remove();
      this.map = null;
    }
  }

  private async initMap(): Promise<void> {
    const L = await import('leaflet');
    const container = this.mapContainer().nativeElement;

    this.map = L.map(container).setView([51.0, 4.35], 8);

    L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
      attribution: '© OpenStreetMap contributors',
      maxZoom: 19,
    }).addTo(this.map);

    this.markerLayer = L.layerGroup().addTo(this.map);
    this.polylineLayer = L.polyline([], { color: '#3388ff', weight: 4 }).addTo(this.map);

    // Invalidate size to ensure map renders correctly
    setTimeout(() => {
      if (this.map) {
        this.map.invalidateSize();
      }
    }, 100);
  }

  private async updateMarkers(markers: MapMarker[]): Promise<void> {
    if (!this.map || !this.markerLayer) {
      return;
    }

    const L = await import('leaflet');

    // Store current markers for later reference
    this.currentMarkers = markers;

    // Clear existing markers completely
    this.markerLayer.clearLayers();
    this.markerInstances.clear();

    // Force map to invalidate size to ensure proper rendering
    this.map.invalidateSize();

    if (markers.length === 0) {
      return;
    }

    // Create markers
    const bounds: L.LatLngBoundsExpression = [];

    for (const marker of markers) {
      // Validate coordinates
      if (isNaN(marker.lat) || isNaN(marker.lon) || marker.lat === 0 || marker.lon === 0) {
        console.warn('Invalid marker coordinates:', marker);
        continue;
      }

      let iconUrl: string;
      // Check if this is a start marker
      if (marker.id === 'start' || marker.id.endsWith('-start')) {
        // Use a special icon for start position (home icon or green marker)
        iconUrl =
          'https://raw.githubusercontent.com/pointhi/leaflet-color-markers/master/img/marker-icon-green.png';
      } else if (marker.kind === 'unplanned') {
        iconUrl =
          'https://raw.githubusercontent.com/pointhi/leaflet-color-markers/master/img/marker-icon-red.png';
      } else if (marker.driverId !== undefined) {
        // Use different colored markers for different drivers
        const driverColors = [
          'blue',
          'pink',
          'green',
          'orange',
          'purple',
          'red',
          'yellow',
          'violet',
        ];
        const colorIndex = marker.driverId % driverColors.length;
        const color = driverColors[colorIndex];
        iconUrl = `https://raw.githubusercontent.com/pointhi/leaflet-color-markers/master/img/marker-icon-${color}.png`;
      } else {
        iconUrl =
          'https://raw.githubusercontent.com/pointhi/leaflet-color-markers/master/img/marker-icon-blue.png';
      }

      const icon = L.icon({
        iconUrl,
        shadowUrl: 'https://cdnjs.cloudflare.com/ajax/libs/leaflet/1.9.4/images/marker-shadow.png',
        iconSize: [25, 41],
        iconAnchor: [12, 41],
        popupAnchor: [1, -34],
        shadowSize: [41, 41],
      });

      const leafletMarker = L.marker([marker.lat, marker.lon], { icon })
        .bindPopup(marker.label)
        .addTo(this.markerLayer!);

      leafletMarker.on('click', () => {
        this.markerClicked.emit(marker.id);
      });

      this.markerInstances.set(marker.id, leafletMarker);
      bounds.push([marker.lat, marker.lon]);
    }

    // Fit bounds to all markers, polyline, and routes (if any)
    const polylinePoints = this.polyline();
    if (polylinePoints.length > 0) {
      for (const point of polylinePoints) {
        bounds.push([point.lat, point.lon]);
      }
    }

    const routes = this.routes();
    if (routes.length > 0) {
      for (const route of routes) {
        for (const point of route.points) {
          bounds.push([point.lat, point.lon]);
        }
      }
    }

    if (bounds.length > 0) {
      // Use setTimeout to ensure map is ready for fitBounds
      setTimeout(() => {
        if (this.map) {
          this.map.invalidateSize();
          this.map.fitBounds(bounds, { padding: [50, 50] });
        }
      }, 0);
    }
  }

  private async updatePolyline(points: PolylinePoint[]): Promise<void> {
    if (!this.map || !this.polylineLayer) {
      return;
    }

    if (points.length === 0) {
      this.polylineLayer.setLatLngs([]);
      return;
    }

    const L = await import('leaflet');
    const latlngs: L.LatLngTuple[] = points.map((p) => [p.lat, p.lon]);
    this.polylineLayer.setLatLngs(latlngs);

    // Update bounds to include polyline
    const bounds: L.LatLngBoundsExpression = [];
    for (const marker of this.currentMarkers) {
      bounds.push([marker.lat, marker.lon]);
    }
    for (const point of points) {
      bounds.push([point.lat, point.lon]);
    }

    if (bounds.length > 0) {
      this.map.fitBounds(bounds, { padding: [50, 50] });
    }
  }

  private async updateRoutes(routes: RoutePolyline[]): Promise<void> {
    if (!this.map) {
      return;
    }

    const L = await import('leaflet');

    // Remove existing route layers completely
    this.routeLayers.forEach((layer) => {
      this.map!.removeLayer(layer);
    });
    this.routeLayers.clear();

    // Force map to invalidate size to ensure proper rendering
    this.map.invalidateSize();

    if (routes.length === 0) {
      // If no routes, still update bounds with markers only
      const bounds: L.LatLngBoundsExpression = [];
      for (const marker of this.currentMarkers) {
        bounds.push([marker.lat, marker.lon]);
      }
      if (bounds.length > 0) {
        setTimeout(() => {
          if (this.map) {
            this.map.invalidateSize();
            this.map.fitBounds(bounds, { padding: [50, 50] });
          }
        }, 0);
      }
      return;
    }

    // Create polyline for each route with its color
    const bounds: L.LatLngBoundsExpression = [];
    for (const marker of this.currentMarkers) {
      bounds.push([marker.lat, marker.lon]);
    }

    for (const route of routes) {
      if (route.points.length === 0) {
        continue;
      }

      const latlngs: L.LatLngTuple[] = route.points.map((p) => [p.lat, p.lon]);
      const polyline = L.polyline(latlngs, {
        color: route.color,
        weight: 4,
        opacity: 0.7,
      }).addTo(this.map);

      this.routeLayers.set(route.id, polyline);

      // Add route points to bounds
      for (const point of route.points) {
        bounds.push([point.lat, point.lon]);
      }
    }

    // Update bounds - use setTimeout to ensure map is ready
    if (bounds.length > 0) {
      setTimeout(() => {
        if (this.map) {
          this.map.invalidateSize();
          this.map.fitBounds(bounds, { padding: [50, 50] });
        }
      }, 0);
    }
  }

  private centerOnMarker(markerId: string): void {
    if (!this.map) {
      return;
    }

    const marker = this.markerInstances.get(markerId);
    if (marker) {
      this.map.setView(marker.getLatLng(), Math.max(this.map.getZoom(), 13));
    }
  }

  private async updateSelectedMarker(selectedId: string | undefined): Promise<void> {
    if (!this.map) {
      return;
    }

    const L = await import('leaflet');

    // Build map of marker kinds
    const markerKindMap = new Map<string, 'unplanned' | 'planned'>();
    for (const m of this.currentMarkers) {
      markerKindMap.set(m.id, m.kind);
    }

    // Build map of marker driver IDs
    const markerDriverMap = new Map<string, number | undefined>();
    for (const m of this.currentMarkers) {
      markerDriverMap.set(m.id, m.driverId);
    }

    // Reset all markers to default
    this.markerInstances.forEach((marker, id) => {
      const kind = markerKindMap.get(id) || 'unplanned';
      const driverId = markerDriverMap.get(id);

      let iconUrl: string;
      // Check if this is a start marker
      if (id === 'start' || id.endsWith('-start')) {
        // Use a special icon for start position (green marker)
        iconUrl =
          'https://raw.githubusercontent.com/pointhi/leaflet-color-markers/master/img/marker-icon-green.png';
      } else if (kind === 'unplanned') {
        iconUrl =
          'https://raw.githubusercontent.com/pointhi/leaflet-color-markers/master/img/marker-icon-red.png';
      } else if (driverId !== undefined) {
        const driverColors = [
          'blue',
          'pink',
          'green',
          'orange',
          'purple',
          'red',
          'yellow',
          'violet',
        ];
        const colorIndex = driverId % driverColors.length;
        const color = driverColors[colorIndex];
        iconUrl = `https://raw.githubusercontent.com/pointhi/leaflet-color-markers/master/img/marker-icon-${color}.png`;
      } else {
        iconUrl =
          'https://raw.githubusercontent.com/pointhi/leaflet-color-markers/master/img/marker-icon-blue.png';
      }

      const icon = L.icon({
        iconUrl,
        shadowUrl: 'https://cdnjs.cloudflare.com/ajax/libs/leaflet/1.9.4/images/marker-shadow.png',
        iconSize: [25, 41],
        iconAnchor: [12, 41],
        popupAnchor: [1, -34],
        shadowSize: [41, 41],
      });
      marker.setIcon(icon);
    });

    // Highlight selected marker
    if (selectedId) {
      const selectedMarker = this.markerInstances.get(selectedId);
      if (selectedMarker) {
        const icon = L.icon({
          iconUrl:
            'https://raw.githubusercontent.com/pointhi/leaflet-color-markers/master/img/marker-icon-green.png',
          shadowUrl:
            'https://cdnjs.cloudflare.com/ajax/libs/leaflet/1.9.4/images/marker-shadow.png',
          iconSize: [35, 51],
          iconAnchor: [17, 51],
          popupAnchor: [1, -34],
          shadowSize: [41, 41],
        });
        selectedMarker.setIcon(icon);
        selectedMarker.openPopup();
        this.map.setView(selectedMarker.getLatLng(), Math.max(this.map.getZoom(), 13));
      }
    }
  }
}
