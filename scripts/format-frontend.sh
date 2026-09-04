#!/usr/bin/env bash

set -euo pipefail

SCRIPT_DIR="$(CDPATH= cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=_compose.sh
source "$SCRIPT_DIR/_compose.sh"

printf '\n==> Formatting frontend with Prettier\n'
exec_service frontend /app npm run format

printf '\n==> Verifying frontend formatting\n'
exec_service frontend /app npm run format:check
