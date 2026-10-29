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

| # | Install | Why the lab needs it | How | Check (new terminal) |
|---|---|---|---|---|
| 1 | **WSL 2 + Ubuntu** | A real Linux inside Windows. Docker runs its containers in WSL 2, and every lab script in `scripts/` is a bash script you run from the Ubuntu terminal | Admin PowerShell: `wsl --install -d Ubuntu`, reboot if asked, then choose a Linux username and password | `wsl -l -v` lists Ubuntu, VERSION 2 |
| 2 | **Docker Desktop** | Builds the images and runs every container. Its engine also provides **Docker Swarm**, the orchestrator that runs the `lab` and `ops` stacks, restarts crashed containers and does rolling updates | https://www.docker.com/products/docker-desktop. Keep "Use WSL 2 based engine" ticked | `docker version` shows a Client and a Server |
| 3 | **Docker for Ubuntu** | Lets the `docker` command inside Ubuntu talk to Docker Desktop's engine, so `bash scripts/*.sh` can build, deploy and read logs | Docker Desktop → Settings → Resources → **WSL Integration** → switch **Ubuntu** on → Apply & Restart | `wsl -d Ubuntu -- docker version` works |
| 4 | **Git** | Gets the code and tracks your changes. In this lab Git is also the **source of truth for what runs**: `infra/stack.yml` and `infra/versions.env` describe the deployed system | https://git-scm.com/download/win | `git --version` |
| 5 | **.NET 10 SDK** | Builds, runs and tests the C# services on Windows (`dotnet build`, `dotnet run`, `dotnet test`), and creates EF Core migrations (`dotnet ef`). The Docker images bring their own SDK, so this is for local development | `winget install Microsoft.DotNet.SDK.10` | `dotnet --list-sdks` shows 10.0.x |
| 6 | **Node.js 24 LTS** | Runs the Angular tooling and `npm`, which installs the portal's packages (`src/web-portal/package.json`) | https://nodejs.org (or `winget install OpenJS.NodeJS.LTS`) | `node -v` shows v24.x |
| 7 | **Angular CLI** | The `ng` command: `ng serve` runs the portal at http://localhost:4200 with live reload while you edit it, and `ng build` compiles it | `npm install -g @angular/cli` | `ng version` |
| 8 | **VS Code** | The editor. With the extensions below it understands every file type in the lab and can talk to the containers and databases | https://code.visualstudio.com. Open the repo folder, then click **Install** when VS Code offers the recommended extensions | Extensions view → "Recommended" all installed |

**The recommended VS Code extensions** (listed in `.vscode/extensions.json`) and what each one is for:

| Extension | What it does for you here |
|---|---|
| **C# Dev Kit** (`ms-dotnettools.csdevkit`) | C# IntelliSense, Solution Explorer for `LAB1.slnx`, run and debug a service with F5, run the unit tests in `tests/Lab.Tests` |
| **Container Tools** (`ms-azuretools.vscode-containers`) | Sidebar view of images, containers, Swarm services and their logs; syntax help in `Dockerfile` and `stack.yml` |
| **WSL** (`ms-vscode-remote.remote-wsl`) | Opens Ubuntu terminals inside VS Code, where the `bash scripts/...` commands run |
| **Angular Language Service** (`angular.ng-template`) | Autocomplete and error checking inside the portal's HTML templates (`src/web-portal/src/app/*.ts`) |
| **vscode-proto3** (`zxh404.vscode-proto3`) | Syntax highlighting and checks for the gRPC contracts in `src/shared-protos/*.proto` |
| **YAML** (`redhat.vscode-yaml`) | Validation and autocomplete for `infra/stack.yml`, `infra/ops.yml` and `infra/autoscale.yml`; catches indentation mistakes before you deploy |
| **REST Client** (`humao.rest-client`) | Click **Send Request** in the `*.http` file next to each service to call its API without Postman |
| **Database Client** (`cweijan.vscode-mysql-client2`) | Browse and query SQL Server (`localhost,1433`), Redis (`:6379`) and MongoDB (`:27017`) from the sidebar: see the tables, cache keys and audit documents the services write |
| **GitLens** (`eamodio.gitlens`) | Shows who changed each line and why (blame, history), useful for following how the lab evolved |
| **ESLint** (`dbaeumer.vscode-eslint`) | Flags mistakes in the portal's TypeScript |
| **Prettier** (`esbenp.prettier-vscode`) | Formats TypeScript, HTML, CSS and JSON consistently on save |

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
