# LAB1 Course: from zero to "I understand every part"

For a developer who has never worked on this architecture. After these modules you should be able to explain each part,
change it, deploy the change, prove the impact, and debug it with logs.

**Before you start:** the system is running (`bash scripts/status.sh` shows 200 everywhere; see [README](../README.md)).
Use two terminals:
- an **Ubuntu (WSL)** terminal in `/mnt/d/LAB1`. Commands in `bash` blocks run here.
- a **PowerShell** terminal in `D:\LAB1`. Commands in `powershell` blocks run here.

Each module has: **Goal → Do → You should see → Why it matters → Check yourself** (answers hidden under "Answers").
When you change code, always ship it with a **new tag** (`bash scripts/ci.sh <service>` picks one for you).

| # | Module | Main parts |
|---|---|---|
| M0 | Orientation | stack, names, tools |
| M1 | Images and containers | Dockerfile, layers, registry |
| M2 | Swarm: services, replicas, self-healing | Swarm |
| M3 | Networking and the gateway | overlay network, Traefik |
| M4 | Frontend change, end to end | Angular, Nginx |
| M5 | gRPC contracts | protobuf, backward compatibility |
| M6 | Database changes | EF Core migrations, shared database |
| M7 | Caching | Redis, cache-aside, stale data |
| M8 | File storage | MinIO, two stores in sync |
| M9 | Background work | worker, SMTP, MongoDB, atomic claim |
| M10 | Configuration and secrets | appsettings, env vars, Swarm secrets |
| M11 | Logging and debugging | log format, trace ids, log levels |
| M12 | Deployment | tags, rolling update, rollback, CI |
| M13 | Autoscaling | autoscale.yml, load tests, tests for config |
| M14 | Failure drills | what breaks when X dies |
| M15 | Drift | Git vs Portainer |
| ★ | Capstone | a feature across 4 services |

---

## M0. Orientation

**Goal:** know what runs, where, and what each piece is for.

**Do**
```bash
bash scripts/status.sh
docker service ls
for s in $(docker stack services lab --format '{{.Name}}'); do
  printf '%-26s %s\n' "$s" "$(docker service inspect "$s" --format '{{index .Spec.Labels "lab.description"}}')"
done
```
Open every page listed by `status.sh`. In the portal, open **System** and read the diagram. Click **Check all**.

**You should see:** 13 `lab_*` services and 3 `ops_*` services, each with a one-line description. All health checks green.

**Why it matters:** names follow a convention, so you can guess what a service does: `<name>-api`, `<name>-worker`,
`<role>-<tool>` (`db-sqlserver`, `cache-redis`, ...). `lab` = the application, `ops` = tools used to run it.

**Check yourself**
1. Which service would you look at if the inbox at :8025 shows no emails?
2. Which services can the browser reach directly, and which only through other services?

<details><summary>Answers</summary>

1. `lab_notification-worker` (it sends them) and `lab_mail-mailpit` (it receives them).
2. The browser only reaches `gateway-traefik` (:8000), plus the tool UIs. Every API is behind Traefik. gRPC ports (8081), Redis and MongoDB are only used by services. Ports 1433, 6379 and 27017 are published for your dev tools only.
</details>

---

## M1. Images and containers

**Goal:** understand what an image is, how it's built, and where it's stored.

**Do**
1. Read `src/orders-api/Dockerfile`. Note the two stages, and that restore happens before the source code is copied.
2. Look at the result:
   ```bash
   curl -s localhost:5000/v2/_catalog
   curl -s localhost:5000/v2/orders-api/tags/list
   docker history localhost:5000/orders-api:3 | head -8
   docker image ls 'localhost:5000/*'
   ```
3. Run an image by hand, outside Swarm:
   ```bash
   docker run --rm --entrypoint bash localhost:5000/orders-api:3 -c 'ls /app | head; cat /etc/os-release | head -2'
   ```
4. Change one line of code (e.g. a log message in `src/orders-api/Program.cs`), then time two builds:
   ```bash
   time bash scripts/build.sh orders-api 900
   time bash scripts/build.sh orders-api 901
   ```

**You should see:** the first build after a code change skips `dotnet restore` (`CACHED`). The second build, with no change, is almost instant.

**Why it matters:** the image is the unit you deploy. The same image runs in test and in production. Layer order decides build speed.

**Check yourself**
1. Why is the SDK image not in the final image?
2. What makes the `restore` layer rebuild?

<details><summary>Answers</summary>

1. Stage 2 starts `FROM aspnet:10.0` and copies only `/app` from stage 1. Compilers and sources stay behind, so the image is smaller and has less to attack.
2. A change to a copied `.csproj` or `Directory.Build.props`. Code changes don't touch that layer.
</details>

---

## M2. Swarm: services, replicas, self-healing

**Goal:** understand *desired state*. You say what you want, and Swarm keeps it true.

**Do**
```bash
docker service ps lab_customers-api                               # tasks = replicas, with history
docker service scale lab_customers-api=2
docker service ps lab_customers-api
docker ps --format '{{.Names}}  {{.Status}}' | grep customers     # see (health: starting) -> (healthy)
```
In the portal **System** page, click **Call ×10** on customers-api: you'll see `customers-api-1` and `customers-api-2` answering.

Self-healing: kill one container and watch.
```bash
docker rm -f $(docker ps -q -f name=lab_customers-api. | head -n 1)
watch -n 1 docker service ps lab_customers-api        # Ctrl+C when a new task is Running
```

**You should see:** a task marked `Failed`/`Shutdown` and a new one `Running`. Within about 15 s the autoscaler also logs it
(and may scale back to 1, since there's no load: `docker service logs lab_autoscaler --since 2m`).

**Why it matters:** you never "start a container" in production. You declare a service, and Swarm (and here, the autoscaler) keeps it at the right count.

**Check yourself**
1. You scaled to 2. Why may it go back to 1 by itself?
2. What's the difference between a service, a task and a container?

<details><summary>Answers</summary>

1. The autoscaler sees CPU below `scaleDownBelowPercent` and removes one replica (never below `min: 1`).
2. Service = the desired state ("run image X, N copies"). Task = one scheduled copy (has a slot number and history). Container = the running process for a task, on one node.
</details>

---

## M3. Networking and the gateway

**Goal:** see how services find each other, and how Traefik decides where a URL goes.

**Do**
1. Service discovery by name on the `labnet` overlay network:
   ```bash
   docker run --rm --network labnet curlimages/curl -s http://customers-api:8080/api/customers/1
   docker run --rm --network labnet curlimages/curl -s -i http://orders-api:8080/api/orders/health
   ```
2. Open the Traefik dashboard (http://localhost:8088) → **HTTP Routers**. Find `orders@swarm`: rule `PathPrefix(/api/orders)`.
3. Compare with the labels in `infra/stack.yml` under `orders-api` → `deploy.labels`.
4. Look at the gateway's access log while you click in the portal:
   ```bash
   docker service logs --raw --tail 10 lab_gateway-traefik
   ```

**You should see:** the response headers include `X-Served-By: orders-api-1`. Each access-log line has the path, the status and the backend URL.

**Why it matters:** routing is configuration (labels), not code. A new API only needs `traefik.*` labels to be reachable at `/api/...`.

**Check yourself**
1. Why does `/api/orders/5` go to orders-api and not to web-portal, whose rule is `PathPrefix(/)`?
2. Why can't your browser call `http://customers-api:8081`?

<details><summary>Answers</summary>

1. Traefik gives longer rules higher priority by default. `/api/orders` is longer than `/`.
2. `customers-api` is a name on the internal `labnet` network, and port 8081 isn't published. Only services on labnet can reach it.
</details>

---

## M4. Frontend change, end to end

**Goal:** change the UI, try it with hot reload, then ship it as a container.

**Do**
1. Inner loop: in PowerShell run `cd src\web-portal; ng serve`, then open http://localhost:4200. API calls go through `proxy.conf.json` to :8000.
2. In `src/web-portal/src/app/orders.ts`, add a **Unit price** column to the table (header + `{{ o.unitPrice | currency: 'PKR ' }}`). Save: the page reloads.
3. Ship it:
   ```bash
   bash scripts/ci.sh web-portal
   ```
4. Open http://localhost:8000 (not :4200) and check the new column.

**You should see:** `ci.sh` runs tests, builds `web-portal:<next tag>`, does a rolling update, and smoke-tests `/`. `infra/versions.env` now has the new tag.

**Why it matters:** the Angular app is just static files in an Nginx container. It only knows relative URLs (`/api/...`), so the same build works anywhere behind the gateway.

**Check yourself**
1. Where would you look if the page loads but every table is empty?
2. Why does `nginx.conf` have `try_files ... /index.html`?

<details><summary>Answers</summary>

1. The request inspector at the bottom: status codes, and which replica answered. Then `bash scripts/logs.sh <service>` or `trace.sh <id>`.
2. A deep link like `/orders/5` is not a file. Nginx must return `index.html` so Angular's router can show the page.
</details>

---

## M5. gRPC contracts

**Goal:** call a gRPC service by hand, and change a contract without breaking callers.

**Do**
1. Call customers-api's gRPC server directly:
   ```bash
   docker run --rm --network labnet -v /mnt/d/LAB1/src/shared-protos:/protos fullstorydev/grpcurl:v1.9.3 \
     -plaintext -import-path /protos -proto customers.proto -d '{"id": 1}' \
     customers-api:8081 customers.v1.CustomerService/GetCustomer
   ```
   Try `"id": 999` and read the `NotFound` status.
2. Contract change, **server first**: add `string phone = 5;` to `CustomerReply` in `src/shared-protos/customers.proto`.
   In `customers-api`, add `public string? Phone { get; set; }` to `Data/Customer.cs` and `Phone = customer.Phone ?? ""` in
   `Grpc/CustomerGrpcService.cs`. Create the migration (PowerShell):
   ```powershell
   dotnet ef migrations add AddCustomerPhone --project src/customers-api --output-dir Data/Migrations
   ```
   Ship only customers-api: `bash scripts/ci.sh customers-api`. Place an order in the portal: everything still works,
   because orders-api (old contract) ignores field 5.
3. **Clients second**: add `string Phone` to `CustomerInfo` in `src/orders-api/OrderDetailsService.cs`, fill it from `c.Phone`,
   show it in `order-detail.ts`, then `bash scripts/ci.sh orders-api` and `bash scripts/ci.sh web-portal`.

**You should see:** the grpcurl output now includes `"phone"` (empty for old customers). The order details page shows it after the last deploy.

**Why it matters:** gRPC is fast and typed, but both sides compile the same `.proto`. Safe changes add fields with **new numbers**.
Never renumber or reuse a number. Deploy the server before the clients.

**Check yourself**
1. What breaks if you change `string email = 3;` to `string email = 6;`?
2. Why deploy customers-api first?

<details><summary>Answers</summary>

1. Old clients still read field 3, which is now empty, and new clients read 6 from old servers, also empty. Numbers are the wire format, and names don't matter on the wire.
2. A new client asking an old server would just get an empty `phone`, which is harmless here. In general the server must understand new fields before any client sends or expects them.
</details>

---

## M6. Database changes

**Goal:** see how schema changes ship with code, and the risk of a shared database.

**Do**
1. Look at the current state:
   ```bash
   bash scripts/sql.sh -Q "SELECT name FROM sys.databases"
   bash scripts/sql.sh -d SalesDb -Q "SELECT TABLE_SCHEMA, TABLE_NAME FROM INFORMATION_SCHEMA.TABLES ORDER BY 1,2"
   bash scripts/sql.sh -d SalesDb -Q "SELECT * FROM orders.__EFMigrationsHistory; SELECT * FROM billing.__EFMigrationsHistory"
   ```
2. If you did M5, check that `Phone` exists: `bash scripts/sql.sh -d CustomersDb -Q "SELECT TOP 3 Id, Name, Phone FROM dbo.Customers"`.
   The migration ran **at container startup** (`MigrateDatabaseAsync` in `Lab.Shared/DatabaseExtensions.cs`). No one ran SQL by hand.
3. Notice the order ids: after SQL Server restarts, ids can jump by 1000 (e.g. 3 → 1003). SQL Server pre-allocates identity values in blocks
   and loses the unused ones on restart. Ids are unique, not gap-free: never use them as invoice numbers that must be consecutive.
4. Shared-table risk: `notification-worker` reads `billing.Invoices` with plain SQL (`InvoiceEmailWorker.cs`).
   Imagine billing-api renames `EmailSent`. Find every place that would break: `grep -rn EmailSent src/`.

**You should see:** two services, two schemas, two migration histories in one database. The worker's raw SQL isn't checked by any compiler.

**Why it matters:** migrations in Git = repeatable, reviewable schema history. Shared tables couple deployments: you'd have to
change billing-api and notification-worker together.

**Check yourself**
1. Two replicas of orders-api start at the same time. Who applies the migration?
2. How would you rename a column without downtime?

<details><summary>Answers</summary>

1. Both try. EF Core takes a lock on the migrations history, so one applies it and the other finds nothing to do. Retry logic covers startup races.
2. Expand/contract: add the new column, write to both, deploy readers of the new column, backfill, then drop the old column in a later release.
</details>

---

## M7. Caching

**Goal:** see cache-aside in action: hits, misses, stale data, and life without Redis.

**Do**
1. Open an order's details and click **Reload** a few times: `Redis MISS` then `Redis HIT`.
2. Look inside Redis:
   ```bash
   R=$(docker ps -q -f name=lab_cache-redis.)
   docker exec $R redis-cli --scan --pattern 'orders-api:*'
   docker exec $R redis-cli TTL orders-api:order:1
   docker exec $R redis-cli HGETALL orders-api:order:1 | head -4
   ```
3. Stale data: pay the invoice on the details page, then reload. It still says Pending until the TTL expires.
4. Make the TTL configurable. In `OrderDetailsService.cs`, replace the fixed 60 s with a value read from `IConfiguration`
   (`Cache:Seconds`, default 60). In `infra/stack.yml` under `orders-api.environment`, add `Cache__Seconds: "10"`.
   Ship it: `bash scripts/ci.sh orders-api`, then `bash scripts/deploy.sh` (the env var lives in the stack file).
5. Redis down: `docker service scale lab_cache-redis=0`, use the portal, then `bash scripts/logs.sh orders-api 20`. Restore it with `docker service scale lab_cache-redis=1`.

**You should see:** TTL counts down from 60 (or 10 after step 4). With Redis down, pages still work: always `MISS`, slower, with a `WARN ... Redis read failed` log line.

**Why it matters:** a cache trades freshness for speed. It must never be the source of truth, and losing it must not break the app.

**Check yourself**
1. Why does orders-api not cache details when billing-api is down?
2. What two ways could fix the "paid but shows Pending" problem?

<details><summary>Answers</summary>

1. It would store an incomplete answer ("no invoice") for 60 s, after billing came back. See the `if (details.Customer is not null && details.Invoice is not null)` check.
2. A shorter TTL, or cache invalidation: delete `orders-api:order:<id>` when the invoice changes (billing would need to tell orders, e.g. via an event).
</details>

---

## M8. File storage

**Goal:** understand object storage and why two stores must stay in sync.

**Do**
1. On **Documents**, upload any file with order id 1. Note its object key.
2. Open the MinIO console (http://localhost:9001, user `labadmin`, password `MINIO_ROOT_PASSWORD` from `infra/.env`) → bucket `documents`, and find the same key.
3. Break the sync: delete that object **in the MinIO console**, then click the file name in the portal.
4. Read the log: `bash scripts/logs.sh documents-api 20`.
5. Check the SQL side: `bash scripts/sql.sh -d DocumentsDb -Q "SELECT Id, FileName, ObjectKey FROM dbo.Documents"`.

**You should see:** a 404 with "exists in SQL but its file is missing from MinIO", and a matching `WARN` log line.

**Why it matters:** databases hold small structured data; object stores hold big files. A backup or restore must cover **both** at the same point in time.

**Check yourself**
1. What does documents-api do if saving the SQL row fails after the upload?
2. Why `ForcePathStyle = true` in `FileStorage.cs`?

<details><summary>Answers</summary>

1. It deletes the just-uploaded object, so no orphan file remains (see the `try/catch` in `Program.cs` MapPost).
2. MinIO serves buckets as paths (`host/bucket/key`). AWS defaults to virtual hosts (`bucket.host/key`), which would need DNS for each bucket.
</details>

---

## M9. Background work

**Goal:** follow work that no user triggers directly.

**Do**
1. Watch the worker: `bash scripts/logs.sh notification-worker`, then place an order in the portal.
2. Within 30 s: a `Claimed 1 invoice(s)` and an `Emailed invoice` line. Check the inbox at http://localhost:8025 and the portal's **Activity** page.
3. Read the audit in MongoDB directly:
   ```bash
   source infra/.env
   docker exec $(docker ps -q -f name=lab_db-mongodb.) mongosh -u labadmin -p "$MONGO_ROOT_PASSWORD" \
     --authenticationDatabase admin lab --quiet --eval 'db.audit.find().sort({at:-1}).limit(3)'
   ```
4. Failure and retry: `docker service scale lab_mail-mailpit=0`, place an order, and wait 30 s. Activity shows `InvoiceEmailFailed`.
   Then `docker service scale lab_mail-mailpit=1`: the next run sends it.
5. Two workers: `docker service scale lab_notification-worker=2`, place 5 orders quickly, and check that each invoice got exactly **one** email in Mailpit.
   Then scale back to 1.

**You should see:** each claimed invoice is handled by exactly one replica (look at the **Worker** column on Activity).

**Why it matters:** `UPDATE TOP (10) ... OUTPUT ... WHERE EmailSent = 0` is atomic. Two workers can never claim the same row.
Without it, scaling the worker would send duplicate emails.

**Check yourself**
1. Why is notification-worker not in `autoscale.yml`?
2. The worker marks `EmailSent = 1` *before* sending. What protects against losing an email if sending fails?

<details><summary>Answers</summary>

1. Its load is a timer, not requests: more replicas don't make it faster in a useful way. One is enough, and the claim makes extra replicas safe anyway.
2. On failure it sets `EmailSent = 0` again (release) and writes an `InvoiceEmailFailed` audit event, so the next run retries. A crash *between* claim and send could still lose one; a production version would add a "claimed at" timestamp and re-claim stale claims.
</details>

---

## M10. Configuration and secrets

**Goal:** know where every setting comes from, and keep passwords out of plain sight.

**Do**
1. Order of precedence (later wins): `appsettings.json` → `appsettings.{Environment}.json` → environment variables → files in `/run/secrets` (`AddKeyPerFile`).
   Open `src/orders-api/appsettings.json` and `appsettings.Development.json`: Swarm values versus localhost values.
2. Env vars are visible to anyone with Docker access:
   ```bash
   docker service inspect lab_orders-api --format '{{json .Spec.TaskTemplate.ContainerSpec.Env}}'
   ```
3. Secrets aren't:
   ```bash
   docker secret ls
   docker secret inspect mssql_sa_password          # no value shown
   docker exec $(docker ps -q -f name=lab_orders-api. | head -1) ls -l /run/secrets/
   ```
4. Change a setting without touching code: in `stack.yml`, under `notification-worker.environment`, change `Worker__IntervalSeconds` from `"30"` to `"10"`,
   then run `bash scripts/deploy.sh` and `bash scripts/logs.sh notification-worker 5`. You'll see `Checking for new invoices every 10 s`. Set it back afterwards.

**You should see:** `/run/secrets/Database__Password` inside the container. `__` in a name means `:` (a nested setting).

**Why it matters:** the same image runs everywhere. Only config differs. Secrets never go in Git, YAML or env vars.

**Check yourself**
1. How does `Database__Password` become `Database:Password` in .NET?
2. Where are the real passwords stored on this machine?

<details><summary>Answers</summary>

1. `AddKeyPerFile("/run/secrets")` turns each file into a setting, and `__` is the hierarchy separator, like in env vars.
2. In `infra/.env` (git-ignored, generated by bootstrap.sh) and, encrypted, in the Swarm's secret store.
</details>

---

## M11. Logging and debugging

**Goal:** find out what happened to one request, across every service.

**The log format** (all .NET services, `Lab.Shared/LabConsoleFormatter.cs`):
```
10:41:07.123 INFO  trace=4bf92f3577b34da6a3ce929d0e0e4736 Request: POST /api/orders -> 201 in 48 ms
time (UTC)   level trace id (same in every service)        category: message
```

**Do**
1. Place an order. In the **request inspector** (bottom of the page), click the trace id of the `POST /api/orders` row to copy it.
2. Run: `bash scripts/trace.sh <trace-id>`. You get lines from **orders-api, customers-api and billing-api**, in time order:
   the REST request, the gRPC calls arriving (`POST /customers.v1.CustomerService/GetCustomer`), the invoice creation and the order log lines.
3. More detail: in `infra/stack.yml` set `Logging__LogLevel__Default: Debug` for orders-api and run `bash scripts/deploy.sh`.
   Open an order twice: now you see `Cache MISS ...` and `Cache HIT ...`. Set it back to `Information` and redeploy.
4. Errors everywhere: `bash scripts/logs.sh errors`.
5. Debug a broken config. Break orders-api on purpose, *outside Git*:
   ```bash
   docker service update --env-add Services__Billing=http://billing-api:9999 lab_orders-api
   ```
   Place an order. It's saved as **Created**, not Invoiced. Find out why using only the inspector, `trace.sh` and `logs.sh`.
   Fix it: `docker service update --env-rm Services__Billing lab_orders-api`.
6. Other log sources: the gateway (`docker service logs --raw --tail 20 lab_gateway-traefik`), the autoscaler
   (`docker service logs -f lab_autoscaler`), and Portainer → Services → a service → **Service logs**.

**You should see** (step 5): `WARN ... Order N saved, but billing-api failed: Status(StatusCode="Unavailable" ...)` under the same trace id as the request.
Every 30 s, `InvoiceRetryWorker` also logs `Retry: order N still not invoiced`, under its *own* trace id. After the fix, it logs `is now invoiced`.

**A real case from building this lab:** the first order after a deploy came back **Created**. `trace.sh` showed, under one id,
orders-api `gRPC call deadline exceeded` after 5 s, and billing-api `CreateInvoice ... -> 200 in 8192 ms`. The cause was a cold start:
JIT and EF Core model building inside a 0.5-CPU limit. The fix was the retry job, not a longer timeout.

**Why it matters:** in microservices one click touches several processes. A shared trace id is the thread that ties their logs together.
It comes from the W3C `traceparent` header, which ASP.NET Core and gRPC pass along automatically.

**Check yourself**
1. Why does the worker's trace id never appear in a browser request?
2. Where would you start if users report "slow order details"?

<details><summary>Answers</summary>

1. The worker starts its own trace for each run (`new Activity("InvoiceEmailRun")`). No browser request is involved. Its trace id is saved in each MongoDB audit event instead.
2. The inspector's `ms` column and `X-Cache` (HIT or MISS), then `trace.sh` on a slow request: the request log line on each service shows its duration, so you see which hop is slow.
</details>

---

## M12. Deployment

**Goal:** ship changes safely, see a rolling update, and recover from a bad release.

**Do**
1. A normal release:
   ```bash
   bash scripts/ci.sh billing-api        # tests -> build tag N+1 -> rolling update -> smoke test
   git diff infra/versions.env           # the new tag is recorded
   ```
   In a second terminal during the update: `watch -n 1 'docker ps --format "{{.Names}} {{.Status}}" | grep billing'`.
   The new container reaches `healthy` *before* the old one stops (`order: start-first`).
2. Rollback by hand: `docker service rollback lab_billing-api`, then put the old tag back in `versions.env`.
3. A broken release: in `src/billing-api/Program.cs`, add `throw new Exception("boom");` right after `var app = builder.Build();`.
   Run `bash scripts/ci.sh billing-api`.
   - Swarm starts the new task, it crashes, and the update fails, so Swarm **rolls back automatically** (`failure_action: rollback`).
   - `deploy-service.sh` notices, restores `versions.env`, and exits with an error. The portal kept working the whole time.
   - Remove the `throw`.
4. Full redeploy from Git: `bash scripts/deploy.sh`. It applies `stack.yml` + `versions.env`, which is what CD would do.

**You should see** (step 3): `update rolled back due to failure` and `Update to N FAILED and Swarm rolled back`.

**A real case from building this lab (a silent rollback):** after a full redeploy, orders-api was quietly back on the *old* tag.
`docker service ps lab_orders-api` and `docker inspect <task-id>` told the story:
the new task was created at 11:20:01, its first log line was at 11:21:10, and it was listening at 11:21:20.
With 5 services restarting at once on 0.5 CPU each, it was **80 s** to start, but the health check allowed only 60 s (`start_period`),
so Swarm marked it unhealthy, stopped it (state `complete`), and rolled back. Two lessons:
- size `start_period` for the worst case (a full redeploy), not a single quiet restart. It's now 120 s.
- never trust "deploy finished" without checking. `deploy.sh` now waits for updates and prints OK/FAIL per service against `versions.env`.
Try it: in `x-health-timing` set `start_period: 5s`, `interval: 3s`, `retries: 1`, then `bash scripts/ci.sh customers-api`.
The new replica can't be healthy that fast, so Swarm rolls back and the script prints why. Restore the values afterwards.

**Why it matters:** numbered tags + start-first + health checks + automatic rollback = releases with no downtime and an undo button.
`versions.env` in Git is the record of what's live.

**Check yourself**
1. Why never deploy the tag `latest`?
2. What makes the new replica "ready" before traffic reaches it?

<details><summary>Answers</summary>

1. You can't tell which code it is, can't roll back to "the previous latest", and two nodes may pull different images.
2. The `healthcheck` in `stack.yml`: the task only becomes `running` (and Traefik only routes to it) after `/health` answers `Healthy`.
</details>

---

## M13. Autoscaling

**Goal:** test the autoscaling configuration and see every rule in action.

**The rules** (`infra/autoscale.yml`): every 15 s, average CPU of each service's replicas as a % of its CPU limit
(`cpus: "0.50"` in stack.yml). Above `scaleUpAbovePercent` → +1 replica (up to `max`). Below `scaleDownBelowPercent` → −1 (down to `min`, never below 1).
The condition must hold for `sustainedChecks` checks in a row. After a change, wait `cooldownSeconds`. Replicas younger than `startupGraceSeconds` aren't measured.

**A real case from building this lab:** with `sustainedChecks` at 1, idle services were scaled up: `customers-api: CPU 126% -> scale to 2`.
`docker stats` confirmed real, short bursts with no traffic: .NET's JIT recompiling hot code, GC, health checks.
The fix was `sustainedChecks: 2`. Now the log shows `CPU high, waiting (1/2 checks)` and nothing happens unless the load lasts.
A unit test (`Every_service_ignores_single_cpu_spikes`) keeps it that way.

**Do**
1. Watch the decisions: `docker service logs -f lab_autoscaler` (one line per service per check).
2. Load test, in a second terminal: `bash scripts/load.sh 180 40`. Expect `orders-api` 1 → 2 → 3, then back to 1 a few minutes after the load stops.
   Keep the portal's **System** page open and click **Call ×10** on orders-api while it's scaled out.
   Note the `hey` summary at the end: every response should be `[200]`.
3. Change the rules. In `autoscale.yml` set orders-api to `max: 2` and `scaleUpAbovePercent: 40`. First **test the config** (PowerShell):
   ```powershell
   dotnet test
   ```
   Then deploy it: `bash scripts/deploy.sh` (the file's hash changes, so the autoscaler restarts with the new rules). Rerun the load test: it stops at 2.
4. Catch a bad config: set `scaleUpAbovePercent: 10` (below `scaleDownBelowPercent: 20`) and run `dotnet test`.
   The test `Scale_up_threshold_is_above_scale_down_threshold` fails and explains why. Revert.
5. The minimum: `docker service scale lab_billing-api=0`. Within 15 s the autoscaler logs `below min 1` and brings it back.
6. Why no errors during scaling? Set `Shutdown__DrainSeconds: "0"` in orders-api's environment, run `bash scripts/deploy.sh`, and rerun the load test.
   Look for `[502]` in the summary when replicas are removed. Remove the setting and redeploy.
7. Load a different service: `bash scripts/load.sh 120 40 /api/customers`.
8. Read the code: `src/autoscaler/ScalingDecision.cs` (the rule), `AutoscalerWorker.cs` (the loop), `DockerClient.cs` (Docker API calls),
   and `tests/Lab.Tests/*` (rule + config tests). Add a test for a case you care about.

**You should see:** log lines like `lab_orders-api: 1 replica(s), CPU 140% of limit -> CPU high, waiting (1/2 checks)`, then
`-> scale to 2 (CPU above 60% for 2 checks)`. (CPU can read above 100 % of the limit: the limit is enforced over short periods,
and the 2-second sample isn't exact. Treat it as "saturated".)

**Why it matters:** autoscaling is config plus a feedback loop. Thresholds too close together cause *flapping*, cooldowns stop overreaction,
and graceful drain + health checks make scaling invisible to users.

**Check yourself**
1. Running `bash scripts/deploy.sh` while scaled to 3: what happens to the replica count?
2. Why measure CPU against the limit and not against the whole machine?

<details><summary>Answers</summary>

1. It resets to the stack file's `replicas: 1`. Then the autoscaler scales up again if the load is still there.
2. A replica can never use more than its limit (0.5 CPU). "80 % of its limit" means "almost saturated", whatever the machine size.
</details>

---

## M14. Failure drills

**Goal:** know the blast radius of each component. For each line: break it, use the portal (Orders, order details, System), read the logs, then restore it.

| Break (`docker service scale ... =0`) | Expected impact | Restore |
|---|---|---|
| `lab_customers-api` | New orders: **503** (can't check the customer). Details: "customer unavailable". Worker emails fail and retry | `=1` |
| `lab_billing-api` | Orders saved as **Created**, details without invoice, Invoices page fails. After restore, `InvoiceRetryWorker` in orders-api invoices them within ~30–60 s (watch `bash scripts/logs.sh orders-api`: `Retry: order N is now invoiced`) | `=1` |
| `lab_cache-redis` | Everything works, always MISS, `WARN Redis` logs | `=1` |
| `lab_storage-minio` | Documents uploads/downloads fail; documents-api health becomes Unhealthy | `=1` |
| `lab_db-mongodb` | Activity page fails; worker logs errors and retries | `=1` |
| `lab_db-sqlserver` | Almost everything fails: health checks go red, then Swarm restarts the API replicas | `=1`, then `bash scripts/sql-tune.sh` |

The autoscaler brings `*-api` services back to `min: 1` by itself (in about 15 s). The infrastructure services (`db-*`, `cache-*`, ...) aren't autoscaled. You restore those yourself.

**Check yourself:** which single component is the biggest risk here, and how does the real system reduce it?

<details><summary>Answer</summary>

SQL Server: every API depends on it. Real systems run it with high availability (e.g. SQL Server Always On Availability Groups), keep backups, and avoid sharing one database between many services.
</details>

---

## M15. Drift

**Goal:** see why Git, not a UI, must be the source of truth.

**Do**
1. In **Portainer** (https://localhost:9443) → Services → `lab_web-portal` → change replicas to 2, or add an env var to `lab_customers-api`.
2. Confirm it: `docker service ls`.
3. Run `bash scripts/deploy.sh`.

**You should see:** your Portainer change disappears. The stack file in Git wins.

**Why it matters:** hand-made changes are invisible to code review, get lost on redeploy, and nobody knows the "real" config. Make every change in `infra/` and deploy it.

---

## ★ Capstone: "Discount code" feature across services

Build it alone. It touches the database, REST, gRPC, cache, UI, tests and deployment order.

**Feature:** an order can have a discount code. `LAB10` gives 10 % off. The invoice amount uses the discounted price, and the details page shows the discount.

**Checklist**
1. `orders-api`: add `DiscountCode` (nullable) and `DiscountAmount` to `Order`, plus a migration. Accept `discountCode` in `CreateOrderRequest`.
   Compute `Amount` after the discount. Log the applied discount.
2. Unit test the discount calculation (move it into a small static class, like `ScalingDecision`), in `tests/Lab.Tests`.
3. `web-portal`: a discount input on the Orders page, and the discount shown on the details page.
4. Ship in the right order with `ci.sh`: backend first (orders-api), then web-portal. Explain why that order is safe.
5. Prove it: place an order with `LAB10`, check the invoice amount (Invoices page), the email text in Mailpit, and `trace.sh` for the order.
6. Prove rollback works: deploy a deliberately broken orders-api and watch the automatic rollback.

**Done when:** you can explain, for one order, every service, data store and log line involved. Use `trace.sh` output as your evidence.

---

## Appendix: switching the database engine

The services choose their EF Core provider from `Database:Provider` (`SqlServer`, `MySql`, `Postgres`) in `Lab.Shared/DatabaseExtensions.cs`.
Switching needs more than config:
1. A database container for the new engine in `stack.yml`, with its secret.
2. **Migrations generated for that provider**. The current ones contain SQL Server types (`nvarchar`, `IDENTITY`).
   The usual approach is one migrations project per provider.
3. notification-worker's raw SQL (`UPDATE TOP ... OUTPUT`) is SQL Server–specific and would need rewriting.
   That's a good illustration of what raw SQL costs you.
