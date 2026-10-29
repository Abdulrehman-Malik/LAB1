# LAB1: .NET Microservices Lab

A small but complete microservices system for learning. It has everything a real one has: services, gRPC, a gateway,
SQL, cache, file storage, a document database, a background worker, containers, Swarm, autoscaling, CI and logs.
It all runs on one Windows laptop with Docker Desktop.

| Read this | When |
|---|---|
| **This README** | Start here: what's in the box, how to start it, daily commands |
| [docs/code-walkthrough.md](docs/code-walkthrough.md) | How the code is organised and how one request travels through it |
| [docs/exercises.md](docs/exercises.md) | **The course**: 16 modules + a capstone, from first deploy to autoscaling and debugging |
| [docs/lab-decisions.md](docs/lab-decisions.md) | Why things are the way they are (versions, ports, memory budget) |

## What's in the box

```
Browser ──► :8000 gateway-traefik ─┬─ /                   web-portal           Angular UI (Nginx)
                                   ├─ /api/customers      customers-api        REST + gRPC server ─┐
                                   ├─ /api/billing        billing-api          REST + gRPC server ─┤ db-sqlserver
                                   ├─ /api/orders         orders-api           REST, gRPC client ──┤ (SQL Server 2022 Express)
                                   │                                           + cache-redis       │
                                   ├─ /api/documents      documents-api        REST ───────────────┘ + storage-minio (files)
                                   └─ /api/notifications  notification-worker  background job ─► mail-mailpit (SMTP) + db-mongodb (audit)
autoscaler ── Docker API ── scales the *-api services by CPU (infra/autoscale.yml), never below 1 replica
ops stack: image-registry (:5000), portainer-ui (:9443)
```

Every Swarm service is called `lab_<name>` (application) or `ops_<name>` (tools). Infrastructure names say
**role-tool** (`db-sqlserver`, `cache-redis`, `storage-minio`, `mail-mailpit`, `gateway-traefik`), and each replica's
hostname is `<name>-<slot>` (`orders-api-2`). See what each one does:

```bash
docker service ls
docker service inspect lab_orders-api --format '{{index .Spec.Labels "lab.description"}}'
```

## Web pages

| URL | What |
|---|---|
| http://localhost:8000 | **The portal.** Every page has a "How this page works" box, and a request inspector at the bottom |
| http://localhost:8088 | Traefik dashboard: routes and the replicas behind them |
| https://localhost:9443 | Portainer (user `admin`, password `PORTAINER_ADMIN_PASSWORD` in `infra/.env`) |
| http://localhost:9001 | MinIO console (user `labadmin`, password `MINIO_ROOT_PASSWORD`) |
| http://localhost:8025 | Mailpit: every email the system sends |
| `localhost:1433` / `:6379` / `:27017` | SQL Server (`sa`), Redis, MongoDB (`labadmin`), for the **Database Client** VS Code extension |

## Start from scratch

Requirements (already installed on this machine): Docker Desktop with WSL integration for Ubuntu, .NET 10 SDK, Node 24, Angular CLI.

Open an **Ubuntu (WSL)** terminal: in VS Code, open a terminal and pick "Ubuntu (WSL)", or run `wsl`. Then:

```bash
cd /mnt/d/LAB1
bash scripts/bootstrap.sh     # Swarm, network, passwords + secrets, image registry, Portainer
bash scripts/build.sh all     # build all 7 images, push to localhost:5000 (first time: slow)
bash scripts/deploy.sh        # deploy the whole application from infra/stack.yml
bash scripts/status.sh        # every service + a health check through Traefik
```

## Daily commands (Ubuntu terminal, in /mnt/d/LAB1)

| Goal | Command |
|---|---|
| What's running? | `bash scripts/status.sh` |
| Follow one service's logs | `bash scripts/logs.sh orders-api` |
| All warnings/errors, last 30 min | `bash scripts/logs.sh errors` |
| Follow one request across services | `bash scripts/trace.sh <trace-id>` (trace id from the portal's request inspector) |
| Ship a code change (test, build, deploy, smoke test) | `bash scripts/ci.sh orders-api` |
| Build / deploy one service by hand | `bash scripts/build.sh orders-api 5` then `bash scripts/deploy-service.sh orders-api 5` |
| Roll back | `docker service rollback lab_orders-api` |
| Load test (watch autoscaling) | `bash scripts/load.sh 120 40` + `docker service logs -f lab_autoscaler` |
| Query SQL | `bash scripts/sql.sh -d SalesDb -Q "SELECT TOP 5 * FROM orders.Orders ORDER BY Id DESC"` |
| Redeploy everything from Git | `bash scripts/deploy.sh` |

**Without Docker (fast inner loop),** in PowerShell from the repo root:

```powershell
. .\scripts\dev-env.ps1                    # loads passwords from infra/.env
dotnet run --project src/customers-api     # :5101 REST, :5102 gRPC
dotnet run --project src/orders-api        # :5301, uses SQL/Redis from the running stack
cd src/web-portal; ng serve                # http://localhost:4200, proxies /api to :8000
dotnet test                                # unit tests (autoscaler rules + autoscale.yml checks)
```

## Folder map

| Path | What |
|---|---|
| `src/shared-protos/` | gRPC contracts (`customers.proto`, `billing.proto`) |
| `src/shared/Lab.Shared/` | Code every service shares: secrets, ports, log format, request logging, DB provider switch, migrations, graceful shutdown |
| `src/<service>/` | One folder per service: `Program.cs`, `Dockerfile`, `appsettings*.json`, a `.http` test file |
| `src/web-portal/` | Angular app (`src/app/*.ts`, one file per page) |
| `src/autoscaler/` | The autoscaler (`ScalingDecision.cs` is the rule, `DockerClient.cs` the Docker API) |
| `tests/Lab.Tests/` | Unit tests |
| `infra/` | `stack.yml` (the app), `ops.yml` (tools), `versions.env` (deployed tags), `autoscale.yml` (scaling rules) |
| `scripts/` | Everything you run |
