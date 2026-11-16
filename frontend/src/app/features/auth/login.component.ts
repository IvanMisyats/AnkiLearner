import { Component, computed, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { AuthService } from '../../core/auth.service';
import { NotifyService } from '../../core/notify.service';
import { safeReturnUrl } from '../../core/safe-return-url';

@Component({
  selector: 'app-login',
  imports: [
    ReactiveFormsModule, RouterLink, MatCardModule, MatFormFieldModule,
    MatInputModule, MatButtonModule, MatProgressSpinnerModule,
  ],
  template: `
    <div class="auth-page">
      <mat-card appearance="outlined">
        <mat-card-header>
          <mat-card-title>Sign in to AnkiLearner</mat-card-title>
        </mat-card-header>
        <mat-card-content>
          <form [formGroup]="form" (ngSubmit)="submit()">
            <mat-form-field appearance="outline">
              <mat-label>Email</mat-label>
              <input matInput type="email" formControlName="email" autocomplete="email" />
            </mat-form-field>
            <mat-form-field appearance="outline">
              <mat-label>Password</mat-label>
              <input matInput type="password" formControlName="password" autocomplete="current-password" />
            </mat-form-field>
            <button mat-flat-button type="submit" [disabled]="form.invalid || pending()">
              @if (pending()) {
                <mat-spinner diameter="20" />
              } @else {
                Sign in
              }
            </button>
          </form>
        </mat-card-content>
        <mat-card-footer>
          <!-- [queryParams] passes returnUrl on to the other page; a null value is left out. -->
          <span>New here? <a routerLink="/register" [queryParams]="{ returnUrl: returnUrl() }">Create an account</a></span>
        </mat-card-footer>
      </mat-card>
    </div>
  `,
  styleUrl: './auth.scss',
})
export class LoginComponent {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly notify = inject(NotifyService);
  private readonly formBuilder = inject(FormBuilder);

  readonly pending = signal(false);

  readonly form = this.formBuilder.nonNullable.group({
    email: ['', [Validators.required, Validators.email]],
    password: ['', Validators.required],
  });

  /**
   * The page to go to after signing in (`?returnUrl=`, set by the auth guard or an expired
   * session), or null for home. Only a safe in-app path is used — see safeReturnUrl.
   * toSignal turns the router's stream of query parameters into a signal, so the value
   * stays current even when only the query string changes on this same page.
   */
  private readonly queryParams = toSignal(this.route.queryParamMap, { requireSync: true });
  readonly returnUrl = computed(() => safeReturnUrl(this.queryParams().get('returnUrl')));

  async submit(): Promise<void> {
    if (this.form.invalid) return;
    this.pending.set(true);
    try {
      const { email, password } = this.form.getRawValue();
      await this.auth.login(email, password);
      // navigateByUrl takes a whole URL string, query included ("/translate?q=hund").
      // replaceUrl swaps this page's history entry for the next one, so the phone's Back
      // button doesn't return to the form after signing in.
      this.router.navigateByUrl(this.returnUrl() ?? '/', { replaceUrl: true });
    } catch (error) {
      this.notify.httpError(error, 'Sign-in failed.');
    } finally {
      this.pending.set(false);
    }
  }
}
