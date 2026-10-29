# LAB1 Code Walkthrough

How the code is organised and how requests travel through it. For hands-on practice, see [exercises.md](exercises.md).
For setup history and decisions, see [lab-decisions.md](lab-decisions.md).

## 1. The parts

| Swarm service | Folder | Talks to | Data |
|---|---|---|---|
| `lab_gateway-traefik` | (config in `infra/stack.yml`) | every service with `traefik.enable=true` | - |
| `lab_web-portal` | `src/web-portal` | the APIs, through the gateway | - |
| `lab_customers-api` | `src/customers-api` | - (it is called) | SQL `CustomersDb` |
| `lab_billing-api` | `src/billing-api` | - (it is called) | SQL `SalesDb.billing` |
| `lab_orders-api` | `src/orders-api` | customers-api + billing-api (gRPC), Redis | SQL `SalesDb.orders` |
| `lab_documents-api` | `src/documents-api` | MinIO (S3 API) | SQL `DocumentsDb` + bucket `documents` |
| `lab_notification-worker` | `src/notification-worker` | customers-api (gRPC), Mailpit (SMTP), MongoDB | reads `SalesDb.billing.Invoices` |
| `lab_autoscaler` | `src/autoscaler` | Docker Engine API | rules in `infra/autoscale.yml` |
| `lab_db-sqlserver`, `lab_cache-redis`, `lab_db-mongodb`, `lab_storage-minio`, `lab_mail-mailpit` | (images) | - | - |
| `ops_image-registry`, `ops_portainer-ui`, `ops_portainer-agent` | `infra/ops.yml` | - | - |

## 2. One pattern for every .NET service

Open any `Program.cs`: once you know one, you know them all.

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.AddLabDefaults(grpc: true);                      // Lab.Shared: secrets from /run/secrets, REST :8080, gRPC :8081, log format
builder.Services.AddLabDbContext<XDbContext>(config, "Db", schema);   // EF Core, provider from Database:Provider
builder.Services.AddHealthChecks().AddDbContextCheck<XDbContext>();   // /health also checks the database

var app = builder.Build();
app.UseLabDefaults();                                    // X-Served-By + X-Trace-Id headers, request log, graceful drain
await app.MigrateDatabaseAsync<XDbContext>();            // apply EF migrations at startup, retry until SQL is up

var api = app.MapGroup("/api/x");                        // same prefix as the Traefik rule
api.MapHealthChecks("/health");                          // used by Swarm's healthcheck and the portal's System page
api.MapGet(...); api.MapPost(...);                       // minimal API endpoints
```

| `Lab.Shared` file | Does |
|---|---|
| `LabHostExtensions.cs` | `AddLabDefaults` (secrets, ports, logging), `UseLabDefaults` (headers, request log), `UseLabGracefulShutdown` (8 s drain) |
| `LabConsoleFormatter.cs` | The one-line log format with `trace=<id>` |
| `DatabaseExtensions.cs` | `AddLabDbContext` (SqlServer/MySql/Postgres switch, per-schema migration history), `MigrateDatabaseAsync`, `SqlServerConnectionString` |

**Settings:** `appsettings.json` = values inside Swarm (`db-sqlserver`, `cache-redis`, ...). `appsettings.Development.json` = localhost values
for `dotnet run`. Environment variables in `stack.yml` and files in `/run/secrets` override both.

## 3. Follow one request: "Place order"

| # | Where | What happens | Log line (orders-api unless noted) |
|---|---|---|---|
| 1 | `web-portal/src/app/orders.ts` → `create()` | `POST /api/orders` | (inspector row) |
| 2 | `infra/stack.yml`, orders-api labels | Traefik picks a healthy orders-api replica | gateway access log |
| 3 | `orders-api/Program.cs` → `MapPost("")` | validates input | |
| 4 | gRPC → `customers-api/Grpc/CustomerGrpcService.cs` | customer exists? 404 → 400; down → 503 | customers-api: `POST /customers.v1.CustomerService/GetCustomer -> 200` |
| 5 | EF Core | row in `SalesDb.orders.Orders` | `Order 7 saved: customer 1, ...` |
| 6 | gRPC → `billing-api/Grpc/BillingGrpcService.cs` | row in `SalesDb.billing.Invoices` | billing-api: `Invoice 7 created for order 7` |
| 7 | `orders-api/Program.cs` | status → Invoiced, `201 Created` | `Order 7 invoiced ...`, `POST /api/orders -> 201 in 48 ms` |
| 8 | ≤ 30 s later: `notification-worker/InvoiceEmailWorker.cs` | claims the invoice, gRPC to customers-api, SMTP, MongoDB audit | worker: `Emailed invoice 7 to ali@lab.local` |

Steps 1–7 share one **trace id**: `bash scripts/trace.sh <id>` prints them in order. Step 8 has its own trace id, which is saved in the audit event.

If step 6 fails (billing down, or slower than the 5 s gRPC deadline), the order stays **Created**. `orders-api/InvoiceRetryWorker.cs`
retries every 30 s until billing answers. It's safe because `CreateInvoice` is idempotent: one invoice per order, enforced by a unique index.

**Order details** (`GET /api/orders/{id}` → `orders-api/OrderDetailsService.cs`): Redis hit → return (`X-Cache: HIT`).
Miss → SQL + 2 gRPC calls → store in Redis 60 s (`X-Cache: MISS`). With `Debug` logging you see `Cache HIT/MISS` lines.

**Upload document** (`documents-api/Program.cs` → `FileStorage.cs`): bytes → MinIO `documents/orders/<id>/<guid>/<file>`, then a row in `DocumentsDb.dbo.Documents`. If the row fails, the object is deleted.

## 4. Where to look when something's wrong

| Symptom | First look | Then |
|---|---|---|
| A page shows an error | the request inspector (status, served by) | `bash scripts/trace.sh <id>` |
| Something failed but you don't know what | `bash scripts/logs.sh errors` | `bash scripts/logs.sh <service> 100` |
| A service is `0/1` | `docker service ps lab_<service> --no-trunc` (error column) | `bash scripts/logs.sh <service> 100` |
| Wrong route / 404 from the gateway | Traefik dashboard :8088 | labels in `stack.yml` |
| Unexpected scaling | `docker service logs lab_autoscaler --since 10m` | `infra/autoscale.yml`, `dotnet test` |
| Emails missing | Activity page, `bash scripts/logs.sh notification-worker` | Mailpit :8025 |

## 5. Change → deploy loop

| Loop | Commands | Use for |
|---|---|---|
| Fast (no Docker) | PowerShell: `. .\scripts\dev-env.ps1`, `dotnet run --project src/<svc>`; `cd src/web-portal; ng serve` | writing code, debugging in VS Code |
| Real (like CI/CD) | Ubuntu: `bash scripts/ci.sh <svc>` | proving it works in containers, behind the gateway |
| Manual | `bash scripts/build.sh <svc> <tag>`, `bash scripts/deploy-service.sh <svc> <tag>` | step by step |
| Config only | edit `infra/*.yml` / `versions.env`, then `bash scripts/deploy.sh` | env vars, labels, limits, autoscale rules |

## 6. Still out of scope

| Item | Note |
|---|---|
| Switching to MySQL/Postgres | provider switch exists; needs per-provider migrations (see exercises.md appendix) |
| Login (IDP / Keycloak) | services are open; a real system checks a JWT on every request |
| Multi-node Swarm, HA SQL Server | single laptop |
| Azure DevOps pipelines | `scripts/ci.sh` runs the same stages locally |
