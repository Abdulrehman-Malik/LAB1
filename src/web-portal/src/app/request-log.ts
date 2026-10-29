import { HttpErrorResponse, HttpInterceptorFn, HttpResponse } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { tap } from 'rxjs';

/** One HTTP call made by the portal, as shown in the request inspector. */
export interface LoggedRequest {
  at: Date;
  method: string;
  url: string;
  status: number;
  ms: number;
  servedBy: string; // X-Served-By header: which replica answered, e.g. orders-api-2
  traceId: string;  // X-Trace-Id header: search the logs with  bash scripts/trace.sh <traceId>
}

/** Keeps the last 15 requests in a signal, so the inspector updates live. */
@Injectable({ providedIn: 'root' })
export class RequestLog {
  readonly entries = signal<LoggedRequest[]>([]);

  add(entry: LoggedRequest) {
    this.entries.update((list) => [entry, ...list].slice(0, 15));
  }

  clear() {
    this.entries.set([]);
  }
}

/**
 * Runs for every HttpClient call (registered in app.config.ts). Reads the X-Served-By and X-Trace-Id
 * headers that every .NET service adds (Lab.Shared/LabHostExtensions.cs, UseLabDefaults).
 */
export const requestLogInterceptor: HttpInterceptorFn = (req, next) => {
  const log = inject(RequestLog);
  const started = performance.now();
  const record = (status: number, headers: { get(name: string): string | null } | undefined) =>
    log.add({
      at: new Date(),
      method: req.method,
      url: req.urlWithParams,
      status,
      ms: Math.round(performance.now() - started),
      servedBy: headers?.get('X-Served-By') ?? '-',
      traceId: headers?.get('X-Trace-Id') ?? '-',
    });

  return next(req).pipe(
    tap({
      next: (event) => {
        if (event instanceof HttpResponse) record(event.status, event.headers);
      },
      error: (error: HttpErrorResponse) => record(error.status, error.headers),
    }),
  );
};
