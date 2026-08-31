#!/usr/bin/env bash

set -euo pipefail

SCRIPT_DIR="$(CDPATH= cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=_compose.sh
source "$SCRIPT_DIR/_compose.sh"

compose up --detach --build db migrations api frontend

"$SCRIPT_DIR/check-backend.sh"
"$SCRIPT_DIR/check-frontend.sh"
"$SCRIPT_DIR/check-migrations.sh"
"$SCRIPT_DIR/check-images.sh"
"$SCRIPT_DIR/check-semgrep.sh"
