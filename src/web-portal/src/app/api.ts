import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { map } from 'rxjs';

export interface Customer {
  id: number;
  name: string;
  email: string;
  city: string;
  createdAt: string;
}

export interface Order {
  id: number;
  customerId: number;
  product: string;
  quantity: number;
  unitPrice: number;
  amount: number;
  status: string;
  createdAt: string;
}

/** What orders-api returns for one order: SQL data + customer (gRPC) + invoice (gRPC). */
export interface OrderDetails {
  order: Order;
  customer: { id: number; name: string; email: string; city: string } | null;
  invoice: { id: number; amount: number; status: string; emailSent: boolean } | null;
}

export interface Invoice {
  id: number;
  orderId: number;
  customerId: number;
  amount: number;
  status: string;
  paymentReference: string | null;
  createdAt: string;
  paidAt: string | null;
  emailSent: boolean;
}

export interface DocumentInfo {
  id: number;
  orderId: number | null;
  fileName: string;
  contentType: string;
  sizeBytes: number;
  bucket: string;
  objectKey: string;
  uploadedAt: string;
}

export interface AuditEvent {
  id: string;
  event: string;
  invoiceId: number;
  orderId: number;
  recipient: string | null;
  detail: string | null;
  traceId: string | null;
  worker: string;
  at: string;
}

/** The services behind Traefik, with the path prefix Traefik routes to each. */
export const SERVICES = [
  { name: 'customers-api', prefix: '/api/customers', role: 'Customers (REST + gRPC server)' },
  { name: 'billing-api', prefix: '/api/billing', role: 'Invoices + fake payments (REST + gRPC server)' },
  { name: 'orders-api', prefix: '/api/orders', role: 'Orders (REST, gRPC client, Redis cache)' },
  { name: 'documents-api', prefix: '/api/documents', role: 'Files (REST, MinIO + SQL)' },
  { name: 'notification-worker', prefix: '/api/notifications', role: 'Invoice emails (background job, MongoDB audit)' },
];

/**
 * The portal never talks to a database. Every call is REST to a relative /api/... URL:
 * in Swarm, Traefik routes it to the right service; with `ng serve`, proxy.conf.json sends it to Traefik.
 * Every response is also recorded by request-log.ts (the inspector at the bottom of the page).
 */
@Injectable({ providedIn: 'root' })
export class Api {
  private readonly http = inject(HttpClient);

  customers() {
    return this.http.get<Customer[]>('/api/customers');
  }

  createCustomer(customer: { name: string; email: string; city: string }) {
    return this.http.post<Customer>('/api/customers', customer);
  }

  orders() {
    return this.http.get<Order[]>('/api/orders');
  }

  createOrder(order: { customerId: number; product: string; quantity: number; unitPrice: number }) {
    return this.http.post<Order>('/api/orders', order);
  }

  /** Also returns the X-Cache header: HIT = served from Redis, MISS = built from SQL + two gRPC calls. */
  orderDetails(id: number) {
    return this.http
      .get<OrderDetails>(`/api/orders/${id}`, { observe: 'response' })
      .pipe(map((response) => ({ details: response.body!, cache: response.headers.get('X-Cache') ?? '?' })));
  }

  invoices() {
    return this.http.get<Invoice[]>('/api/billing/invoices');
  }

  payInvoice(invoiceId: number, cardNumber: string) {
    return this.http.post(`/api/billing/invoices/${invoiceId}/pay`, { cardNumber });
  }

  documents(orderId?: number) {
    return this.http.get<DocumentInfo[]>('/api/documents', { params: orderId ? { orderId } : {} });
  }

  uploadDocument(file: File, orderId: number | null) {
    const form = new FormData();
    form.append('file', file);
    if (orderId) form.append('orderId', String(orderId));
    return this.http.post<DocumentInfo>('/api/documents', form);
  }

  deleteDocument(id: number) {
    return this.http.delete(`/api/documents/${id}`);
  }

  downloadUrl(id: number) {
    return `/api/documents/${id}/download`;
  }

  audit() {
    return this.http.get<AuditEvent[]>('/api/notifications/audit');
  }

  /** Health endpoints return plain text ("Healthy"/"Unhealthy") with 200 or 503. */
  health(prefix: string) {
    return this.http.get(`${prefix}/health`, { observe: 'response', responseType: 'text' });
  }
}

/** Turns an HTTP error into a readable message (the APIs return plain strings or ProblemDetails). */
export function errorText(error: HttpErrorResponse): string {
  const body = error.error;
  const detail = typeof body === 'string' ? body : (body?.detail ?? body?.title);
  return `${error.status} ${error.statusText}${detail ? ': ' + detail : ''}`;
}
