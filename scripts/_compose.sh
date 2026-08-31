#!/usr/bin/env bash

set -euo pipefail

SCRIPT_DIR="$(CDPATH= cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(CDPATH= cd -- "$SCRIPT_DIR/.." && pwd)"

if [[ -n "${FINOVA_ENV_FILE:-}" ]]; then
    ENV_FILE="$FINOVA_ENV_FILE"
elif [[ -f "$REPO_ROOT/.env.dev" ]]; then
    ENV_FILE="$REPO_ROOT/.env.dev"
else
    ENV_FILE="$REPO_ROOT/.env.dev.example"
fi

compose() {
    docker compose \
        --project-directory "$REPO_ROOT" \
        --env-file "$ENV_FILE" \
        --file "$REPO_ROOT/compose.dev.yml" \
        "$@"
}

service_container() {
    local service="$1"
    local container_id

    container_id="$(compose ps --status running --quiet "$service")"
    if [[ -z "$container_id" ]]; then
        echo "The Finova development service '$service' is not running." >&2
        echo "Run ./scripts/check-all.sh or start compose.dev.yml first." >&2
        return 1
    fi

    printf '%s\n' "$container_id"
}

exec_service() {
    local service="$1"
    local workdir="$2"
    shift 2

    local container_id
    container_id="$(service_container "$service")"
    docker exec --workdir "$workdir" "$container_id" "$@"
}
