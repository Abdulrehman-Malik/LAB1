#!/usr/bin/env bash
# Shows every service, its replicas and image tag, then checks each route through Traefik.
# Usage: bash scripts/status.sh
set -uo pipefail

docker stack services lab --format 'table {{.Name}}\t{{.Replicas}}\t{{.Image}}'
echo
docker stack services ops --format 'table {{.Name}}\t{{.Replicas}}\t{{.Image}}' 2>/dev/null
echo
echo "Routes through Traefik (http://localhost:8000):"
for path in / /api/customers/health /api/billing/health /api/orders/health /api/documents/health /api/notifications/health; do
  printf '  %-30s %s\n' "$path" "$(curl -s -o /dev/null -w '%{http_code}' "http://localhost:8000$path")"
done
echo
echo "Web pages:"
echo "  Portal            http://localhost:8000"
echo "  Traefik routes    http://localhost:8088"
echo "  Portainer         https://localhost:9443"
echo "  MinIO console     http://localhost:9001   (labadmin / MINIO_ROOT_PASSWORD in infra/.env)"
echo "  Mailpit inbox     http://localhost:8025"
