# LAB1: Lab Decisions

Source: the "How the Application Works: A Beginner's Guide" document (reference repo: https://github.com/Abdulrehman-Malik/vendor-takeover-lab), plus your original requirements and answers.
Date: 2026-10-28

## Machine (checked 2026-10-28)

| Item | Status |
|---|---|
| OS / hardware | Windows 11 Pro, 4 logical CPUs, **7.9 GB RAM**, 92 GB free on D: |
| Docker Desktop | 29.9.2 (WSL2 backend), kubectl included |
| WSL | WSL 2, only the `docker-desktop` distro. **No Ubuntu yet** |
| Node.js / npm | 24.21 / 11.19 |
| .NET SDK | **Not installed** |
| Angular CLI | **Not installed** |
| MySQL | Windows services `MySQL80` (stopped) and `PerfectLPG-MySQL` (running on port 3307, belongs to another app). **The lab won't use either.** |
| Git, VS Code, winget | Installed |

## Decisions

| # | Topic | Decision | Where it came from |
|---|---|---|---|
| 1 | .NET | **.NET 10** (multi-stage Dockerfile: `sdk:10.0` → `aspnet:10.0`) | Guide §3.5, §8.5 |
| 2 | Services (3 to 5) | **4 APIs + 1 worker:** `orders-api`, `customers-api`, `billing-api`, `documents-api`, `notification-worker` | Guide §8.1 |
| 3 | Domain | Orders / Customers / Billing / Documents | Guide §2, §8.1 |
| 4 | Database | **SQL Server 2022 Express** in a container (`mcr.microsoft.com/mssql/server:2022-latest`, `MSSQL_PID=Express`). Container limit 2 GB (needed to start), `max server memory` 768 MB (`scripts/sql-tune.sh`). EF Core picks the provider from config (`Database:Provider = SqlServer \| MySql \| Postgres`), so you can switch later; switching also means generating new migrations for that provider. | **Changed 2026-10-29 at your request** (was MySQL). Matches the guide. |
| 5 | DB layout | 3 databases: `CustomersDb`, `SalesDb` (shared by orders and billing, separate schemas), `DocumentsDb`. EF Core migrations create each one on first start. The lab connects as `sa` (as in the guide). | Guide §8.1 |
| 6 | Frontend | One simple **Angular** app `web-portal`, served by Nginx | Guide §3.1, §8.1 |
| 7 | Admin | `admin-node`: a Node.js health dashboard (optional, later) | Guide §3.2 |
| 8 | Service-to-service | **gRPC:** orders-api → customers-api and billing-api. Shared `.proto` files in `src/shared-protos` | Guide §3.3 |
| 9 | Frontend → backend | REST/JSON through the reverse proxy | Guide §3.3 |
| 10 | Reverse proxy | **Traefik v3**, path routing through Swarm labels | Guide §3.4, §9.6 |
| 11 | Cache | **Redis 7**, cache-aside pattern in orders-api | Guide §3.13 |
| 12 | Files / docs / email | MinIO (`pgsty/minio:RELEASE.2026-08-04T00-00-00Z`), MongoDB 8.0 (audit log), Mailpit v1.32 (SMTP) | Guide §3.12, §3.14, §9.12 |
| 13 | Containers | Docker, one image per service, tags = version or build number (never `latest`) | Guide §3.5, §4.6 |
| 14 | Orchestration | **Docker Swarm**, single node on Docker Desktop. One `infra/stack.yml` for the whole system | Guide §3.6 |
| 15 | Dashboard | **Portainer CE** on port 9443. Git stays the source of truth (avoid "Portainer drift") | Guide §3.7 |
| 16 | Registry | Local registry on `localhost:5000` (stands in for ACR) | Guide §3.9, §8.1 |
| 17 | WSL | **Install Ubuntu in WSL** and enable Docker Desktop WSL integration. Lab scripts are bash and run from Ubuntu. | Guide §8.7 |
| 18 | Autoscaling | **Custom .NET autoscaler** service in the stack. It reads `infra/autoscale.yml` (per service: `min: 1`, `max`, CPU up/down thresholds, cooldown), checks container CPU through the Docker API, and runs `docker service scale`. **At least 1 replica always.** | Your requirement #8. The guide doesn't cover it, because Swarm has no built-in autoscaling. |
| 19 | Secrets | Swarm secrets, not plain env vars | Guide §3.15, §9.11 |
| 20 | CI/CD | Local `scripts/ci.sh` (test → build → push → rolling update → smoke test). Azure DevOps pipelines are optional, later. | Guide §4, §8.6 |
| 21 | Login (IDP) | Skipped for now (Keycloak optional later) | Guide §8.1 |
| 22 | Names | Swarm services `lab_<name>` (app) and `ops_<name>` (registry, Portainer). Infra = `<role>-<tool>`: `gateway-traefik`, `db-sqlserver`, `cache-redis`, `db-mongodb`, `storage-minio`, `mail-mailpit`. Replica hostname `<name>-<slot>`. Every service has a `lab.description` label | Your request, 2026-10-29 |
| 23 | Logging | One line per entry in every .NET service, with a W3C trace id that follows a request across REST and gRPC (`Lab.Shared/LabConsoleFormatter.cs`). Responses carry `X-Served-By` and `X-Trace-Id`. `scripts/trace.sh` and `scripts/logs.sh` collect them. Traefik access log on | Your request, 2026-10-29 |
| 24 | Worker | notification-worker: atomic SQL claim on `SalesDb.billing.Invoices`, gRPC to customers-api, MailKit → Mailpit, audit in MongoDB 8.0 (`lab.audit`). One replica, not autoscaled | Guide §8.1 |
| 25 | Tests / CI | xUnit `tests/Lab.Tests`: autoscaler rules + checks of the real `infra/autoscale.yml`. `scripts/ci.sh`: tests → build → rolling update → smoke test; Swarm auto-rollback is detected and `versions.env` restored | Your request, 2026-10-29 |

## Memory budget (8 GB laptop, Docker has 3.8 GB)

Close heavy apps while the stack runs. Limits: SQL Server ~1 GB real use (2 GB limit), MongoDB 512 MB (0.25 GB cache), MinIO 384 MB,
each .NET service 256 MB, Mailpit 64 MB, Redis 96 MB. Autoscaler `max` ≤ 3 (checked by a unit test).
If Docker runs out of memory, give WSL more: create `%UserProfile%\.wslconfig` with `[wsl2]` and `memory=5GB`, then run `wsl --shutdown`.

## Ports

| Port | What |
|---|---|
| 8000 | gateway-traefik: portal `/`, APIs `/api/*` |
| 8088 | Traefik dashboard |
| 9443 | Portainer |
| 5000 | Image registry |
| 1433 | SQL Server (user `sa`, password `MSSQL_SA_PASSWORD` in `infra/.env`) |
| 6379 | Redis |
| 27017 | MongoDB (user `labadmin`, `MONGO_ROOT_PASSWORD`, authSource `admin`) |
| 9000 / 9001 | MinIO S3 API / console (user `labadmin`, `MINIO_ROOT_PASSWORD`) |
| 8025 / 1025 | Mailpit inbox / SMTP |
| 5101–5501 | Local `dotnet run` ports (see each `appsettings.Development.json`) |

## Step-by-step plan

1. ✅ VS Code extensions
2. ✅ Install tooling: .NET 10 SDK (10.0.403), Angular CLI (22.2.5), WSL Ubuntu, Docker Desktop WSL integration
3. ✅ Infra: Swarm init, `labnet` overlay network, registry, Portainer (`bash scripts/bootstrap.sh`)
4. Try each component alone (guide §9: Docker, Swarm, Traefik, SQL Server, Redis, ...). Optional: do these by hand any time
5. ✅ Solution skeleton: shared protos, customers-api (SQL Server + gRPC server); image `localhost:5000/customers-api:1`
6. ✅ Redis, billing-api, orders-api (gRPC client + Redis cache), documents-api (MinIO), notification-worker (Mailpit + MongoDB)
7. ✅ Angular `web-portal`: Orders, order details, Customers, Invoices, Documents, Activity, System. Each page has a "How this page works" box, and every page has a request inspector
8. ✅ Full Swarm stack behind Traefik (:8000), `infra/versions.env`, scripts: bootstrap, build, deploy, deploy-service, ci, status, logs, trace, sql, load. Health checks, plus an 8 s drain on shutdown so scaling and rolling updates don't drop requests
9. ✅ Autoscaler + `infra/autoscale.yml` + unit tests: measured 1 → 2 → 3 replicas under load, then back to 1 (min) when idle, with 0 failed requests
10. ✅ Clear names, logging with trace ids, the course in [exercises.md](exercises.md) ← start here
