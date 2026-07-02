import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { Router, RouterLink } from '@angular/router';
import { AuthService } from '../../core/auth.service';
import { NotifyService } from '../../core/notify.service';

@Component({
  selector: 'app-register',
  imports: [
    ReactiveFormsModule, RouterLink, MatCardModule, MatFormFieldModule,
    MatInputModule, MatButtonModule, MatProgressSpinnerModule,
  ],
  template: `
    <div class="auth-page">
      <mat-card appearance="outlined">
        <mat-card-header>
          <mat-card-title>Create your account</mat-card-title>
        </mat-card-header>
        <mat-card-content>
          <form [formGroup]="form" (ngSubmit)="submit()">
            <mat-form-field appearance="outline">
              <mat-label>Email</mat-label>
              <input matInput type="email" formControlName="email" autocomplete="email" />
            </mat-form-field>
            <mat-form-field appearance="outline">
              <mat-label>Password</mat-label>
              <input matInput type="password" formControlName="password" autocomplete="new-password" />
              @if (form.controls.password.hasError('minlength')) {
                <mat-error>At least 8 characters.</mat-error>
              }
            </mat-form-field>
            <button mat-flat-button type="submit" [disabled]="form.invalid || pending()">
              @if (pending()) {
                <mat-spinner diameter="20" />
              } @else {
                Create account
              }
            </button>
          </form>
        </mat-card-content>
        <mat-card-footer>
          <span>Already registered? <a routerLink="/login">Sign in</a></span>
        </mat-card-footer>
      </mat-card>
    </div>
  `,
  styleUrl: './auth.scss',
})
export class RegisterComponent {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly notify = inject(NotifyService);
  private readonly formBuilder = inject(FormBuilder);

  readonly pending = signal(false);

  readonly form = this.formBuilder.nonNullable.group({
    email: ['', [Validators.required, Validators.email]],
    password: ['', [Validators.required, Validators.minLength(8)]],
  });

  async submit(): Promise<void> {
    if (this.form.invalid) return;
    this.pending.set(true);
    try {
      const { email, password } = this.form.getRawValue();
      await this.auth.register(email, password);
      this.router.navigate(['/']);
    } catch (error) {
      this.notify.httpError(error, 'Registration failed.');
    } finally {
      this.pending.set(false);
    }
  }
}
