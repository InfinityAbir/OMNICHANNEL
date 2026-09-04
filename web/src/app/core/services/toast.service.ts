import { Injectable, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';

export type ToastKind = 'error' | 'success' | 'info';

export interface Toast {
  id: number;
  kind: ToastKind;
  message: string;
}

const AUTO_DISMISS_MS = 5000;

/**
 * App-wide toast feed. Several mutation actions (assign, tag, status/priority change, create
 * conversation) previously subscribed with no error handler at all — a failed request just did
 * nothing visibly, which is worse than a raw error message. This gives every one of those a
 * single, consistent, human-readable failure surface instead of silence or a leaked
 * "Http failure response for ..." string.
 */
@Injectable({ providedIn: 'root' })
export class ToastService {
  private nextId = 1;
  readonly toasts = signal<Toast[]>([]);

  show(message: string, kind: ToastKind = 'info'): void {
    const id = this.nextId++;
    this.toasts.update((current) => [...current, { id, kind, message }]);
    setTimeout(() => this.dismiss(id), AUTO_DISMISS_MS);
  }

  showError(error: unknown, fallback = 'Something went wrong. Please try again.'): void {
    this.show(extractErrorMessage(error, fallback), 'error');
  }

  dismiss(id: number): void {
    this.toasts.update((current) => current.filter((t) => t.id !== id));
  }
}

/** Reads ASP.NET Core's ProblemDetails shape ({ title, detail }) off a failed request, so the
 * toast shows the same message the API actually intended to communicate — never a raw
 * "Http failure response for http://..." string, a stack trace, or `[object Object]`. */
export function extractErrorMessage(error: unknown, fallback: string): string {
  if (error instanceof HttpErrorResponse) {
    if (error.status === 0) {
      return 'Network error — check your connection and try again.';
    }

    const body = error.error as { title?: string; detail?: string } | null;
    const fromBody = body?.detail || body?.title;
    if (typeof fromBody === 'string' && fromBody.trim().length > 0) {
      return fromBody;
    }

    if (error.status === 429) {
      return 'Too many requests — please slow down and try again shortly.';
    }
  }

  return fallback;
}
