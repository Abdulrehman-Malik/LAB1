#!/usr/bin/env bash
# Deploys the whole application from Git: infra/stack.yml + image tags from infra/versions.env.
# Re-running applies any changes (and resets replica counts to the stack file's baseline).
# Then it waits for the rolling updates to finish and checks that every service really runs the tag from
# versions.env. If Swarm rolled a service back (new version never became healthy), it says so and exits 1.
# Run from the repo root in WSL Ubuntu: bash scripts/deploy.sh
set -euo pipefail
cd "$(dirname "$0")/.."

set -a
source infra/.env
source infra/versions.env
AUTOSCALE_HASH="$(sha1sum infra/autoscale.yml | cut -c1-10)"
set +a

docker stack deploy --detach=true -c infra/stack.yml lab
bash scripts/sql-tune.sh

echo "==> Waiting for rolling updates to finish (can take a few minutes after a big change)"
for _ in $(seq 1 72); do
  busy=""
  for s in $(docker stack services lab --format '{{.Name}}'); do
    state="$(docker service inspect "$s" --format '{{if .UpdateStatus}}{{.UpdateStatus.State}}{{end}}')"
    replicas="$(docker service ls --filter "name=$s" --format '{{.Replicas}}' | head -n 1)"
    if [[ "$state" == updating || "$state" == rollback_started || "${replicas%/*}" != "${replicas#*/}" ]]; then busy="$busy $s"; fi
  done
  [ -z "$busy" ] && break
  sleep 5
done
[ -n "$busy" ] && echo "    still busy after 6 minutes:$busy"

echo "==> Checking that each service runs the tag from infra/versions.env"
failed=0
for service in customers-api billing-api orders-api documents-api notification-worker web-portal autoscaler; do
  var="$(echo "$service" | tr 'a-z-' 'A-Z_')_TAG"
  expected="${!var}"
  running="$(docker service inspect "lab_$service" --format '{{.Spec.TaskTemplate.ContainerSpec.Image}}' | sed 's/@.*//')"
  if [[ "$running" == *":$expected" ]]; then
    printf '    OK    %-22s %s\n' "$service" "$running"
  else
    status="$(docker service inspect "lab_$service" --format '{{if .UpdateStatus}}{{.UpdateStatus.State}}: {{.UpdateStatus.Message}}{{end}}')"
    printf '    FAIL  %-22s runs %s, expected tag %s. %s\n' "$service" "$running" "$expected" "$status"
    printf '          why: bash scripts/logs.sh %s 100   and   docker service ps lab_%s --no-trunc\n' "$service" "$service"
    failed=1
  fi
done

echo
docker stack services lab --format 'table {{.Name}}\t{{.Replicas}}\t{{.Image}}'
exit $failed
