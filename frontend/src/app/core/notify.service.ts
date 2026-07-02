import { HttpErrorResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { MatSnackBar } from '@angular/material/snack-bar';

/** Small wrapper around Material's snackbar for toasts and API error messages. */
@Injectable({ providedIn: 'root' })
export class NotifyService {
  private readonly snackBar = inject(MatSnackBar);

  success(message: string): void {
    this.snackBar.open(message, undefined, { duration: 2500 });
  }

  error(message: string): void {
    this.snackBar.open(message, 'Dismiss', { duration: 6000 });
  }

  /** Extracts a readable message from an RFC-7807 problem response. */
  httpError(error: unknown, fallback = 'Something went wrong.'): void {
    this.error(NotifyService.messageFor(error, fallback));
  }

  static messageFor(error: unknown, fallback = 'Something went wrong.'): string {
    if (error instanceof HttpErrorResponse) {
      const problem = error.error as
        | { title?: string; detail?: string; errors?: Record<string, string[]> }
        | null;
      if (problem?.errors) {
        const first = Object.values(problem.errors).flat()[0];
        if (first) return first;
      }
      if (problem?.detail) return problem.detail;
      if (problem?.title) return problem.title;
      if (error.status === 0) return 'Cannot reach the server.';
    }
    return fallback;
  }
}
