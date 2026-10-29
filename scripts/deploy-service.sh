#!/usr/bin/env bash
# Rolling update of one service to a new image tag, recorded in infra/versions.env so a later full deploy
# (scripts/deploy.sh) keeps it: Git stays the source of truth.
# If the new version never becomes healthy, Swarm rolls back by itself (failure_action: rollback in stack.yml);
# this script then puts the old tag back in versions.env and exits with an error.
# Usage: bash scripts/deploy-service.sh orders-api 2
# Manual undo later: docker service rollback lab_orders-api   (and put the old tag back in versions.env)
set -euo pipefail
cd "$(dirname "$0")/.."

service=${1:?service name, e.g. orders-api}
tag=${2:?image tag, e.g. 2}
var="$(echo "$service" | tr 'a-z-' 'A-Z_')_TAG"
previous="$(grep "^$var=" infra/versions.env | cut -d= -f2)"

sed -i "s/^$var=.*/$var=$tag/" infra/versions.env

if ! docker service update --detach=false --image "localhost:5000/$service:$tag" "lab_$service"; then
  sed -i "s/^$var=.*/$var=$previous/" infra/versions.env
  echo "==> Update to $tag FAILED and Swarm rolled back to $previous. Why: bash scripts/logs.sh $service 100"
  exit 1
fi

running="$(docker service inspect "lab_$service" --format '{{.Spec.TaskTemplate.ContainerSpec.Image}}')"
if [[ "$running" != *":$tag"* ]]; then
  sed -i "s/^$var=.*/$var=$previous/" infra/versions.env
  echo "==> Swarm rolled back: lab_$service runs $running, not tag $tag. Why: bash scripts/logs.sh $service 100"
  exit 1
fi
echo "==> lab_$service now runs localhost:5000/$service:$tag"
