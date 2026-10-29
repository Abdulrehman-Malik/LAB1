#!/usr/bin/env bash
# Finds every log line, in every service, that belongs to one request.
# The trace id is shown in the portal's request inspector (bottom of every page), in the X-Trace-Id
# response header, and in MongoDB audit events. Services pass it along, so one id follows the request
# from orders-api through its gRPC calls into customers-api and billing-api.
#
# Usage: bash scripts/trace.sh 4bf92f3577b34da6a3ce929d0e0e4736          (searches the last 2 hours)
#        bash scripts/trace.sh 4bf92f3577b34da6a3ce929d0e0e4736 24h      (further back: slower)
set -uo pipefail

trace="${1:?trace id, e.g. 4bf92f3577b34da6a3ce929d0e0e4736, shown in the request inspector of the portal}"
since="${2:-2h}"

for service in $(docker stack services lab --format '{{.Name}}'); do
  docker service logs --raw --since "$since" "$service" 2>/dev/null | grep -F "trace=$trace" | sed "s/^/[${service#lab_}] /"
done | sort -k2,2   # sort by the time column, so you read the story in order
