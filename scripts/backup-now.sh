#!/usr/bin/env bash

set -euo pipefail

SCRIPT_DIR="$(CDPATH= cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(CDPATH= cd -- "$SCRIPT_DIR/.." && pwd)"
ENV_FILE="${FINOVA_ENV_FILE:-$REPO_ROOT/.env.prod}"

if [[ ! -f "$ENV_FILE" ]]; then
    echo "Production environment file not found: $ENV_FILE" >&2
    echo "Create .env.prod or set FINOVA_ENV_FILE." >&2
    exit 1
fi

compose() {
    docker compose \
        --project-directory "$REPO_ROOT" \
        --env-file "$ENV_FILE" \
        --file "$REPO_ROOT/compose.prod.yml" \
        "$@"
}

compose run --rm backup backup
