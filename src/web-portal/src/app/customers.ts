import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Api, Customer, errorText } from './api';
import { Explain } from './explain';

@Component({
  selector: 'app-customers',
  imports: [FormsModule, Explain],
  template: `
    <h2>Customers</h2>
    <app-explain>
      <p>The simplest flow in the system: <code>GET/POST /api/customers</code> → Traefik → <code>customers-api</code> (REST, port 8080)
         → SQL Server <code>CustomersDb.dbo.Customers</code> through EF Core. No other service is involved.</p>
      <p>The same service also runs a <b>gRPC server on port 8081</b>, which other services use (orders-api, notification-worker),
         never the browser. The contract is <code>src/shared-protos/customers.proto</code>.</p>
      <p>The first 3 customers are seed data from the EF Core migration (<code>CustomersDbContext.OnModelCreating</code>).
         Emails must be unique: try adding a duplicate to see the error.</p>
      <p>Code: <code>src/customers-api/Program.cs</code>, <code>Grpc/CustomerGrpcService.cs</code>, <code>Data/</code>.</p>
    </app-explain>

    <form class="row" (ngSubmit)="create()">
      <label>Name <input name="name" [(ngModel)]="name" placeholder="Full name" required /></label>
      <label>Email <input name="email" [(ngModel)]="email" placeholder="name@lab.local" required /></label>
      <label>City <input name="city" [(ngModel)]="city" placeholder="City" /></label>
      <button type="submit">Add customer</button>
    </form>
    @if (error()) {
      <p class="error">{{ error() }}</p>
    }

    <table>
      <thead>
        <tr><th>Id</th><th>Name</th><th>Email</th><th>City</th></tr>
      </thead>
      <tbody>
        @for (c of customers(); track c.id) {
          <tr><td>{{ c.id }}</td><td>{{ c.name }}</td><td>{{ c.email }}</td><td>{{ c.city }}</td></tr>
        }
      </tbody>
    </table>
  `,
})
export class CustomersPage {
  private readonly api = inject(Api);

  protected readonly customers = signal<Customer[]>([]);
  protected readonly error = signal('');
  protected readonly name = signal('');
  protected readonly email = signal('');
  protected readonly city = signal('');

  constructor() {
    this.load();
  }

  private load() {
    this.api.customers().subscribe({
      next: (customers) => this.customers.set(customers),
      error: (e) => this.error.set(errorText(e)),
    });
  }

  protected create() {
    this.error.set('');
    this.api.createCustomer({ name: this.name(), email: this.email(), city: this.city() }).subscribe({
      next: () => {
        this.name.set('');
        this.email.set('');
        this.city.set('');
        this.load();
      },
      error: (e) => this.error.set(errorText(e)),
    });
  }
}
