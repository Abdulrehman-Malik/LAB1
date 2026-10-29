import { DatePipe } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Api, AuditEvent, errorText } from './api';
import { Explain } from './explain';

@Component({
  selector: 'app-activity',
  imports: [DatePipe, RouterLink, Explain],
  template: `
    <h2>Activity <small class="hint">MongoDB audit log</small></h2>
    <app-explain>
      <p><code>notification-worker</code> is a <b>background job</b>: nobody calls it. Every 30 s it
         (<code>src/notification-worker/InvoiceEmailWorker.cs</code>):</p>
      <ol>
        <li><b>claims</b> new invoices with one atomic SQL statement on <code>SalesDb.billing.Invoices</code></li>
        <li>looks up each customer's email from <code>customers-api</code> over <b>gRPC</b></li>
        <li>sends the email over <b>SMTP</b> to Mailpit, a fake mail server. See the <a [href]="mailpit" target="_blank">inbox (:8025)</a></li>
        <li>writes one <b>document</b> per email to <b>MongoDB</b> (database <code>lab</code>, collection <code>audit</code>). That's this list.</li>
      </ol>
      <p>MongoDB stores JSON-like documents that don't all need the same shape: a failure event has a <code>detail</code> and no recipient.
         Each event keeps the <b>trace id</b> of the worker run. Run <code>bash scripts/trace.sh &lt;id&gt;</code> to see that run's logs,
         including the gRPC call it made into customers-api.</p>
    </app-explain>
    <button (click)="load()">Reload</button>
    @if (error()) {
      <p class="error">{{ error() }}</p>
    }

    <table>
      <thead>
        <tr><th>When</th><th>Event</th><th>Invoice</th><th>Order</th><th>Recipient</th><th>Detail</th><th>Worker</th><th>Trace id</th></tr>
      </thead>
      <tbody>
        @for (e of events(); track e.id) {
          <tr>
            <td>{{ e.at | date: 'medium' }}</td>
            <td><span class="badge" [class.hit]="e.event === 'InvoiceEmailSent'" [class.bad]="e.event !== 'InvoiceEmailSent'">{{ e.event }}</span></td>
            <td>{{ e.invoiceId }}</td>
            <td><a [routerLink]="['/orders', e.orderId]">{{ e.orderId }}</a></td>
            <td>{{ e.recipient ?? '-' }}</td>
            <td>{{ e.detail ?? '-' }}</td>
            <td>{{ e.worker }}</td>
            <td><code class="trace">{{ e.traceId }}</code></td>
          </tr>
        } @empty {
          <tr><td colspan="8" class="hint">Nothing yet. Place an order and wait up to 30 seconds.</td></tr>
        }
      </tbody>
    </table>
  `,
})
export class ActivityPage {
  private readonly api = inject(Api);

  protected readonly mailpit = `http://${location.hostname}:8025`;
  protected readonly events = signal<AuditEvent[]>([]);
  protected readonly error = signal('');

  constructor() {
    this.load();
  }

  protected load() {
    this.api.audit().subscribe({
      next: (events) => this.events.set(events),
      error: (e) => this.error.set(errorText(e)),
    });
  }
}
