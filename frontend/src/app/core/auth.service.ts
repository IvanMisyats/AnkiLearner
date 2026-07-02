import { HttpClient } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { AuthResponse, MeResponse, SettingsDto, UserDto } from './api.types';

/**
 * Holds the authentication state for the whole app.
 *
 * The access token lives only in memory (never in localStorage — safer against XSS).
 * A refresh token lives in an httpOnly cookie the browser sends automatically, so a
 * page reload can silently obtain a fresh access token via initialize().
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);

  // "signal" is Angular's reactive state container — reading it in a template
  // subscribes the view to changes automatically (think INotifyPropertyChanged).
  readonly user = signal<UserDto | null>(null);
  readonly settings = signal<SettingsDto | null>(null);
  readonly isAuthenticated = computed(() => this.user() !== null);

  /** In-memory bearer token; read by the auth interceptor on every request. */
  accessToken: string | null = null;

  /** Single in-flight refresh so parallel 401s don't fire multiple refresh calls. */
  private refreshInFlight: Promise<boolean> | null = null;

  /** Called once at app startup: try to restore the session from the refresh cookie. */
  async initialize(): Promise<void> {
    const refreshed = await this.tryRefresh();
    if (refreshed) {
      await this.loadMe().catch(() => this.clear());
    }
  }

  async login(email: string, password: string): Promise<void> {
    const response = await firstValueFrom(
      this.http.post<AuthResponse>('/api/auth/login', { email, password }),
    );
    this.applyAuth(response);
    await this.loadMe();
  }

  async register(email: string, password: string): Promise<void> {
    const response = await firstValueFrom(
      this.http.post<AuthResponse>('/api/auth/register', { email, password }),
    );
    this.applyAuth(response);
    await this.loadMe();
  }

  async logout(): Promise<void> {
    try {
      await firstValueFrom(this.http.post('/api/auth/logout', null));
    } finally {
      this.clear();
      this.router.navigate(['/login']);
    }
  }

  /**
   * Exchanges the refresh cookie for a new access token. Returns false when the
   * session is gone (no cookie / revoked). Concurrent callers share one attempt.
   */
  tryRefresh(): Promise<boolean> {
    this.refreshInFlight ??= (async () => {
      try {
        const response = await firstValueFrom(
          this.http.post<AuthResponse>('/api/auth/refresh', null),
        );
        this.applyAuth(response);
        return true;
      } catch {
        this.clear();
        return false;
      } finally {
        this.refreshInFlight = null;
      }
    })();
    return this.refreshInFlight;
  }

  /** Drops local auth state and returns to the login page. Used after failed refresh. */
  sessionExpired(): void {
    this.clear();
    this.router.navigate(['/login']);
  }

  refreshSettings(settings: SettingsDto): void {
    this.settings.set(settings);
  }

  private async loadMe(): Promise<void> {
    const me = await firstValueFrom(this.http.get<MeResponse>('/api/auth/me'));
    this.user.set(me.user);
    this.settings.set(me.settings);
  }

  private applyAuth(response: AuthResponse): void {
    this.accessToken = response.accessToken;
    this.user.set(response.user);
  }

  private clear(): void {
    this.accessToken = null;
    this.user.set(null);
    this.settings.set(null);
  }
}
