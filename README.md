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

## First-time setup on a new machine (Windows 10/11)

You need at least **8 GB RAM** (16 GB is more comfortable) and about 20 GB of free disk. Do the steps in order. Each one ends with a check.

| # | Install | How | Check (new terminal) |
|---|---|---|---|
| 1 | **WSL 2 + Ubuntu** | Admin PowerShell: `wsl --install -d Ubuntu`, reboot if asked, then choose a Linux username and password | `wsl -l -v` lists Ubuntu, VERSION 2 |
| 2 | **Docker Desktop** | https://www.docker.com/products/docker-desktop. Keep "Use WSL 2 based engine" ticked | `docker version` shows a Client and a Server |
| 3 | **Docker for Ubuntu** | Docker Desktop → Settings → Resources → **WSL Integration** → switch **Ubuntu** on → Apply & Restart | `wsl -d Ubuntu -- docker version` works |
| 4 | **Git** | https://git-scm.com/download/win | `git --version` |
| 5 | **.NET 10 SDK** | `winget install Microsoft.DotNet.SDK.10` | `dotnet --list-sdks` shows 10.0.x |
| 6 | **Node.js 24 LTS** | https://nodejs.org (or `winget install OpenJS.NodeJS.LTS`) | `node -v` shows v24.x |
| 7 | **Angular CLI** | `npm install -g @angular/cli` | `ng version` |
| 8 | **VS Code** | https://code.visualstudio.com. Open the repo folder, then click **Install** when VS Code offers the recommended extensions (listed in `.vscode/extensions.json`: C# Dev Kit, Docker, WSL, Angular, proto3, YAML, REST Client, Database Client, GitLens, ESLint, Prettier) | Extensions view → "Recommended" all installed |

Optional, if Docker runs out of memory: create `%UserProfile%\.wslconfig` with these two lines, then run `wsl --shutdown` and restart Docker Desktop.

```ini
[wsl2]
memory=5GB
```

## Start the lab

1. **Get the code:**
   ```powershell
   git clone https://github.com/Abdulrehman-Malik/LAB1.git D:\LAB1
   ```
   Any folder works. Below, `D:\LAB1` becomes `/mnt/d/LAB1` inside Ubuntu, and `C:\code\LAB1` would become `/mnt/c/code/LAB1`.
2. **Open an Ubuntu (WSL) terminal**: in VS Code, open a terminal and pick "Ubuntu (WSL)" from the **+** dropdown, or run `wsl`. Then:
   ```bash
   cd /mnt/d/LAB1
   bash scripts/bootstrap.sh     # Swarm, network, NEW random passwords (infra/.env) + secrets, image registry, Portainer
   bash scripts/build.sh all     # build all 7 images, push to localhost:5000 (first time: 15-30 min, downloads a lot)
   bash scripts/deploy.sh        # deploy everything; ends with OK/FAIL per service
   bash scripts/status.sh        # every service + a health check through Traefik (all should say 200)
   ```
3. Open **http://localhost:8000**, place an order, and follow [docs/exercises.md](docs/exercises.md) from **M0**.

`infra/.env` is created by `bootstrap.sh` on your machine and is never committed. The passwords for Portainer, SQL Server, MinIO and MongoDB are in it.

**If something fails:** `bash scripts/status.sh`, then `bash scripts/logs.sh errors`, then `docker service ps lab_<service> --no-trunc` (the error column).
A failed `build.sh` is usually a network hiccup while downloading packages: run the same command again.

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
