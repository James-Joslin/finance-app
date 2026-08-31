#!/usr/bin/env bash

set -euo pipefail

SCRIPT_DIR="$(CDPATH= cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=_compose.sh
source "$SCRIPT_DIR/_compose.sh"

db_container="$(service_container db)"
check_database="finova_ci_check_$$"
check_container="finova-migration-check-$$"

cleanup() {
    local exit_code=$?
    set +e
    docker rm --force "$check_container" >/dev/null 2>&1
    docker exec "$db_container" sh -c \
        'dropdb --if-exists --username "$POSTGRES_USER" --maintenance-db "$POSTGRES_DB" "$1"' \
        sh "$check_database" >/dev/null 2>&1
    exit "$exit_code"
}
trap cleanup EXIT INT TERM

docker exec "$db_container" sh -c \
    'createdb --username "$POSTGRES_USER" --maintenance-db "$POSTGRES_DB" "$1"' \
    sh "$check_database"

compose run \
    --detach \
    --no-deps \
    --name "$check_container" \
    --entrypoint sleep \
    --env "POSTGRES_DB=$check_database" \
    migrations infinity >/dev/null

docker exec --workdir /migrations "$check_container" alembic upgrade head
docker exec --workdir /migrations "$check_container" alembic downgrade base
docker exec --workdir /migrations "$check_container" alembic upgrade head
