#!/usr/bin/env bash

set -euo pipefail

SCRIPT_DIR="$(CDPATH= cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=_compose.sh
source "$SCRIPT_DIR/_compose.sh"

printf '\n==> Auditing frontend dependencies\n'
exec_service frontend /app npm run audit

printf '\n==> Verifying Prettier formatting\n'
exec_service frontend /app npm run format:check

printf '\n==> Running ESLint\n'
exec_service frontend /app npm run lint

printf '\n==> Running frontend tests\n'
exec_service frontend /app npm test -- --reporter=dot

printf '\n==> Building production frontend\n'
exec_service frontend /app npm run build
