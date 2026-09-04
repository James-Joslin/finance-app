#!/usr/bin/env bash

set -euo pipefail

SCRIPT_DIR="$(CDPATH= cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"

"$SCRIPT_DIR/format-backend.sh"
"$SCRIPT_DIR/format-frontend.sh"
