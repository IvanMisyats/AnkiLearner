import { Component, inject } from '@angular/core';
import { MatCardModule } from '@angular/material/card';
import { AuthService } from '../../core/auth.service';

/** Placeholder landing page — replaced by the study dashboard in Phase 7. */
@Component({
  selector: 'app-dashboard',
  imports: [MatCardModule],
  template: `
    <div class="page">
      <mat-card appearance="outlined">
        <mat-card-content>
          <h1>Welcome{{ userEmail() ? ', ' + userEmail() : '' }}!</h1>
          <p>Your study dashboard will appear here.</p>
        </mat-card-content>
      </mat-card>
    </div>
  `,
  styles: `
    .page { padding: 16px; max-width: 800px; margin: 0 auto; }
  `,
})
export class DashboardComponent {
  private readonly auth = inject(AuthService);
  readonly userEmail = () => this.auth.user()?.email ?? '';
}
