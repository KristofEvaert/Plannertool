import { Component, inject } from '@angular/core';

import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { AuthService } from '@services/auth.service';
import { MessageService } from 'primeng/api';
import { ButtonModule } from 'primeng/button';
import { InputTextModule } from 'primeng/inputtext';
import { PasswordModule } from 'primeng/password';
import { ToastModule } from 'primeng/toast';

@Component({
  selector: 'app-login-page',
  standalone: true,
  imports: [FormsModule, InputTextModule, PasswordModule, ButtonModule, ToastModule],
  providers: [MessageService],
  templateUrl: './login.page.html',
  styleUrls: ['./login.page.css'],
})
export class LoginPage {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly messageService = inject(MessageService);

  email = '';
  password = '';
  loading = false;

  async login(): Promise<void> {
    if (!this.email || !this.password) {
      this.messageService.add({
        severity: 'warn',
        summary: 'Validation',
        detail: 'Email and password are required',
      });
      return;
    }

    this.loading = true;
    try {
      await this.auth.login({ email: this.email, password: this.password });
      this.messageService.add({ severity: 'success', summary: 'Logged in' });
      const user = this.auth.currentUser();
      const roles = user?.roles ?? [];
      const isDriverOnly =
        roles.includes('Driver') &&
        !roles.some((r) => r === 'SuperAdmin' || r === 'Admin' || r === 'Planner');
      this.router.navigate([isDriverOnly ? '/driver' : '/start']);
    } catch (err: any) {
      this.messageService.add({
        severity: 'error',
        summary: 'Login failed',
        detail: err?.error?.message || err?.message || 'Invalid credentials',
      });
    } finally {
      this.loading = false;
    }
  }
}
