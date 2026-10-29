#!/usr/bin/env bash
# Waits for SQL Server to accept logins, then caps its cache ("max server memory") so it uses about 1 GB.
# Usage: bash scripts/sql-tune.sh [MB]   (default 768)
set -euo pipefail
cd "$(dirname "$0")/.."

set -a; source infra/.env; set +a
max_mb="${1:-768}"

sqlcmd() {
  local container
  container="$(docker ps -q -f name=lab_db-sqlserver. | head -n 1)"
  [ -n "$container" ] || return 1
  docker exec "$container" /opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -P "$MSSQL_SA_PASSWORD" -b "$@"
}

echo "==> Waiting for SQL Server to accept logins"
for _ in $(seq 1 60); do
  if sqlcmd -Q "SELECT 1" >/dev/null 2>&1; then break; fi
  sleep 3
done

sqlcmd -Q "EXEC sp_configure 'show advanced options', 1; RECONFIGURE; EXEC sp_configure 'max server memory (MB)', $max_mb; RECONFIGURE;" >/dev/null
echo "==> SQL Server max server memory set to $max_mb MB"
