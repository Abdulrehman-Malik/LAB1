#!/usr/bin/env bash
# The CI/CD pipeline, run locally. Same stages as pipelines in Azure DevOps or GitHub Actions:
#   1. unit tests          (stop if any fail)
#   2. build + push image  (new tag = current tag + 1, unless you give one)
#   3. rolling update      (infra/versions.env is updated, so Git records what runs)
#   4. smoke test          (health endpoint through Traefik; on failure: automatic rollback)
#
# Usage: bash scripts/ci.sh orders-api        next tag
#        bash scripts/ci.sh orders-api 9      a chosen tag
set -euo pipefail
cd "$(dirname "$0")/.."
source infra/versions.env

service="${1:?service, e.g. orders-api}"
var="$(echo "$service" | tr 'a-z-' 'A-Z_')_TAG"
previous="${!var}"
tag="${2:-$((previous + 1))}"

case "$service" in
  customers-api)       health=/api/customers/health ;;
  billing-api)         health=/api/billing/health ;;
  orders-api)          health=/api/orders/health ;;
  documents-api)       health=/api/documents/health ;;
  notification-worker) health=/api/notifications/health ;;
  web-portal)          health=/ ;;
  *)                   health="" ;;
esac

echo "==> 1/4 Unit tests (dotnet test, inside the .NET SDK container)"
docker run --rm -v "$PWD":/src -w /src -v lab-nuget-cache:/root/.nuget/packages \
  mcr.microsoft.com/dotnet/sdk:10.0 \
  dotnet test tests/Lab.Tests --artifacts-path /tmp/artifacts --nologo -v q

echo "==> 2/4 Build and push localhost:5000/$service:$tag"
bash scripts/build.sh "$service" "$tag"

echo "==> 3/4 Rolling update lab_$service: $previous -> $tag"
bash scripts/deploy-service.sh "$service" "$tag"   # fails (and restores versions.env) if Swarm had to roll back

if [ -n "$health" ]; then
  echo "==> 4/4 Smoke test http://localhost:8000$health"
  for _ in $(seq 1 12); do
    if curl -sf -o /dev/null "http://localhost:8000$health"; then
      echo "==> OK: $service:$tag is live"
      exit 0
    fi
    sleep 5
  done
  echo "==> Smoke test FAILED: rolling back to $previous"
  docker service rollback --detach=false "lab_$service"
  sed -i "s/^$var=.*/$var=$previous/" infra/versions.env
  exit 1
fi
echo "==> 4/4 No HTTP endpoint for $service; check: docker service logs lab_$service"
