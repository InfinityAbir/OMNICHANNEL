import { HttpClient } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import {
  AuthTokenResponse,
  CurrentUserResponse,
  LoginRequest,
  RegisterRequest,
} from '../models/auth.models';

const ACCESS_TOKEN_KEY = 'omnichannel.accessToken';
const REFRESH_TOKEN_KEY = 'omnichannel.refreshToken';

/**
 * Tokens live in localStorage — the API is bearer-token based, not cookie-based, so this is the
 * pragmatic choice for a SPA talking to it. Mitigation: Angular auto-escapes template
 * interpolation by default (no [innerHTML] is used anywhere message/user content is rendered),
 * so there is no first-party XSS vector to steal it from; backend CSP/security headers add
 * defense in depth. Documented trade-off, not an oversight — see Phase 3 report.
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly currentUserSignal = signal<CurrentUserResponse | null>(null);

  readonly currentUser = this.currentUserSignal.asReadonly();
  readonly isAuthenticated = computed(() => this.currentUserSignal() !== null);

  get accessToken(): string | null {
    return localStorage.getItem(ACCESS_TOKEN_KEY);
  }

  get refreshToken(): string | null {
    return localStorage.getItem(REFRESH_TOKEN_KEY);
  }

  async register(request: RegisterRequest): Promise<void> {
    const tokens = await firstValueFrom(
      this.http.post<AuthTokenResponse>('/api/v1/auth/register', request),
    );
    this.storeTokens(tokens);
    await this.loadCurrentUser();
  }

  async login(request: LoginRequest): Promise<void> {
    const tokens = await firstValueFrom(this.http.post<AuthTokenResponse>('/api/v1/auth/login', request));
    this.storeTokens(tokens);
    await this.loadCurrentUser();
  }

  async logout(): Promise<void> {
    const refreshToken = this.refreshToken;
    this.clearTokens();
    this.currentUserSignal.set(null);
    if (refreshToken) {
      try {
        await firstValueFrom(this.http.post('/api/v1/auth/logout', { refreshToken }));
      } catch {
        // Best-effort — the local session is already cleared either way.
      }
    }
  }

  async restoreSession(): Promise<void> {
    if (!this.accessToken) {
      return;
    }

    try {
      await this.loadCurrentUser();
    } catch {
      this.clearTokens();
      this.currentUserSignal.set(null);
    }
  }

  async refreshAccessToken(): Promise<boolean> {
    const refreshToken = this.refreshToken;
    if (!refreshToken) {
      return false;
    }

    try {
      const tokens = await firstValueFrom(
        this.http.post<AuthTokenResponse>('/api/v1/auth/refresh', { refreshToken }),
      );
      this.storeTokens(tokens);
      return true;
    } catch {
      this.clearTokens();
      this.currentUserSignal.set(null);
      return false;
    }
  }

  private async loadCurrentUser(): Promise<void> {
    const user = await firstValueFrom(this.http.get<CurrentUserResponse>('/api/v1/users/me'));
    this.currentUserSignal.set(user);
  }

  private storeTokens(tokens: AuthTokenResponse): void {
    localStorage.setItem(ACCESS_TOKEN_KEY, tokens.accessToken);
    localStorage.setItem(REFRESH_TOKEN_KEY, tokens.refreshToken);
  }

  private clearTokens(): void {
    localStorage.removeItem(ACCESS_TOKEN_KEY);
    localStorage.removeItem(REFRESH_TOKEN_KEY);
  }
}
