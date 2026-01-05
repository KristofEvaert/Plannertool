import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  OnDestroy,
  OnInit,
  signal,
} from '@angular/core';

import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { MANUAL_SECTIONS, type ManualSection } from '@app/_data/manual.data';
import {
  TECHNICAL_DIAGRAMS,
  TECHNICAL_SECTIONS,
  type TechnicalDiagram,
  type TechnicalSection,
} from '@app/_data/technical-manual.data';
import { HelpManualComponent } from '@components/help-manual/help-manual.component';
import { AuthService } from '@services/auth.service';
import { ButtonModule } from 'primeng/button';
import { CardModule } from 'primeng/card';
import { InputTextModule } from 'primeng/inputtext';

interface MapMarker {
  top: string;
  left: string;
  color: string;
  ringColor: string;
}

@Component({
  selector: 'app-start',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    FormsModule,
    CardModule,
    ButtonModule,
    InputTextModule,
    RouterLink,
    HelpManualComponent,
  ],
  templateUrl: './start.page.html',
  standalone: true,
})
export class StartPage implements OnInit, OnDestroy {
  private readonly auth = inject(AuthService);

  activePingIndex = signal<number>(0);
  private pingInterval: any;

  readonly markers: MapMarker[] = [
    { top: '20%', left: '25%', color: 'bg-red-500', ringColor: 'ring-red-500/30' },
    { top: '35%', left: '45%', color: 'bg-blue-500', ringColor: 'ring-blue-500/30' },
    { top: '50%', left: '30%', color: 'bg-green-500', ringColor: 'ring-green-500/30' },
    { top: '60%', left: '55%', color: 'bg-yellow-500', ringColor: 'ring-yellow-500/30' },
    { top: '25%', left: '65%', color: 'bg-purple-500', ringColor: 'ring-purple-500/30' },
    { top: '70%', left: '40%', color: 'bg-orange-500', ringColor: 'ring-orange-500/30' },
    { top: '45%', left: '70%', color: 'bg-pink-500', ringColor: 'ring-pink-500/30' },
    { top: '75%', left: '60%', color: 'bg-teal-500', ringColor: 'ring-teal-500/30' },
  ];

  ngOnInit() {
    this.pingInterval = setInterval(() => {
      const randomIndex = Math.floor(Math.random() * this.markers.length);
      this.activePingIndex.set(randomIndex);
    }, 1000);
  }

  ngOnDestroy() {
    if (this.pingInterval) {
      clearInterval(this.pingInterval);
    }
  }

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
