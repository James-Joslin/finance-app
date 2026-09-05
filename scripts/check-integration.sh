#!/usr/bin/env bash

set -euo pipefail

SCRIPT_DIR="$(CDPATH= cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(CDPATH= cd -- "$SCRIPT_DIR/.." && pwd)"

mode="${1:-all}"
if [[ "$mode" != "all" && "$mode" != "--e2e-only" ]]; then
    echo "Usage: $0 [--e2e-only]" >&2
    exit 2
fi

if [[ -n "${FINOVA_ENV_FILE:-}" ]]; then
    ENV_FILE="$FINOVA_ENV_FILE"
elif [[ -f "$REPO_ROOT/.env.dev" ]]; then
    ENV_FILE="$REPO_ROOT/.env.dev"
else
    ENV_FILE="$REPO_ROOT/.env.dev.example"
fi

test_project="finova-integration-$$"
port_base=$((30000 + ($$ % 1000)))
database_port=$((port_base + 1))
api_port=$((port_base + 2))
frontend_port=$((port_base + 3))

compose_test() {
    POSTGRES_HOST_PORT="$database_port"     API_PORT="$api_port"     FRONTEND_PORT="$frontend_port"     docker compose         --project-name "$test_project"         --project-directory "$REPO_ROOT"         --env-file "$ENV_FILE"         --file "$REPO_ROOT/compose.dev.yml"         "$@"
}

cleanup() {
    local exit_code=$?
    set +e
    compose_test down --volumes --remove-orphans --rmi local >/dev/null 2>&1
    exit "$exit_code"
}
trap cleanup EXIT INT TERM

printf '\n==> Starting disposable integration stack (%s)\n' "$test_project"
compose_test up --detach --build db migrations api frontend

printf '\n==> Waiting for disposable API\n'
api_ready=false
for _ in $(seq 1 60); do
    if compose_test exec -T api curl --fail --silent http://localhost:8080/status/ready >/dev/null 2>&1; then
        api_ready=true
        break
    fi
    sleep 2
done
if [[ "$api_ready" != true ]]; then
    compose_test logs migrations api
    echo "The disposable API did not become ready." >&2
    exit 1
fi

printf '\n==> Running Playwright household E2E\n'
compose_test run --rm --no-deps --build e2e
if [[ "$mode" == "--e2e-only" ]]; then
    exit 0
fi
printf '\n==> Resetting disposable database for API integration tests\n'
compose_test down --volumes --remove-orphans >/dev/null
compose_test up --detach --build db migrations api frontend
api_ready=false
for _ in $(seq 1 60); do
    if compose_test exec -T api curl --fail --silent http://localhost:8080/status/ready >/dev/null 2>&1; then
        api_ready=true
        break
    fi
    sleep 2
done
if [[ "$api_ready" != true ]]; then
    compose_test logs migrations api
    echo "The reset disposable API did not become ready." >&2
    exit 1
fi

printf '\n==> Running database-backed API tests\n'
compose_test exec -T --workdir /repo api     dotnet restore api.Tests/financesApi.Tests.csproj
compose_test exec -T --workdir /repo api     dotnet test api.Tests/financesApi.Tests.csproj     --configuration Release     --no-restore     --verbosity normal
