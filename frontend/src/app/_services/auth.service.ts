import { HttpClient } from '@angular/common/http';
import { inject, Injectable, signal } from '@angular/core';
import { Router } from '@angular/router';
import { environment } from '@environments/environment';
import type { CurrentUser, LoginRequest, LoginResponse } from '@models';
import { lastValueFrom } from 'rxjs';
import { UsersApiService } from './users-api.service';

const TOKEN_KEY = 'tp_token';
const EXPIRES_KEY = 'tp_token_expires';

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly usersApi = inject(UsersApiService);
  private readonly router = inject(Router);
  private readonly baseUrl = `${environment.apiBaseUrl}/api/auth`;

  currentUser = signal<CurrentUser | null>(null);

  constructor() {
    this.restoreSession();
  }

  private restoreSession(): void {
    const token = this.getToken();
    const expires = this.getExpiry();
    if (!token || !expires || Date.now() > expires) {
      this.logout(false);
      return;
    }
    this.refreshCurrentUser();
  }

  private getExpiry(): number | null {
    const raw = localStorage.getItem(EXPIRES_KEY);
    if (!raw) return null;
    const parsed = Number(raw);
    return Number.isFinite(parsed) ? parsed : null;
  }

  getToken(): string | null {
    return localStorage.getItem(TOKEN_KEY);
  }

  isAuthenticated(): boolean {
    const token = this.getToken();
    const expires = this.getExpiry();
    return !!token && !!expires && Date.now() < expires;
  }

  hasAnyRole(roles: string[]): boolean {
    const user = this.currentUser();
    if (!user) return false;
    return roles.some((r) => user.roles.includes(r));
  }

  async login(request: LoginRequest): Promise<void> {
    const response = await lastValueFrom(
      this.http.post<LoginResponse>(`${this.baseUrl}/login`, request),
    );
    if (!response) throw new Error('Login failed');
    const expires = new Date(response.expiresAtUtc).getTime();
    localStorage.setItem(TOKEN_KEY, response.token);
    localStorage.setItem(EXPIRES_KEY, expires.toString());
    await this.refreshCurrentUser();
  }

  async refreshCurrentUser(): Promise<void> {
    try {
      const user = await lastValueFrom(this.usersApi.getMe());
      if (user) {
        this.currentUser.set({
          id: user.id,
          email: user.email,
          displayName: user.displayName,
          roles: user.roles,
          driverToolId: user.driverToolId,
          driverOwnerId: user.driverOwnerId,
          ownerId: user.ownerId,
        });
      }
    } catch {
      this.logout(false);
    }
  }

  logout(navigateToLogin = true): void {
    localStorage.removeItem(TOKEN_KEY);
    localStorage.removeItem(EXPIRES_KEY);
    this.currentUser.set(null);
    if (navigateToLogin) {
      this.router.navigate(['/login']);
    }
  }
}
