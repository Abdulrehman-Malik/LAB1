import { CurrencyPipe, DatePipe, DecimalPipe } from '@angular/common';
import { Component, OnInit, inject, input, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Api, DocumentInfo, OrderDetails, errorText } from './api';
import { Explain } from './explain';

@Component({
  selector: 'app-order-detail',
  imports: [RouterLink, CurrencyPipe, DatePipe, DecimalPipe, Explain],
  template: `
    <p><a routerLink="/orders">← Orders</a></p>
    <h2>
      Order {{ id() }}
      @if (cache()) {
        <span class="badge" [class.hit]="cache() === 'HIT'" title="X-Cache response header">Redis {{ cache() }}</span>
      }
    </h2>
    <app-explain>
      <p><code>GET /api/orders/{{ id() }}</code> goes to <code>orders-api</code>, which uses the <b>cache-aside</b> pattern
         (<code>src/orders-api/OrderDetailsService.cs</code>):</p>
      <ul>
        <li><span class="badge">Redis MISS</span>: reads the order from SQL, calls <code>customers-api</code> and <code>billing-api</code>
            over gRPC, and stores the combined answer in Redis for 60 s (key <code>orders-api:order:{{ id() }}</code>).</li>
        <li><span class="badge hit">Redis HIT</span>: returns the stored copy in about 1 ms. No SQL, no gRPC.</li>
      </ul>
      <p>Click <b>Reload</b> to see MISS turn into HIT. Pay the invoice and reload: it still shows Pending until the cached copy expires.
         That's the price of caching (stale data). The card titles say which service and which store each part came from.</p>
      <p>If a card says "unavailable", that service was down: orders-api still answers with what it has (graceful degradation).</p>
    </app-explain>
    <button (click)="load()">Reload</button>
    @if (error()) {
      <p class="error">{{ error() }}</p>
    }

    @if (details(); as d) {
      <div class="cards">
        <section class="card">
          <h3>Order <small>orders-api → SQL SalesDb.orders</small></h3>
          <p>{{ d.order.product }} × {{ d.order.quantity }}</p>
          <p>{{ d.order.amount | currency: 'PKR ' }}</p>
          <p>Status: <span class="badge" [class.hit]="d.order.status === 'Invoiced'">{{ d.order.status }}</span></p>
          <p>Created: {{ d.order.createdAt | date: 'medium' }}</p>
        </section>

        <section class="card">
          <h3>Customer <small>gRPC → customers-api → CustomersDb</small></h3>
          @if (d.customer; as c) {
            <p>{{ c.name }}</p>
            <p>{{ c.email }}</p>
            <p>{{ c.city }}</p>
          } @else {
            <p class="error">Unavailable: customers-api did not answer</p>
          }
        </section>

        <section class="card">
          <h3>Invoice <small>gRPC → billing-api → SalesDb.billing</small></h3>
          @if (d.invoice; as i) {
            <p>Invoice #{{ i.id }}: {{ i.amount | currency: 'PKR ' }}</p>
            <p>Status: <span class="badge" [class.hit]="i.status === 'Paid'">{{ i.status }}</span></p>
            <p>Email: <span class="badge" [class.hit]="i.emailSent">{{ i.emailSent ? 'sent' : 'not yet' }}</span>
               <small class="hint">(notification-worker, every 30 s)</small></p>
            @if (i.status === 'Pending') {
              <button (click)="pay(i.id)">Pay (fake gateway)</button>
            }
            @if (paid()) {
              <p class="hint">Paid! Still showing Pending? That's the cached copy in Redis, up to 60 s old.</p>
            }
          } @else {
            <p class="error">No invoice: billing-api was down or did not answer</p>
          }
        </section>

        <section class="card">
          <h3>Documents <small>documents-api → MinIO + DocumentsDb</small></h3>
          @for (doc of documents(); track doc.id) {
            <p><a [href]="api.downloadUrl(doc.id)">{{ doc.fileName }}</a> <small class="hint">{{ doc.sizeBytes / 1024 | number: '1.0-1' }} KB</small></p>
          } @empty {
            <p class="hint">No documents.</p>
          }
          <input type="file" (change)="upload($event)" />
        </section>
      </div>
    }
  `,
})
export class OrderDetailPage implements OnInit {
  protected readonly api = inject(Api);

  /** From the route /orders/:id (withComponentInputBinding in app.config.ts). */
  readonly id = input.required<string>();

  protected readonly details = signal<OrderDetails | null>(null);
  protected readonly documents = signal<DocumentInfo[]>([]);
  protected readonly cache = signal('');
  protected readonly error = signal('');
  protected readonly paid = signal(false);

  ngOnInit() {
    this.load();
  }

  protected load() {
    this.error.set('');
    this.api.orderDetails(Number(this.id())).subscribe({
      next: ({ details, cache }) => {
        this.details.set(details);
        this.cache.set(cache);
      },
      error: (e) => this.error.set(errorText(e)),
    });
    this.api.documents(Number(this.id())).subscribe({
      next: (docs) => this.documents.set(docs),
      error: () => this.documents.set([]),
    });
  }

  protected pay(invoiceId: number) {
    this.api.payInvoice(invoiceId, '4111 1111 1111 1111').subscribe({
      next: () => {
        this.paid.set(true);
        this.load();
      },
      error: (e) => this.error.set(errorText(e)),
    });
  }

  protected upload(event: Event) {
    const file = (event.target as HTMLInputElement).files?.[0];
    if (!file) return;
    this.api.uploadDocument(file, Number(this.id())).subscribe({
      next: () => this.load(),
      error: (e) => this.error.set(errorText(e)),
    });
  }
}
