import { CommonModule } from '@angular/common';
import { Component, Input } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { DialogModule } from 'primeng/dialog';
import { InputTextModule } from 'primeng/inputtext';
import { ButtonModule } from 'primeng/button';
import { MANUAL_SECTIONS, type ManualSection } from '@app/_data/manual.data';

@Component({
  selector: 'app-help-manual',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink, DialogModule, InputTextModule, ButtonModule],
  template: `
    <p-button
      type="button"
      icon="pi pi-info-circle"
      [text]="true"
      [rounded]="true"
      size="small"
      severity="secondary"
      ariaLabel="Open manual"
      (onClick)="open()"
    />

    <p-dialog
      [(visible)]="visible"
      [modal]="true"
      [style]="{ width: '720px' }"
      [appendTo]="'body'"
      [baseZIndex]="10000"
      [header]="dialogTitle"
      (onHide)="searchTerm = ''"
    >
      <div class="mb-4">
        <div class="flex items-center gap-2">
          <i class="pi pi-search text-gray-500"></i>
          <input
            pInputText
            [(ngModel)]="searchTerm"
            placeholder="Search this manual..."
            class="w-full"
          />
        </div>
      </div>

      <div *ngIf="filteredSections.length > 0; else emptyState">
        <div *ngFor="let section of filteredSections" class="mb-6 border-b border-gray-200 pb-4 last:border-b-0 last:pb-0">
          <div class="flex items-center justify-between gap-2">
            <h3 class="text-lg font-semibold text-gray-900">{{ section.title }}</h3>
            <a
              *ngIf="showLinks && section.route"
              [routerLink]="section.route"
              class="text-sm text-blue-600 hover:underline"
              (click)="visible = false"
            >
              Open tab
            </a>
          </div>
          <p class="text-sm text-gray-600 mb-2">{{ section.summary }}</p>

          <div *ngIf="section.functions.length > 0" class="mb-3">
            <div class="text-xs font-semibold uppercase tracking-wide text-gray-500 mb-1">Functions</div>
            <ul class="list-disc pl-5 text-sm text-gray-700 space-y-1">
              <li *ngFor="let item of section.functions">{{ item }}</li>
            </ul>
          </div>

          <div *ngIf="section.options.length > 0">
            <div class="text-xs font-semibold uppercase tracking-wide text-gray-500 mb-1">Options</div>
            <ul class="list-disc pl-5 text-sm text-gray-700 space-y-1">
              <li *ngFor="let item of section.options">{{ item }}</li>
            </ul>
          </div>
        </div>
      </div>

      <ng-template #emptyState>
        <div class="text-sm text-gray-500">No matching manual entries.</div>
      </ng-template>
    </p-dialog>
  `,
})
export class HelpManualComponent {
  @Input() sectionId?: string;
  @Input() sections?: ManualSection[];
  @Input() title?: string;
  @Input() showLinks = false;

  visible = false;
  searchTerm = '';

  open(): void {
    this.visible = true;
  }

  get dialogTitle(): string {
    if (this.title) {
      return this.title;
    }
    const sections = this.resolvedSections;
    if (sections.length === 1) {
      return `${sections[0].title} Manual`;
    }
    return 'Planner Tool Manual';
  }

  get resolvedSections(): ManualSection[] {
    if (this.sections && this.sections.length > 0) {
      return this.sections;
    }
    if (this.sectionId) {
      return MANUAL_SECTIONS.filter((section) => section.id === this.sectionId);
    }
    return MANUAL_SECTIONS;
  }

  get filteredSections(): ManualSection[] {
    const term = this.searchTerm.trim().toLowerCase();
    if (!term) {
      return this.resolvedSections;
    }

    return this.resolvedSections
      .map((section) => {
        const matchesHeader =
          section.title.toLowerCase().includes(term) ||
          section.summary.toLowerCase().includes(term);
        const functions = this.filterItems(section.functions, term);
        const options = this.filterItems(section.options, term);

        if (!matchesHeader && functions.length === 0 && options.length === 0) {
          return null;
        }

        const resolvedFunctions = functions.length > 0 ? functions : matchesHeader ? section.functions : [];
        const resolvedOptions = options.length > 0 ? options : matchesHeader ? section.options : [];

        return {
          ...section,
          functions: resolvedFunctions,
          options: resolvedOptions,
        } as ManualSection;
      })
      .filter((section): section is ManualSection => section !== null);
  }

  private filterItems(items: string[], term: string): string[] {
    return items.filter((item) => item.toLowerCase().includes(term));
  }
}
