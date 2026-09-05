#!/usr/bin/env bash

set -euo pipefail

SCRIPT_DIR="$(CDPATH= cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(CDPATH= cd -- "$SCRIPT_DIR/.." && pwd)"
failed=0

check_reference() {
    local source="$1"
    local reference="$2"

    if [[ ! "$reference" =~ :[^@]+@sha256:[0-9a-f]{64}$ ]]; then
        echo "Unpinned container image in $source: $reference" >&2
        failed=1
    fi
}

dockerfiles=(
    api/Dockerfile
    frontend/Dockerfile
    migrations/Dockerfile
    operations/Dockerfile
)

for relative_path in "${dockerfiles[@]}"; do
    dockerfile="$REPO_ROOT/$relative_path"
    while IFS= read -r reference; do
        check_reference "$relative_path" "$reference"
    done < <(
        awk '
            toupper($1) == "FROM" {
                for (field = 2; field <= NF; field++) {
                    if ($field !~ /^--/) {
                        print $field
                        break
                    }
                }
            }
        ' "$dockerfile"
    )
done

while IFS= read -r reference; do
    if [[ "$reference" == *'${FINOVA_IMAGE_PREFIX'* ]]; then
        continue
    fi
    check_reference "compose.prod.yml" "$reference"
done < <(
    sed -nE 's/^[[:space:]]*image:[[:space:]]*"?([^"[:space:]]+)"?.*$/\1/p' \
        "$REPO_ROOT/compose.prod.yml"
)

if [[ "$failed" -ne 0 ]]; then
    echo "Production container inputs must retain a readable tag and a sha256 digest." >&2
    exit 1
fi

echo "All production container inputs are pinned to sha256 digests."
