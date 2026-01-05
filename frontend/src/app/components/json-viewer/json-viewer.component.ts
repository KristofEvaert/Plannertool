import { Component, input } from '@angular/core';
import { ButtonModule } from 'primeng/button';
import { TooltipModule } from 'primeng/tooltip';

@Component({
  selector: 'app-json-viewer',
  standalone: true,
  imports: [ButtonModule, TooltipModule],
  templateUrl: './json-viewer.component.html',
  styleUrl: './json-viewer.component.css',
})
export class JsonViewerComponent {
  readonly value = input<unknown>();
  readonly emptyMessage = input('No data');

  get isEmpty(): boolean {
    const value = this.value();
    if (value == null) {
      return true;
    }
    if (typeof value === 'string') {
      return value.trim().length === 0;
    }
    return false;
  }

  get displayValue(): string {
    const value = this.value();
    if (value == null) {
      return '';
    }

    if (typeof value === 'string') {
      const trimmed = value.trim();
      if (!trimmed) {
        return '';
      }
      const parsed = this.tryParseJson(trimmed);
      return parsed != null ? JSON.stringify(parsed, null, 2) : trimmed;
    }

    return JSON.stringify(value, null, 2);
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
