import { Component, computed, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { CardModule } from 'primeng/card';
import { ButtonModule } from 'primeng/button';
import { InputTextModule } from 'primeng/inputtext';
import { HelpManualComponent } from '@components/help-manual/help-manual.component';
import { MANUAL_SECTIONS, type ManualSection } from '@app/_data/manual.data';
import {
  TECHNICAL_DIAGRAMS,
  TECHNICAL_SECTIONS,
  type TechnicalDiagram,
  type TechnicalSection,
} from '@app/_data/technical-manual.data';
import { AuthService } from '@services/auth.service';

@Component({
  selector: 'app-start',
  imports: [
    CommonModule,
    FormsModule,
    CardModule,
    ButtonModule,
    InputTextModule,
    RouterLink,
    HelpManualComponent,
  ],
  template: `
    <div
      class="min-h-screen bg-gradient-to-br from-gray-50 to-gray-100 dark:from-surface-900 dark:to-surface-800 py-12 px-4"
    >
      <div class="max-w-6xl mx-auto">
        <!-- Hero Section -->
        <div
          class="bg-white dark:bg-surface-900 rounded-2xl shadow-sm border border-gray-200 dark:border-surface-700 p-8 lg:p-12 mb-8"
        >
          <div class="grid lg:grid-cols-2 gap-8 lg:gap-12 items-center">
            <!-- Left: Logo, Title, Features -->
            <div class="space-y-6">
              <!-- Logo -->
              <div class="flex items-center gap-4">
                <img
                  src="assets/images/trescal_logo.png"
                  alt="Trescal"
                  class="h-12 object-contain"
                  onerror="this.style.display='none'; this.nextElementSibling.style.display='block';"
                />
                <span
                  class="text-2xl font-semibold text-gray-900 dark:text-surface-50"
                  style="display: none;"
                  >Trescal</span
                >
              </div>

              <!-- Title & Subtitle -->
              <div>
                <div class="flex items-center gap-2 mb-3">
                  <h1 class="text-4xl lg:text-5xl font-bold text-gray-900 dark:text-surface-50">
                    Service Planning Tool
                  </h1>
                  <app-help-manual [sectionId]="'dashboard'" [title]="'Dashboard Manual'" />
                </div>
                <p class="text-lg text-gray-600 dark:text-surface-300">
                  Plan and optimize service executions for drivers.
                </p>
              </div>

              <!-- Feature Bullets -->
              <div class="space-y-3">
                <div class="flex items-center gap-3 text-gray-700 dark:text-surface-200">
                  <i class="pi pi-calendar text-blue-600 dark:text-blue-400 text-xl"></i>
                  <span>Driver availability grid</span>
                </div>
                <div class="flex items-center gap-3 text-gray-700 dark:text-surface-200">
                  <i class="pi pi-wrench text-blue-600 dark:text-blue-400 text-xl"></i>
                  <span>Service locations backlog & prioritization</span>
                </div>
                <div class="flex items-center gap-3 text-gray-700 dark:text-surface-200">
                  <i class="pi pi-directions text-blue-600 dark:text-blue-400 text-xl"></i>
                  <span>Route generation (OR-Tools ready)</span>
                </div>
                <div class="flex items-center gap-3 text-gray-700 dark:text-surface-200">
                  <i class="pi pi-file-excel text-blue-600 dark:text-blue-400 text-xl"></i>
                  <span>Excel & ERP sync</span>
                </div>
              </div>

              <!-- Action Buttons -->
              <div class="flex flex-wrap gap-3 pt-4">
                <p-button
                  label="Drivers"
                  icon="pi pi-users"
                  routerLink="/drivers"
                  [outlined]="true"
                />
                <p-button
                  label="Service Location"
                  icon="pi pi-map-marker"
                  routerLink="/service-locations"
                  [outlined]="true"
                />
              </div>
            </div>

            <!-- Right: Map Preview -->
            <div class="relative">
              <div
                class="aspect-[16/10] rounded-xl overflow-hidden relative bg-gradient-to-br from-blue-50 to-indigo-100 dark:from-blue-900/20 dark:to-indigo-900/20 border border-gray-200 dark:border-surface-700"
                style="background-image: 
                  linear-gradient(rgba(0,0,0,0.02) 1px, transparent 1px),
                  linear-gradient(90deg, rgba(0,0,0,0.02) 1px, transparent 1px);
                  background-size: 40px 40px;"
              >
                <!-- Map Markers -->
                <div
                  class="absolute top-[20%] left-[25%] w-4 h-4 bg-red-500 rounded-full ring-4 ring-red-500/30 shadow-lg"
                  style="transform: translate(-50%, -50%);"
                >
                  <div
                    class="absolute top-0 left-0 w-full h-full bg-red-500 rounded-full animate-ping opacity-75"
                  ></div>
                </div>
                <div
                  class="absolute top-[35%] left-[45%] w-4 h-4 bg-blue-500 rounded-full ring-4 ring-blue-500/30 shadow-lg"
                  style="transform: translate(-50%, -50%);"
                ></div>
                <div
                  class="absolute top-[50%] left-[30%] w-4 h-4 bg-green-500 rounded-full ring-4 ring-green-500/30 shadow-lg"
                  style="transform: translate(-50%, -50%);"
                ></div>
                <div
                  class="absolute top-[60%] left-[55%] w-4 h-4 bg-yellow-500 rounded-full ring-4 ring-yellow-500/30 shadow-lg"
                  style="transform: translate(-50%, -50%);"
                ></div>
                <div
                  class="absolute top-[25%] left-[65%] w-4 h-4 bg-purple-500 rounded-full ring-4 ring-purple-500/30 shadow-lg"
                  style="transform: translate(-50%, -50%);"
                ></div>
                <div
                  class="absolute top-[70%] left-[40%] w-4 h-4 bg-orange-500 rounded-full ring-4 ring-orange-500/30 shadow-lg"
                  style="transform: translate(-50%, -50%);"
                ></div>
                <div
                  class="absolute top-[45%] left-[70%] w-4 h-4 bg-pink-500 rounded-full ring-4 ring-pink-500/30 shadow-lg"
                  style="transform: translate(-50%, -50%);"
                ></div>
                <div
                  class="absolute top-[75%] left-[60%] w-4 h-4 bg-teal-500 rounded-full ring-4 ring-teal-500/30 shadow-lg"
                  style="transform: translate(-50%, -50%);"
                ></div>

                <!-- Service Area Badge -->
                <div
                  class="absolute top-4 right-4 bg-white dark:bg-surface-800 px-3 py-1.5 rounded-lg shadow-md border border-gray-200 dark:border-surface-700"
                >
                  <div
                    class="flex items-center gap-2 text-xs font-medium text-gray-700 dark:text-surface-200"
                  >
                    <i class="pi pi-map-marker text-blue-600 dark:text-blue-400"></i>
                    <span>Service Area</span>
                  </div>
                </div>
              </div>
            </div>
          </div>
        </div>

        <!-- Manual Section -->
        <div
          class="bg-white dark:bg-surface-900 rounded-2xl shadow-sm border border-gray-200 dark:border-surface-700 p-8 lg:p-10"
        >
          <div class="flex flex-wrap items-center justify-between gap-4 mb-4">
            <h2 class="text-2xl font-bold text-gray-900 dark:text-surface-50">
              Planner Tool Manual
            </h2>
            <app-help-manual
              [sections]="manualSections"
              [showLinks]="true"
              [title]="'Full Manual'"
            />
          </div>

          <div class="mb-5">
            <div class="flex items-center gap-2">
              <i class="pi pi-search text-gray-500"></i>
              <input
                pInputText
                placeholder="Search the manual..."
                class="w-full"
                [(ngModel)]="manualSearch"
              />
            </div>
          </div>

          <div *ngIf="filteredManualSections.length > 0; else manualEmpty">
            <div
              class="mb-6 border-b border-gray-200 pb-4 last:border-b-0 last:pb-0"
              *ngFor="let section of filteredManualSections"
            >
              <div class="flex items-center justify-between gap-2">
                <h3 class="text-lg font-semibold text-gray-900 dark:text-surface-50">
                  {{ section.title }}
                </h3>
                <a
                  class="text-sm text-blue-600 hover:underline"
                  *ngIf="section.route"
                  [routerLink]="section.route"
                >
                  Open tab
                </a>
              </div>
              <p class="text-sm text-gray-600 dark:text-surface-300 mb-2">{{ section.summary }}</p>

              <div class="mb-3" *ngIf="section.functions.length > 0">
                <div class="text-xs font-semibold uppercase tracking-wide text-gray-500 mb-1">
                  Functions
                </div>
                <ul class="list-disc pl-5 text-sm text-gray-700 dark:text-surface-200 space-y-1">
                  <li *ngFor="let item of section.functions">{{ item }}</li>
                </ul>
              </div>

              <div *ngIf="section.options.length > 0">
                <div class="text-xs font-semibold uppercase tracking-wide text-gray-500 mb-1">
                  Options
                </div>
                <ul class="list-disc pl-5 text-sm text-gray-700 dark:text-surface-200 space-y-1">
                  <li *ngFor="let item of section.options">{{ item }}</li>
                </ul>
              </div>
            </div>
          </div>

          <ng-template #manualEmpty>
            <div class="text-sm text-gray-500">No matching manual entries.</div>
          </ng-template>
        </div>

        <!-- Technical Manual Section (SuperAdmin) -->
        <div
          class="bg-white dark:bg-surface-900 rounded-2xl shadow-sm border border-gray-200 dark:border-surface-700 p-8 lg:p-10 mt-8"
          *ngIf="isSuperAdmin()"
        >
          <div class="flex flex-wrap items-center justify-between gap-4 mb-4">
            <div>
              <h2 class="text-2xl font-bold text-gray-900 dark:text-surface-50">
                Technical Manual
              </h2>
              <p class="text-sm text-gray-600 dark:text-surface-300">SuperAdmin only</p>
            </div>
          </div>

          <div class="mb-5">
            <div class="flex items-center gap-2">
              <i class="pi pi-search text-gray-500"></i>
              <input
                pInputText
                placeholder="Search technical manual..."
                class="w-full"
                [(ngModel)]="technicalSearch"
              />
            </div>
          </div>

          <div class="mb-6" *ngIf="filteredTechnicalDiagrams.length > 0">
            <div class="text-xs font-semibold uppercase tracking-wide text-gray-500 mb-2">
              Diagrams
            </div>
            <div class="grid grid-cols-1 lg:grid-cols-2 gap-4">
              <div
                class="border border-gray-200 rounded-lg p-3 bg-gray-50"
                *ngFor="let diagram of filteredTechnicalDiagrams"
              >
                <div class="text-sm font-semibold text-gray-800 mb-2">{{ diagram.title }}</div>

                <ng-container [ngSwitch]="diagram.id">
                  <svg
                    viewBox="0 0 780 160"
                    role="img"
                    aria-label="Planning flow diagram"
                    *ngSwitchCase="'flow'"
                  >
                    <defs>
                      <marker
                        id="arrow"
                        markerWidth="10"
                        markerHeight="8"
                        refX="9"
                        refY="4"
                        orient="auto"
                      >
                        <path d="M0,0 L10,4 L0,8 Z" fill="#4b5563" />
                      </marker>
                    </defs>
                    <rect
                      x="10"
                      y="30"
                      width="130"
                      height="50"
                      rx="8"
                      fill="#ffffff"
                      stroke="#94a3b8"
                    />
                    <text x="75" y="60" text-anchor="middle" font-size="12" fill="#111827">
                      Map UI
                    </text>

                    <rect
                      x="170"
                      y="30"
                      width="160"
                      height="50"
                      rx="8"
                      fill="#ffffff"
                      stroke="#94a3b8"
                    />
                    <text x="250" y="55" text-anchor="middle" font-size="12" fill="#111827">
                      AutoRoutes
                    </text>
                    <text x="250" y="70" text-anchor="middle" font-size="10" fill="#6b7280">
                      Controller
                    </text>

                    <rect
                      x="360"
                      y="30"
                      width="200"
                      height="50"
                      rx="8"
                      fill="#ffffff"
                      stroke="#94a3b8"
                    />
                    <text x="460" y="55" text-anchor="middle" font-size="12" fill="#111827">
                      Planning Services
                    </text>
                    <text x="460" y="70" text-anchor="middle" font-size="10" fill="#6b7280">
                      Routing, weights
                    </text>

                    <rect
                      x="590"
                      y="30"
                      width="180"
                      height="50"
                      rx="8"
                      fill="#ffffff"
                      stroke="#94a3b8"
                    />
                    <text x="680" y="55" text-anchor="middle" font-size="12" fill="#111827">
                      EF Core
                    </text>
                    <text x="680" y="70" text-anchor="middle" font-size="10" fill="#6b7280">
                      SQL Server
                    </text>

                    <line
                      x1="140"
                      y1="55"
                      x2="170"
                      y2="55"
                      stroke="#4b5563"
                      stroke-width="2"
                      marker-end="url(#arrow)"
                    />
                    <line
                      x1="330"
                      y1="55"
                      x2="360"
                      y2="55"
                      stroke="#4b5563"
                      stroke-width="2"
                      marker-end="url(#arrow)"
                    />
                    <line
                      x1="560"
                      y1="55"
                      x2="590"
                      y2="55"
                      stroke="#4b5563"
                      stroke-width="2"
                      marker-end="url(#arrow)"
                    />
                  </svg>

                  <svg
                    viewBox="0 0 780 190"
                    role="img"
                    aria-label="Core data model diagram"
                    *ngSwitchCase="'data-model'"
                  >
                    <defs>
                      <marker
                        id="arrow2"
                        markerWidth="10"
                        markerHeight="8"
                        refX="9"
                        refY="4"
                        orient="auto"
                      >
                        <path d="M0,0 L10,4 L0,8 Z" fill="#4b5563" />
                      </marker>
                    </defs>
                    <rect
                      x="20"
                      y="20"
                      width="150"
                      height="50"
                      rx="8"
                      fill="#ffffff"
                      stroke="#94a3b8"
                    />
                    <text x="95" y="50" text-anchor="middle" font-size="12" fill="#111827">
                      Drivers
                    </text>

                    <rect
                      x="230"
                      y="20"
                      width="150"
                      height="50"
                      rx="8"
                      fill="#ffffff"
                      stroke="#94a3b8"
                    />
                    <text x="305" y="50" text-anchor="middle" font-size="12" fill="#111827">
                      Routes
                    </text>

                    <rect
                      x="440"
                      y="20"
                      width="170"
                      height="50"
                      rx="8"
                      fill="#ffffff"
                      stroke="#94a3b8"
                    />
                    <text x="525" y="50" text-anchor="middle" font-size="12" fill="#111827">
                      ServiceLocations
                    </text>

                    <rect
                      x="230"
                      y="110"
                      width="150"
                      height="50"
                      rx="8"
                      fill="#ffffff"
                      stroke="#94a3b8"
                    />
                    <text x="305" y="140" text-anchor="middle" font-size="12" fill="#111827">
                      RouteStops
                    </text>

                    <rect
                      x="20"
                      y="110"
                      width="170"
                      height="50"
                      rx="8"
                      fill="#ffffff"
                      stroke="#94a3b8"
                    />
                    <text x="105" y="140" text-anchor="middle" font-size="12" fill="#111827">
                      RouteMessages
                    </text>

                    <rect
                      x="440"
                      y="110"
                      width="220"
                      height="50"
                      rx="8"
                      fill="#ffffff"
                      stroke="#94a3b8"
                    />
                    <text x="550" y="140" text-anchor="middle" font-size="12" fill="#111827">
                      RouteChangeNotifications
                    </text>

                    <line
                      x1="170"
                      y1="45"
                      x2="230"
                      y2="45"
                      stroke="#4b5563"
                      stroke-width="2"
                      marker-end="url(#arrow2)"
                    />
                    <line
                      x1="305"
                      y1="70"
                      x2="305"
                      y2="110"
                      stroke="#4b5563"
                      stroke-width="2"
                      marker-end="url(#arrow2)"
                    />
                    <line
                      x1="380"
                      y1="45"
                      x2="440"
                      y2="45"
                      stroke="#4b5563"
                      stroke-width="2"
                      marker-end="url(#arrow2)"
                    />
                    <line
                      x1="230"
                      y1="135"
                      x2="190"
                      y2="135"
                      stroke="#4b5563"
                      stroke-width="2"
                      marker-end="url(#arrow2)"
                    />
                    <line
                      x1="380"
                      y1="135"
                      x2="440"
                      y2="135"
                      stroke="#4b5563"
                      stroke-width="2"
                      marker-end="url(#arrow2)"
                    />
                  </svg>

                  <svg
                    viewBox="0 0 780 170"
                    role="img"
                    aria-label="Travel time model diagram"
                    *ngSwitchCase="'travel-model'"
                  >
                    <defs>
                      <marker
                        id="arrow3"
                        markerWidth="10"
                        markerHeight="8"
                        refX="9"
                        refY="4"
                        orient="auto"
                      >
                        <path d="M0,0 L10,4 L0,8 Z" fill="#4b5563" />
                      </marker>
                    </defs>
                    <rect
                      x="30"
                      y="30"
                      width="200"
                      height="50"
                      rx="8"
                      fill="#ffffff"
                      stroke="#94a3b8"
                    />
                    <text x="130" y="55" text-anchor="middle" font-size="12" fill="#111827">
                      TravelTimeRegions
                    </text>

                    <rect
                      x="280"
                      y="20"
                      width="210"
                      height="50"
                      rx="8"
                      fill="#ffffff"
                      stroke="#94a3b8"
                    />
                    <text x="385" y="45" text-anchor="middle" font-size="12" fill="#111827">
                      RegionSpeedProfiles
                    </text>

                    <rect
                      x="280"
                      y="95"
                      width="210"
                      height="50"
                      rx="8"
                      fill="#ffffff"
                      stroke="#94a3b8"
                    />
                    <text x="385" y="120" text-anchor="middle" font-size="12" fill="#111827">
                      LearnedTravelStats
                    </text>

                    <rect
                      x="540"
                      y="58"
                      width="200"
                      height="50"
                      rx="8"
                      fill="#ffffff"
                      stroke="#94a3b8"
                    />
                    <text x="640" y="83" text-anchor="middle" font-size="12" fill="#111827">
                      TravelTimeModel
                    </text>

                    <line
                      x1="230"
                      y1="55"
                      x2="280"
                      y2="45"
                      stroke="#4b5563"
                      stroke-width="2"
                      marker-end="url(#arrow3)"
                    />
                    <line
                      x1="230"
                      y1="55"
                      x2="280"
                      y2="120"
                      stroke="#4b5563"
                      stroke-width="2"
                      marker-end="url(#arrow3)"
                    />
                    <line
                      x1="490"
                      y1="70"
                      x2="540"
                      y2="83"
                      stroke="#4b5563"
                      stroke-width="2"
                      marker-end="url(#arrow3)"
                    />
                    <line
                      x1="490"
                      y1="120"
                      x2="540"
                      y2="108"
                      stroke="#4b5563"
                      stroke-width="2"
                      marker-end="url(#arrow3)"
                    />
                  </svg>
                </ng-container>
              </div>
            </div>
          </div>

          <div *ngIf="filteredTechnicalSections.length > 0; else technicalEmpty">
            <div
              class="mb-6 border-b border-gray-200 pb-4 last:border-b-0 last:pb-0"
              *ngFor="let section of filteredTechnicalSections"
            >
              <h3 class="text-lg font-semibold text-gray-900 dark:text-surface-50">
                {{ section.title }}
              </h3>
              <p class="text-sm text-gray-600 dark:text-surface-300 mb-2">{{ section.summary }}</p>

              <div class="mb-3" *ngIf="section.businessLogic.length > 0">
                <div class="text-xs font-semibold uppercase tracking-wide text-gray-500 mb-1">
                  Business logic
                </div>
                <ul class="list-disc pl-5 text-sm text-gray-700 dark:text-surface-200 space-y-1">
                  <li *ngFor="let item of section.businessLogic">{{ item }}</li>
                </ul>
              </div>

              <div class="mb-3" *ngIf="section.database.length > 0">
                <div class="text-xs font-semibold uppercase tracking-wide text-gray-500 mb-1">
                  Database
                </div>
                <ul class="list-disc pl-5 text-sm text-gray-700 dark:text-surface-200 space-y-1">
                  <li *ngFor="let item of section.database">{{ item }}</li>
                </ul>
              </div>

              <div class="mb-3" *ngIf="section.api.length > 0">
                <div class="text-xs font-semibold uppercase tracking-wide text-gray-500 mb-1">
                  API
                </div>
                <ul class="list-disc pl-5 text-sm text-gray-700 dark:text-surface-200 space-y-1">
                  <li *ngFor="let item of section.api">{{ item }}</li>
                </ul>
              </div>

              <div *ngIf="section.notes.length > 0">
                <div class="text-xs font-semibold uppercase tracking-wide text-gray-500 mb-1">
                  Notes
                </div>
                <ul class="list-disc pl-5 text-sm text-gray-700 dark:text-surface-200 space-y-1">
                  <li *ngFor="let item of section.notes">{{ item }}</li>
                </ul>
              </div>
            </div>
          </div>

          <ng-template #technicalEmpty>
            <div class="text-sm text-gray-500">No matching technical entries.</div>
          </ng-template>
        </div>
      </div>
    </div>
  `,
  standalone: true,
})
export class StartPage {
  private readonly auth = inject(AuthService);

  manualSearch = '';
  manualSections = MANUAL_SECTIONS;
  technicalSearch = '';
  technicalSections = TECHNICAL_SECTIONS;
  technicalDiagrams = TECHNICAL_DIAGRAMS;

  isSuperAdmin = computed(() => this.auth.hasAnyRole(['SuperAdmin']));

  get filteredManualSections(): ManualSection[] {
    const term = this.manualSearch.trim().toLowerCase();
    if (!term) {
      return this.manualSections;
    }
    return this.manualSections
      .map((section) => {
        const matchesHeader =
          section.title.toLowerCase().includes(term) ||
          section.summary.toLowerCase().includes(term);
        const functions = section.functions.filter((item) => item.toLowerCase().includes(term));
        const options = section.options.filter((item) => item.toLowerCase().includes(term));

        if (!matchesHeader && functions.length === 0 && options.length === 0) {
          return null;
        }

        const resolvedFunctions =
          functions.length > 0 ? functions : matchesHeader ? section.functions : [];
        const resolvedOptions = options.length > 0 ? options : matchesHeader ? section.options : [];

        return {
          ...section,
          functions: resolvedFunctions,
          options: resolvedOptions,
        } as ManualSection;
      })
      .filter((section): section is ManualSection => section !== null);
  }

  get filteredTechnicalSections(): TechnicalSection[] {
    const term = this.technicalSearch.trim().toLowerCase();
    if (!term) {
      return this.technicalSections;
    }
    return this.technicalSections
      .map((section) => {
        const matchesHeader =
          section.title.toLowerCase().includes(term) ||
          section.summary.toLowerCase().includes(term);
        const businessLogic = section.businessLogic.filter((item) =>
          item.toLowerCase().includes(term),
        );
        const database = section.database.filter((item) => item.toLowerCase().includes(term));
        const api = section.api.filter((item) => item.toLowerCase().includes(term));
        const notes = section.notes.filter((item) => item.toLowerCase().includes(term));

        if (
          !matchesHeader &&
          businessLogic.length === 0 &&
          database.length === 0 &&
          api.length === 0 &&
          notes.length === 0
        ) {
          return null;
        }

        return {
          ...section,
          businessLogic:
            businessLogic.length > 0 ? businessLogic : matchesHeader ? section.businessLogic : [],
          database: database.length > 0 ? database : matchesHeader ? section.database : [],
          api: api.length > 0 ? api : matchesHeader ? section.api : [],
          notes: notes.length > 0 ? notes : matchesHeader ? section.notes : [],
        } as TechnicalSection;
      })
      .filter((section): section is TechnicalSection => section !== null);
  }

  get filteredTechnicalDiagrams(): TechnicalDiagram[] {
    const term = this.technicalSearch.trim().toLowerCase();
    if (!term) {
      return this.technicalDiagrams;
    }
    return this.technicalDiagrams.filter((diagram) => {
      const haystack = [diagram.title, ...diagram.keywords].join(' ').toLowerCase();
      return haystack.includes(term);
    });
  }
}
