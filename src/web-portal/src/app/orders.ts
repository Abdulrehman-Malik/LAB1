import { CurrencyPipe, DatePipe } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { Api, Customer, Order, errorText } from './api';
import { Explain } from './explain';

@Component({
  selector: 'app-orders',
  imports: [FormsModule, RouterLink, CurrencyPipe, DatePipe, Explain],
  template: `
    <h2>Orders</h2>
    <app-explain>
      <p><b>Place order</b> sends <code>POST /api/orders</code>. Traefik routes it to a replica of <code>orders-api</code>, which:</p>
      <ol>
        <li>asks <code>customers-api</code> over <b>gRPC</b> whether the customer exists (unknown customer: 400 error below)</li>
        <li>saves the order in SQL Server, <code>SalesDb.orders.Orders</code> (EF Core)</li>
        <li>asks <code>billing-api</code> over <b>gRPC</b> to create an invoice. Status becomes <span class="badge hit">Invoiced</span>.
            If billing-api is down or too slow (5 s deadline), the order is still saved as <span class="badge warn">Created</span>.</li>
      </ol>
      <p><span class="badge warn">Created</span> orders don't stay that way: a background job in orders-api
         (<code>InvoiceRetryWorker.cs</code>) asks billing-api again every 30 s. This is <b>eventual consistency</b>:
         the system catches up on its own. Retrying is safe because <code>CreateInvoice</code> is idempotent.</p>
      <p>About 30 s later, <code>notification-worker</code> emails the invoice (see Activity, and the inbox at <a [href]="mailpit" target="_blank">:8025</a>).</p>
      <p>Code: <code>src/orders-api/Program.cs</code> (MapPost), <code>src/web-portal/src/app/orders.ts</code>.
         Watch it in the request inspector below, then run <code>bash scripts/trace.sh &lt;trace id&gt;</code>.</p>
    </app-explain>

    <form class="row" (ngSubmit)="create()">
      <label>Customer
        <select name="customerId" [(ngModel)]="customerId" required>
          @for (c of customers(); track c.id) {
            <option [ngValue]="c.id">{{ c.id }}: {{ c.name }}</option>
          }
          <option [ngValue]="999">999: (does not exist, to see the error)</option>
        </select>
      </label>
      <label>Product <input name="product" [(ngModel)]="product" placeholder="e.g. Laptop stand" required /></label>
      <label>Qty <input name="quantity" type="number" min="1" [(ngModel)]="quantity" class="narrow" /></label>
      <label>Unit price <input name="unitPrice" type="number" min="1" [(ngModel)]="unitPrice" class="narrow" /></label>
      <button type="submit">Place order</button>
    </form>
    @if (error()) {
      <p class="error">{{ error() }}</p>
    }

    <table>
      <thead>
        <tr><th>Id</th><th>Customer</th><th>Product</th><th>Qty</th><th>Amount</th><th>Status</th><th>Created</th><th></th></tr>
      </thead>
      <tbody>
        @for (o of orders(); track o.id) {
          <tr>
            <td>{{ o.id }}</td>
            <td>{{ o.customerId }}</td>
            <td>{{ o.product }}</td>
            <td>{{ o.quantity }}</td>
            <td>{{ o.amount | currency: 'PKR ' }}</td>
            <td><span class="badge" [class.hit]="o.status === 'Invoiced'" [class.warn]="o.status !== 'Invoiced'">{{ o.status }}</span></td>
            <td>{{ o.createdAt | date: 'short' }}</td>
            <td><a [routerLink]="['/orders', o.id]">Details</a></td>
          </tr>
        } @empty {
          <tr><td colspan="8" class="hint">No orders yet. Place one above.</td></tr>
        }
      </tbody>
    </table>
  `,
})
export class OrdersPage {
  private readonly api = inject(Api);

  protected readonly mailpit = `http://${location.hostname}:8025`;
  protected readonly orders = signal<Order[]>([]);
  protected readonly customers = signal<Customer[]>([]);
  protected readonly error = signal('');
  protected readonly customerId = signal(1);
  protected readonly product = signal('');
  protected readonly quantity = signal(1);
  protected readonly unitPrice = signal(1000);

  constructor() {
    this.api.customers().subscribe({ next: (c) => this.customers.set(c), error: (e) => this.error.set(errorText(e)) });
    this.load();
  }

  private load() {
    this.api.orders().subscribe({
      next: (orders) => this.orders.set(orders),
      error: (e) => this.error.set(errorText(e)),
    });
  }

  protected create() {
    this.error.set('');
    this.api
      .createOrder({
        customerId: this.customerId(),
        product: this.product(),
        quantity: this.quantity(),
        unitPrice: this.unitPrice(),
      })
      .subscribe({
        next: () => {
          this.product.set('');
          this.load();
        },
        error: (e) => this.error.set(errorText(e)),
      });
  }
}
