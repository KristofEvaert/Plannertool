import { Component, inject, signal } from '@angular/core';
import { email, FormField, form, required } from '@angular/forms/signals';

import { Router } from '@angular/router';
import { AuthService } from '@services';
import { MessageService } from 'primeng/api';
import { ButtonModule } from 'primeng/button';
import { InputTextModule } from 'primeng/inputtext';
import { PasswordModule } from 'primeng/password';
import { ToastModule } from 'primeng/toast';

@Component({
  selector: 'app-login-page',
  imports: [FormField, InputTextModule, PasswordModule, ButtonModule, ToastModule],
  providers: [MessageService],
  templateUrl: './login.page.html',
  styleUrls: ['./login.page.css'],
})
export class LoginPage {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly messageService = inject(MessageService);

  protected loginModel = signal({ email: '', password: '' });

  public loginForm = form(this.loginModel, (schemaPath) => {
    required(schemaPath.email, { message: 'Email is required' });
    email(schemaPath.email, { message: 'Enter a valid email address' });
    required(schemaPath.password, { message: 'Password is required' });
  });
  public loading = signal(false);

  async login(event: Event) {
    event.preventDefault();
    this.loading.set(true);

    const { email, password } = this.loginModel();

    if (this.loginForm.password().invalid() || this.loginForm.email().invalid()) {
      this.loginForm.password().markAsTouched();
      this.loginForm.email().markAsTouched();
      this.loading.set(false);
      return;
    }

    try {
      await this.auth.login({ email, password });

      this.messageService.add({ severity: 'success', summary: 'Logged in' });

      const roles = this.auth.currentUser()?.roles ?? [];
      const isDriverOnly =
        roles.includes('Driver') &&
        !roles.some((r) => r === 'SuperAdmin' || r === 'Admin' || r === 'Planner');

      this.router.navigate([isDriverOnly ? '/driver' : '/start']);
    } catch (err: any) {
      this.messageService.add({
        severity: 'error',
        summary: 'Login failed',
        detail: err?.error?.message || err?.message || 'Invalid credentials',
        life: 5000,
      });
    } finally {
      this.loading.set(false);
    }
  }
}
