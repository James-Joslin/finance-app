#!/usr/bin/env bash

set -euo pipefail

SCRIPT_DIR="$(CDPATH= cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=_compose.sh
source "$SCRIPT_DIR/_compose.sh"

api_container="$(service_container api)"
if ! docker exec "$api_container" test -f /repo/api.Tests/financesApi.Tests.csproj; then
    echo "The API container does not have the test-project mount." >&2
    echo "Recreate it with: docker compose --env-file '$ENV_FILE' -f '$REPO_ROOT/compose.dev.yml' up -d --build api" >&2
    exit 1
fi

printf '\n==> Restoring backend dependencies\n'
docker exec --workdir /repo "$api_container" \
    dotnet restore api.Tests/financesApi.Tests.csproj

printf '\n==> Formatting backend projects\n'
docker exec --workdir /repo "$api_container" \
    dotnet format api/financesApi.csproj --no-restore
docker exec --workdir /repo "$api_container" \
    dotnet format api.Tests/financesApi.Tests.csproj --no-restore
