#!/usr/bin/env bash

set -euo pipefail

SCRIPT_DIR="$(CDPATH= cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(CDPATH= cd -- "$SCRIPT_DIR/.." && pwd)"

"$SCRIPT_DIR/check-container-pins.sh"

docker build \
    --platform linux/amd64 \
    --file "$REPO_ROOT/api/Dockerfile" \
    --tag finova-api-check \
    "$REPO_ROOT/api"

docker build \
    --platform linux/amd64 \
    --file "$REPO_ROOT/frontend/Dockerfile" \
    --tag finova-frontend-check \
    "$REPO_ROOT/frontend"

docker build \
    --platform linux/amd64 \
    --file "$REPO_ROOT/migrations/Dockerfile" \
    --tag finova-migrations-check \
    "$REPO_ROOT/migrations"

docker build \
    --platform linux/amd64 \
    --file "$REPO_ROOT/operations/Dockerfile" \
    --tag finova-operations-check \
    "$REPO_ROOT/operations"
