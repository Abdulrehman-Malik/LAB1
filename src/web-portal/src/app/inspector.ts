import { DatePipe } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { RequestLog } from './request-log';

/** The bar at the bottom of every page: the last requests, which replica served them, and their trace ids. */
@Component({
  selector: 'app-inspector',
  imports: [DatePipe],
  template: `
    <footer class="inspector" [class.collapsed]="collapsed()">
      <div class="inspector-head">
        <strong>Request inspector</strong>
        <span class="hint">
          Every API call from this page. <b>Served by</b> = the replica that answered (X-Served-By).
          Copy a <b>trace id</b> and run <code>bash scripts/trace.sh &lt;id&gt;</code> to see its log lines in every service.
        </span>
        <span class="spacer"></span>
        <button class="link" (click)="log.clear()">Clear</button>
        <button class="link" (click)="collapsed.set(!collapsed())">{{ collapsed() ? 'Show' : 'Hide' }}</button>
      </div>
      @if (!collapsed()) {
        <table class="compact">
          <thead>
            <tr><th>Time</th><th>Request</th><th>Status</th><th>ms</th><th>Served by</th><th>Trace id</th></tr>
          </thead>
          <tbody>
            @for (r of log.entries(); track $index) {
              <tr>
                <td>{{ r.at | date: 'HH:mm:ss' }}</td>
                <td><code>{{ r.method }} {{ r.url }}</code></td>
                <td><span class="badge" [class.hit]="r.status < 400" [class.bad]="r.status >= 400 || r.status === 0">{{ r.status || 'failed' }}</span></td>
                <td>{{ r.ms }}</td>
                <td>{{ r.servedBy }}</td>
                <td>
                  @if (r.traceId !== '-') {
                    <code class="trace" title="Click to copy" (click)="copy(r.traceId)">{{ r.traceId }}</code>
                  } @else { - }
                </td>
              </tr>
            } @empty {
              <tr><td colspan="6" class="hint">No requests yet.</td></tr>
            }
          </tbody>
        </table>
        @if (copied()) { <p class="hint">Copied: <code>bash scripts/trace.sh {{ copied() }}</code></p> }
      }
    </footer>
  `,
})
export class Inspector {
  protected readonly log = inject(RequestLog);
  protected readonly collapsed = signal(false);
  protected readonly copied = signal('');

  protected copy(traceId: string) {
    navigator.clipboard?.writeText(`bash scripts/trace.sh ${traceId}`);
    this.copied.set(traceId);
  }
}
