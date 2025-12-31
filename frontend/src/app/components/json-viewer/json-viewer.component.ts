import { CommonModule } from '@angular/common';
import { Component, Input } from '@angular/core';
import { ButtonModule } from 'primeng/button';
import { TooltipModule } from 'primeng/tooltip';

@Component({
  selector: 'app-json-viewer',
  standalone: true,
  imports: [CommonModule, ButtonModule, TooltipModule],
  templateUrl: './json-viewer.component.html',
  styleUrl: './json-viewer.component.css',
})
export class JsonViewerComponent {
  @Input() value: unknown;
  @Input() emptyMessage = 'No data';

  get isEmpty(): boolean {
    if (this.value == null) {
      return true;
    }
    if (typeof this.value === 'string') {
      return this.value.trim().length === 0;
    }
    return false;
  }

  get displayValue(): string {
    if (this.value == null) {
      return '';
    }

    if (typeof this.value === 'string') {
      const trimmed = this.value.trim();
      if (!trimmed) {
        return '';
      }
      const parsed = this.tryParseJson(trimmed);
      return parsed != null ? JSON.stringify(parsed, null, 2) : trimmed;
    }

    return JSON.stringify(this.value, null, 2);
  }

  copyToClipboard(): void {
    const text = this.displayValue;
    if (!text) {
      return;
    }
    if (navigator?.clipboard?.writeText) {
      navigator.clipboard.writeText(text);
      return;
    }
  }

  private tryParseJson(value: string): unknown | null {
    const startsJson = value.startsWith('{') || value.startsWith('[');
    if (!startsJson) {
      return null;
    }
    try {
      return JSON.parse(value);
    } catch {
      return null;
    }
  }
}
