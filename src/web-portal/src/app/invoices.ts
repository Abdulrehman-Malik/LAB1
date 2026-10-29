import { CurrencyPipe, DatePipe } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Api, Invoice, errorText } from './api';
import { Explain } from './explain';

@Component({
  selector: 'app-invoices',
  imports: [CurrencyPipe, DatePipe, RouterLink, Explain],
  template: `
    <h2>Invoices</h2>
    <app-explain>
      <p>Invoices are owned by <code>billing-api</code> and stored in <code>SalesDb.billing.Invoices</code>.
         They are never created from this page: <code>orders-api</code> creates them over <b>gRPC</b> when an order is placed
         (<code>src/billing-api/Grpc/BillingGrpcService.cs</code>, <code>CreateInvoice</code>).</p>
      <p><b>Pay</b> calls <code>POST /api/billing/invoices/{{ '{' }}id{{ '}' }}/pay</code>: a fake payment gateway that approves every card.</p>
      <p><b>Email</b> is set by <code>notification-worker</code>. It reads this table with plain SQL (not through billing-api),
         so this table is shared by two services. If billing-api renames a column, the worker breaks.</p>
      <p>This page reads fresh data (no Redis), unlike the order details page.</p>
    </app-explain>
    <button (click)="load()">Reload</button>
    @if (error()) {
      <p class="error">{{ error() }}</p>
    }

    <table>
      <thead>
        <tr><th>Id</th><th>Order</th><th>Customer</th><th>Amount</th><th>Status</th><th>Email</th><th>Payment ref</th><th>Created</th><th></th></tr>
      </thead>
      <tbody>
        @for (i of invoices(); track i.id) {
          <tr>
            <td>{{ i.id }}</td>
            <td><a [routerLink]="['/orders', i.orderId]">{{ i.orderId }}</a></td>
            <td>{{ i.customerId }}</td>
            <td>{{ i.amount | currency: 'PKR ' }}</td>
            <td><span class="badge" [class.hit]="i.status === 'Paid'" [class.warn]="i.status !== 'Paid'">{{ i.status }}</span></td>
            <td><span class="badge" [class.hit]="i.emailSent">{{ i.emailSent ? 'sent' : 'pending' }}</span></td>
            <td><code>{{ i.paymentReference ?? '-' }}</code></td>
            <td>{{ i.createdAt | date: 'short' }}</td>
            <td>
              @if (i.status === 'Pending') {
                <button (click)="pay(i.id)">Pay</button>
              }
            </td>
          </tr>
        } @empty {
          <tr><td colspan="9" class="hint">No invoices yet. Place an order first.</td></tr>
        }
      </tbody>
    </table>
  `,
})
export class InvoicesPage {
  private readonly api = inject(Api);

  protected readonly invoices = signal<Invoice[]>([]);
  protected readonly error = signal('');

  constructor() {
    this.load();
  }

  protected load() {
    this.api.invoices().subscribe({
      next: (invoices) => this.invoices.set(invoices),
      error: (e) => this.error.set(errorText(e)),
    });
  }

  protected pay(id: number) {
    this.api.payInvoice(id, '4111 1111 1111 1111').subscribe({
      next: () => this.load(),
      error: (e) => this.error.set(errorText(e)),
    });
  }
}
