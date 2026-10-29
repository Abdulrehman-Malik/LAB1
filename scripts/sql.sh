#!/usr/bin/env bash
# Runs SQL inside the SQL Server container, as sa.
# Usage: bash scripts/sql.sh -d SalesDb -Q "SELECT TOP 5 * FROM orders.Orders ORDER BY Id DESC"
#        bash scripts/sql.sh -Q "SELECT name FROM sys.databases"
set -euo pipefail
cd "$(dirname "$0")/.."
set -a; source infra/.env; set +a

container="$(docker ps -q -f name=lab_db-sqlserver. | head -n 1)"
docker exec -i "$container" /opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -P "$MSSQL_SA_PASSWORD" -W "$@"
