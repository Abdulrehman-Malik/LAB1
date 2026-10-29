import { Component, inject, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { Api, SERVICES } from './api';
import { Explain } from './explain';

interface ServiceHealth {
  name: string;
  prefix: string;
  role: string;
  status: string;
  servedBy: string;
  ms: number;
  replicasSeen: Record<string, number>; // replica hostname -> how many of the last calls it answered
}

@Component({
  selector: 'app-system',
  imports: [Explain],
  template: `
    <h2>System</h2>
    <app-explain [open]="true" title="How the system fits together">
      <pre class="diagram">
Browser ──► :8000 gateway-traefik ─┬─ /                   web-portal (this page, Angular in Nginx)
                                   ├─ /api/customers      customers-api ─────┐
                                   ├─ /api/billing        billing-api ───────┤  db-sqlserver
                                   ├─ /api/orders         orders-api ────────┤  (CustomersDb, SalesDb, DocumentsDb)
                                   │                        │ gRPC ► customers-api, billing-api
                                   │                        └ cache-redis
                                   ├─ /api/documents      documents-api ─────┘ + storage-minio (files)
                                   └─ /api/notifications  notification-worker ─► mail-mailpit (SMTP), db-mongodb (audit)
autoscaler ── Docker API ── adds/removes replicas of the *-api services (infra/autoscale.yml)</pre>
      <p>Everything runs as one Docker Swarm stack, <code>lab</code> (<code>infra/stack.yml</code>). Service names in Swarm are
         <code>lab_&lt;name&gt;</code>, e.g. <code>lab_orders-api</code>. Each replica's hostname is <code>&lt;name&gt;-&lt;slot&gt;</code>, e.g. <code>orders-api-2</code>.</p>
      <p><b>Check all</b> calls each service's <code>/health</code> endpoint (it also checks that service's database).
         <b>Call ×10</b> calls one service 10 times and counts which replica answered: with 2+ replicas Traefik spreads the calls.
         Run <code>bash scripts/load.sh</code> and watch new replicas appear here.</p>
    </app-explain>

    <button (click)="checkAll()">Check all</button>
    <table>
      <thead>
        <tr><th>Service</th><th>Route</th><th>Role</th><th>Health</th><th>Last served by</th><th>ms</th><th>Replicas seen (last calls)</th><th></th></tr>
      </thead>
      <tbody>
        @for (s of services(); track s.name) {
          <tr>
            <td><code>{{ s.name }}</code></td>
            <td><code>{{ s.prefix }}</code></td>
            <td>{{ s.role }}</td>
            <td><span class="badge" [class.hit]="s.status === 'Healthy'" [class.bad]="s.status !== 'Healthy' && s.status !== '…'">{{ s.status }}</span></td>
            <td>{{ s.servedBy }}</td>
            <td>{{ s.ms }}</td>
            <td>
              @for (r of replicas(s); track r[0]) {
                <span class="badge">{{ r[0] }} × {{ r[1] }}</span>
              }
            </td>
            <td><button class="link" (click)="callTimes(s, 10)">Call ×10</button></td>
          </tr>
        }
      </tbody>
    </table>

    <h3>Tools</h3>
    <table>
      <thead><tr><th>Tool</th><th>Open</th><th>What to look at</th></tr></thead>
      <tbody>
        @for (t of tools; track t.name) {
          <tr><td>{{ t.name }}</td><td><a [href]="t.url" target="_blank">{{ t.url }}</a></td><td>{{ t.what }}</td></tr>
        }
      </tbody>
    </table>
  `,
})
export class SystemPage {
  private readonly api = inject(Api);
  private readonly host = location.hostname;

  protected readonly services = signal<ServiceHealth[]>(
    SERVICES.map((s) => ({ ...s, status: '…', servedBy: '-', ms: 0, replicasSeen: {} })),
  );

  protected readonly tools = [
    { name: 'Traefik dashboard', url: `http://${this.host}:8088`, what: 'Routers (path rules) and the replicas behind each service' },
    { name: 'Portainer', url: `https://${this.host}:9443`, what: 'Stacks, services, replicas, logs, secrets (user admin)' },
    { name: 'MinIO console', url: `http://${this.host}:9001`, what: 'Bucket "documents": the uploaded files (user labadmin)' },
    { name: 'Mailpit inbox', url: `http://${this.host}:8025`, what: 'Every email notification-worker sends' },
    { name: 'Image registry', url: `http://${this.host}:5000/v2/_catalog`, what: 'Images pushed by scripts/build.sh' },
  ];

  constructor() {
    this.checkAll();
  }

  protected replicas(s: ServiceHealth) {
    return Object.entries(s.replicasSeen);
  }

  protected checkAll() {
    for (const s of this.services()) this.callTimes(s, 1, true);
  }

  protected callTimes(service: ServiceHealth, times: number, reset = times > 1) {
    if (reset) this.update(service.name, { replicasSeen: {} });
    for (let i = 0; i < times; i++) {
      const started = performance.now();
      this.api.health(service.prefix).subscribe({
        next: (r) => this.record(service.name, r.body ?? '?', r.headers.get('X-Served-By') ?? '-', started),
        error: (e: HttpErrorResponse) =>
          this.record(service.name, e.status === 0 ? 'Unreachable' : `${e.status} ${e.error ?? ''}`, e.headers?.get('X-Served-By') ?? '-', started),
      });
    }
  }

  private record(name: string, status: string, servedBy: string, started: number) {
    const current = this.services().find((s) => s.name === name)!;
    const seen = { ...current.replicasSeen };
    if (servedBy !== '-') seen[servedBy] = (seen[servedBy] ?? 0) + 1;
    this.update(name, { status, servedBy, ms: Math.round(performance.now() - started), replicasSeen: seen });
  }

  private update(name: string, changes: Partial<ServiceHealth>) {
    this.services.update((list) => list.map((s) => (s.name === name ? { ...s, ...changes } : s)));
  }
}
