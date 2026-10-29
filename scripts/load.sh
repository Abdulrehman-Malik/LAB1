#!/usr/bin/env bash
# Sends heavy traffic through Traefik, so you can watch the autoscaler add replicas.
# Usage: bash scripts/load.sh [seconds] [concurrent-users] [path]
#        defaults: 120 seconds, 40 users, /api/orders
# Watch in a second terminal:  docker service logs -f lab_autoscaler
#                         or:  watch -n 2 docker service ls
set -euo pipefail

seconds="${1:-120}"
users="${2:-40}"
path="${3:-/api/orders}"

echo "==> $users users hitting http://gateway-traefik$path for $seconds s"
docker run --rm --network labnet williamyeh/hey -z "${seconds}s" -c "$users" "http://gateway-traefik$path"
