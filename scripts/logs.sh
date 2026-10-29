#!/usr/bin/env bash
# Follows the logs of one service (all its replicas), or shows recent errors and warnings everywhere.
# Usage: bash scripts/logs.sh orders-api          live logs of lab_orders-api (Ctrl+C to stop)
#        bash scripts/logs.sh orders-api 200      last 200 lines, no follow
#        bash scripts/logs.sh errors              WARN/ERROR lines from every service in the last 30 minutes
set -uo pipefail

target="${1:?service name, e.g. orders-api, or errors}"

if [ "$target" = errors ]; then
  for service in $(docker stack services lab --format '{{.Name}}'); do
    docker service logs --raw --since 30m "$service" 2>/dev/null | grep -E ' (WARN |ERROR|CRIT ) ' | sed "s/^/[${service#lab_}] /"
  done | sort -k2,2
elif [ -n "${2:-}" ]; then
  docker service logs --raw --tail "$2" "lab_$target"
else
  docker service logs --raw --follow --tail 20 "lab_$target"
fi
