#!/usr/bin/env bash

set -euo pipefail

SCRIPT_DIR="$(CDPATH= cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(CDPATH= cd -- "$SCRIPT_DIR/.." && pwd)"
check_container="finova-semgrep-check-$$"

cleanup() {
    local exit_code=$?
    set +e
    docker rm --force "$check_container" >/dev/null 2>&1
    exit "$exit_code"
}
trap cleanup EXIT INT TERM

docker run \
    --detach \
    --name "$check_container" \
    --volume "$REPO_ROOT:/src:ro" \
    --workdir /src \
    --entrypoint sleep \
    semgrep/semgrep:1.174.0 \
    infinity >/dev/null

docker exec --workdir /src "$check_container" \
    semgrep scan --config auto --error
