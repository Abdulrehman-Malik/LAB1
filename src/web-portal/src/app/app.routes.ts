import { Routes } from '@angular/router';
import { ActivityPage } from './activity';
import { CustomersPage } from './customers';
import { DocumentsPage } from './documents';
import { InvoicesPage } from './invoices';
import { OrderDetailPage } from './order-detail';
import { OrdersPage } from './orders';
import { SystemPage } from './system';

export const routes: Routes = [
  { path: '', redirectTo: 'orders', pathMatch: 'full' },
  { path: 'orders', component: OrdersPage, title: 'Orders' },
  { path: 'orders/:id', component: OrderDetailPage, title: 'Order details' },
  { path: 'customers', component: CustomersPage, title: 'Customers' },
  { path: 'invoices', component: InvoicesPage, title: 'Invoices' },
  { path: 'documents', component: DocumentsPage, title: 'Documents' },
  { path: 'activity', component: ActivityPage, title: 'Activity' },
  { path: 'system', component: SystemPage, title: 'System' },
];
