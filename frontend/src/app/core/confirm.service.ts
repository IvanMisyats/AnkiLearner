import { Component, Injectable, inject } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialog, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { firstValueFrom } from 'rxjs';

export interface ConfirmData {
  title: string;
  message: string;
  /** Label of the confirming button. */
  confirmText?: string;
  /** Paints the confirming button in the error colour for destructive actions. */
  destructive?: boolean;
}

/**
 * The yes/no dialog body. Components do not reference it directly — they call
 * {@link ConfirmService.ask} instead.
 *
 * For a C# reader: `MAT_DIALOG_DATA` is an injection token carrying whatever was
 * passed to `dialog.open(..., { data })`, and `MatDialogRef` is the handle used to
 * close the dialog and hand a result back to the caller.
 */
@Component({
  selector: 'app-confirm-dialog',
  imports: [MatDialogModule, MatButtonModule],
  template: `
    <h2 mat-dialog-title>{{ data.title }}</h2>
    <mat-dialog-content>{{ data.message }}</mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button (click)="dialogRef.close(false)">Cancel</button>
      <button
        mat-flat-button
        [class.destructive]="data.destructive"
        (click)="dialogRef.close(true)">
        {{ data.confirmText ?? 'Confirm' }}
      </button>
    </mat-dialog-actions>
  `,
  styles: `
    /* Material styles mat-flat-button through its own tokens, and its rule outranks a
       plain class selector — so recolour by overriding the tokens, not the background. */
    .destructive {
      --mat-button-filled-container-color: var(--mat-sys-error);
      --mat-button-filled-label-text-color: var(--mat-sys-on-error);
      --mat-button-filled-state-layer-color: var(--mat-sys-on-error);
      --mat-button-filled-ripple-color: color-mix(in srgb, var(--mat-sys-on-error) 20%, transparent);
    }
  `,
})
export class ConfirmDialogComponent {
  readonly dialogRef = inject(MatDialogRef<ConfirmDialogComponent, boolean>);
  readonly data = inject<ConfirmData>(MAT_DIALOG_DATA);
}

/** Material confirmation dialog, the counterpart of NotifyService for yes/no questions. */
@Injectable({ providedIn: 'root' })
export class ConfirmService {
  private readonly dialog = inject(MatDialog);

  /** Resolves true only when the user pressed the confirming button. */
  async ask(data: ConfirmData): Promise<boolean> {
    const ref = this.dialog.open(ConfirmDialogComponent, { data, width: '360px' });
    return (await firstValueFrom(ref.afterClosed())) === true;
  }
}
