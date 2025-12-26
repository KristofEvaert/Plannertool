import { Component, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ButtonModule } from 'primeng/button';
import { TableModule } from 'primeng/table';
import { InputTextModule } from 'primeng/inputtext';
import { DialogModule } from 'primeng/dialog';
import { ToastModule } from 'primeng/toast';
import { MessageService } from 'primeng/api';
import { ServiceTypesApiService } from '@services/service-types-api.service';
import type {
  ServiceTypeDto,
  CreateServiceTypeRequest,
} from '@models/service-type.model';
import { catchError, of } from 'rxjs';

@Component({
  selector: 'app-service-types',
  imports: [
    CommonModule,
    FormsModule,
    ButtonModule,
    TableModule,
    InputTextModule,
    DialogModule,
    ToastModule,
  ],
  providers: [MessageService],
  templateUrl: './service-types.page.html',
  standalone: true,
})
export class ServiceTypesPage {
  private readonly api = inject(ServiceTypesApiService);
  private readonly messageService = inject(MessageService);

  // Data
  items = signal<ServiceTypeDto[]>([]);
  loading = signal(false);

  // Dialog state
  showDialog = signal(false);

  // Computed for two-way binding
  get showDialogValue(): boolean {
    return this.showDialog();
  }
  set showDialogValue(value: boolean) {
    this.showDialog.set(value);
  }

  form = signal<CreateServiceTypeRequest>({
    code: '',
    name: '',
    description: '',
  });

  constructor() {
    this.loadData();
  }

  loadData(): void {
    this.loading.set(true);
    this.api
      .getAll(false)
      .pipe(
        catchError((err) => {
          this.loading.set(false);
          this.messageService.add({
            severity: 'error',
            summary: 'Error',
            detail: err.detail || err.message || 'Failed to load service types',
          });
          return of([]);
        })
      )
      .subscribe((types) => {
        this.loading.set(false);
        this.items.set(types);
      });
  }

  openAddDialog(): void {
    this.form.set({
      code: '',
      name: '',
      description: '',
    });
    this.showDialog.set(true);
  }

  save(): void {
    const form = this.form();

    if (!form.code.trim()) {
      this.messageService.add({
        severity: 'error',
        summary: 'Validation Error',
        detail: 'Code is required',
      });
      return;
    }

    if (!form.name.trim()) {
      this.messageService.add({
        severity: 'error',
        summary: 'Validation Error',
        detail: 'Name is required',
      });
      return;
    }

    // Validate code format (uppercase, alphanumeric, underscores)
    const codeRegex = /^[A-Z0-9_]+$/;
    if (!codeRegex.test(form.code)) {
      this.messageService.add({
        severity: 'error',
        summary: 'Validation Error',
        detail: 'Code must be uppercase, alphanumeric, and can contain underscores',
      });
      return;
    }

    this.loading.set(true);
    const request: CreateServiceTypeRequest = {
      code: form.code.trim().toUpperCase(),
      name: form.name.trim(),
      description: form.description?.trim() || undefined,
    };

    this.api
      .create(request)
      .pipe(
        catchError((err) => {
          this.loading.set(false);
          this.messageService.add({
            severity: 'error',
            summary: 'Error',
            detail: err.detail || err.message || 'Failed to create service type',
          });
          return of(null);
        })
      )
      .subscribe((result) => {
        this.loading.set(false);
        if (result) {
          this.showDialog.set(false);
          this.messageService.add({
            severity: 'success',
            summary: 'Success',
            detail: 'Service type created',
          });
          this.loadData();
        }
      });
  }
}

