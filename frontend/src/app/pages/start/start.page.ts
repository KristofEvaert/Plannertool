import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';
import { CardModule } from 'primeng/card';
import { ButtonModule } from 'primeng/button';

@Component({
  selector: 'app-start',
  imports: [CommonModule, CardModule, ButtonModule, RouterLink],
  template: `
    <div class="min-h-screen bg-gradient-to-br from-gray-50 to-gray-100 dark:from-surface-900 dark:to-surface-800 py-12 px-4">
      <div class="max-w-6xl mx-auto">
        <!-- Hero Section -->
        <div class="bg-white dark:bg-surface-900 rounded-2xl shadow-sm border border-gray-200 dark:border-surface-700 p-8 lg:p-12 mb-8">
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
                <span class="text-2xl font-semibold text-gray-900 dark:text-surface-50" style="display: none;">Trescal</span>
              </div>

              <!-- Title & Subtitle -->
              <div>
                <h1 class="text-4xl lg:text-5xl font-bold text-gray-900 dark:text-surface-50 mb-3">
                  Service Planning Tool
                </h1>
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
                  [outlined]="true"
                  routerLink="/drivers"
                />
                <p-button
                  label="Service Location"
                  icon="pi pi-map-marker"
                  [outlined]="true"
                  routerLink="/service-locations"
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
                  <div class="absolute top-0 left-0 w-full h-full bg-red-500 rounded-full animate-ping opacity-75"></div>
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
                  <div class="flex items-center gap-2 text-xs font-medium text-gray-700 dark:text-surface-200">
                    <i class="pi pi-map-marker text-blue-600 dark:text-blue-400"></i>
                    <span>Service Area</span>
                  </div>
                </div>

              </div>
            </div>
          </div>
        </div>
      </div>
    </div>
  `,
  standalone: true,
})
export class StartPage {}
