#!/usr/bin/env bash
# Builds images and pushes them to the local registry (localhost:5000), like the build stage of a CI pipeline.
# Usage: bash scripts/build.sh all              every service, with its tag from infra/versions.env
#        bash scripts/build.sh orders-api        one service, with its tag from infra/versions.env
#        bash scripts/build.sh orders-api 7      one service, with a new tag (then: bash scripts/deploy-service.sh orders-api 7)
set -euo pipefail
cd "$(dirname "$0")/.."
source infra/versions.env

services=(customers-api billing-api orders-api documents-api notification-worker web-portal autoscaler)

# orders-api -> ORDERS_API_TAG
tag_var() { echo "$(echo "$1" | tr 'a-z-' 'A-Z_')_TAG"; }

build() {
  local service=$1 tag=$2 image="localhost:5000/$1:$2"
  echo "==> Building $image"
  if [ "$service" = web-portal ]; then
    docker build -t "$image" src/web-portal                    # Angular app: its own build context
  else
    docker build -f "src/$service/Dockerfile" -t "$image" .     # .NET: repo root, for the shared code
  fi
  docker push -q "$image"
}

case "${1:-}" in
  "")  echo "Usage: bash scripts/build.sh all | <service> [tag]"; echo "Services: ${services[*]}"; exit 1 ;;
  all) for s in "${services[@]}"; do v=$(tag_var "$s"); build "$s" "${!v}"; done ;;
  *)   v=$(tag_var "$1"); build "$1" "${2:-${!v}}" ;;
esac
