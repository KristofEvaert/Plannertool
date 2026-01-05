import { Component, inject, signal } from '@angular/core';
import { ButtonModule } from 'primeng/button';
import { InputNumberModule } from 'primeng/inputnumber';
import { MessageModule } from 'primeng/message';
import { CardModule } from 'primeng/card';
import { MessageService } from 'primeng/api';
import { FormsModule } from '@angular/forms';
import { catchError, of } from 'rxjs';
import { ImportApiService } from '@services/import-api.service';
import type { ImportPolesResultDto } from '@models/import.model';

@Component({
  selector: 'app-import',
  imports: [ButtonModule, InputNumberModule, MessageModule, CardModule, FormsModule],
  providers: [MessageService],
  templateUrl: './import.page.html',
  styleUrl: './import.page.css',
  standalone: true,
})
export class ImportPage {
  private readonly importApi = inject(ImportApiService);
  private readonly messageService = inject(MessageService);

  days = 14;
  loading = signal(false);
  lastResult = signal<ImportPolesResultDto | null>(null);
  error = signal<string | null>(null);

  importPoles(): void {
    this.loading.set(true);
    this.error.set(null);

    this.importApi
      .importPoles(this.days)
      .pipe(
        catchError((err) => {
          this.error.set(err.title || err.message || 'Failed to import poles');
          this.messageService.add({
            severity: 'error',
            summary: 'Import Error',
            detail: err.detail || err.title || err.message || 'Failed to import poles',
          });
          return of(null);
        }),
      )
      .subscribe((result) => {
        this.loading.set(false);
        if (result) {
          this.lastResult.set(result);
          this.messageService.add({
            severity: 'success',
            summary: 'Import Complete',
            detail: `Imported ${result.imported} new poles, updated ${result.updated}`,
          });
        }
      });
  }
}
