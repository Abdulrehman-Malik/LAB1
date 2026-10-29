#!/usr/bin/env bash
# One-time (and safe to re-run) setup of the platform the application runs on:
#   Swarm, the labnet network, random passwords + Swarm secrets, and the "ops" stack (image registry + Portainer).
# Run from the repo root in WSL Ubuntu: bash scripts/bootstrap.sh
set -euo pipefail
cd "$(dirname "$0")/.."

if [ "$(docker info --format '{{.Swarm.LocalNodeState}}')" != "active" ]; then
  echo "==> Initialising Docker Swarm (this machine becomes the manager node)"
  docker swarm init >/dev/null
fi

if ! docker network inspect labnet >/dev/null 2>&1; then
  echo "==> Creating overlay network 'labnet' (services find each other by name on it)"
  docker network create --driver overlay --attachable labnet >/dev/null
fi

# Random passwords live in infra/.env (git-ignored); missing keys are added, existing ones kept
touch infra/.env
for key in PORTAINER_ADMIN_PASSWORD MSSQL_SA_PASSWORD MINIO_ROOT_PASSWORD MONGO_ROOT_PASSWORD; do
  if ! grep -q "^$key=" infra/.env; then
    echo "==> Generating $key in infra/.env"
    # "Lab_" prefix satisfies SQL Server's rule: upper + lower + digit + symbol
    echo "$key=Lab_$(openssl rand -hex 12)" >> infra/.env
  fi
done
set -a; source infra/.env; set +a

# Swarm secrets: encrypted in the Swarm, mounted as files in /run/secrets only inside the services that list them
create_secret() {
  if ! docker secret inspect "$1" >/dev/null 2>&1; then
    echo "==> Creating Swarm secret '$1'"
    printf '%s' "$2" | docker secret create "$1" - >/dev/null
  fi
}
create_secret portainer_admin_password "$PORTAINER_ADMIN_PASSWORD"
create_secret mssql_sa_password "$MSSQL_SA_PASSWORD"
create_secret minio_root_user "labadmin"
create_secret minio_root_password "$MINIO_ROOT_PASSWORD"
create_secret mongo_root_password "$MONGO_ROOT_PASSWORD"

# Older versions of this lab used separate "registry" and "portainer" stacks; they are now one "ops" stack
for old in registry portainer; do
  if docker stack ls --format '{{.Name}}' | grep -qx "$old"; then
    echo "==> Removing old stack '$old' (replaced by 'ops')"
    docker stack rm "$old" >/dev/null
    sleep 15
  fi
done

echo "==> Deploying the ops stack (image registry + Portainer)"
docker stack deploy --detach=true -c infra/ops.yml ops

echo
echo "Image registry: http://localhost:5000/v2/_catalog"
echo "Portainer:      https://localhost:9443  (user admin, password PORTAINER_ADMIN_PASSWORD in infra/.env)"
echo "Next:           bash scripts/build.sh all && bash scripts/deploy.sh"
