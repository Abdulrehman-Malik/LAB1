import { Component } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { Inspector } from './inspector';

@Component({
  imports: [RouterOutlet, RouterLink, RouterLinkActive, Inspector],
  selector: 'app-root',
  template: `
    <header>
      <strong>LAB1 Portal</strong>
      <nav>
        <a routerLink="/orders" routerLinkActive="active" title="orders-api">Orders</a>
        <a routerLink="/customers" routerLinkActive="active" title="customers-api">Customers</a>
        <a routerLink="/invoices" routerLinkActive="active" title="billing-api">Invoices</a>
        <a routerLink="/documents" routerLinkActive="active" title="documents-api + MinIO">Documents</a>
        <a routerLink="/activity" routerLinkActive="active" title="notification-worker + MongoDB">Activity</a>
        <a routerLink="/system" routerLinkActive="active" title="Health of every service">System</a>
      </nav>
    </header>
    <main>
      <router-outlet />
    </main>
    <app-inspector />
  `,
})
export class App {}
