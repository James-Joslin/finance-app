#!/usr/bin/env bash

set -euo pipefail

SCRIPT_DIR="$(CDPATH= cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=_compose.sh
source "$SCRIPT_DIR/_compose.sh"

db_container="$(service_container db)"
database_user="$(docker exec "$db_container" printenv POSTGRES_USER)"
maintenance_database="$(docker exec "$db_container" printenv POSTGRES_DB)"
source_database="finova_backup_source_$$"
target_database="finova_backup_restore_$$"
sentinel="sentinel_$$"
test_blob=""

cleanup() {
    local exit_code=$?
    set +e
    if [[ -n "$test_blob" ]]; then
        compose run --rm --no-deps backup delete --blob "$test_blob" >/dev/null 2>&1
    fi
    docker exec "$db_container" dropdb \
        --if-exists --username "$database_user" --maintenance-db "$maintenance_database" \
        "$target_database" >/dev/null 2>&1
    docker exec "$db_container" dropdb \
        --if-exists --username "$database_user" --maintenance-db "$maintenance_database" \
        "$source_database" >/dev/null 2>&1
    exit "$exit_code"
}
trap cleanup EXIT INT TERM

compose up --detach azurite
compose build backup

compose run --rm --no-deps --entrypoint python backup \
    -m unittest discover --start-directory /app/tests

docker exec "$db_container" createdb \
    --username "$database_user" --maintenance-db "$maintenance_database" \
    "$source_database"

compose run --rm --no-deps \
    --env "POSTGRES_DB=$source_database" \
    migrations upgrade head

docker exec "$db_container" psql \
    --username "$database_user" --dbname "$source_database" \
    --set ON_ERROR_STOP=1 \
    --command "CREATE TABLE backup_restore_sentinel (value text PRIMARY KEY);" \
    --command "INSERT INTO backup_restore_sentinel (value) VALUES ('$sentinel');"

backup_output="$(compose run --rm --no-deps \
    --env "POSTGRES_DB=$source_database" \
    backup backup)"
printf '%s\n' "$backup_output"
test_blob="$(printf '%s\n' "$backup_output" | sed -n \
    's/.*"blob":"\([^"]*\)".*"event":"backup_completed".*/\1/p' | tail -n 1)"
if [[ -z "$test_blob" ]]; then
    echo "The backup check could not determine the uploaded blob name." >&2
    exit 1
fi

compose run --rm --no-deps backup restore \
    --blob "$test_blob" \
    --target-database "$target_database"

restored_sentinel="$(docker exec "$db_container" psql \
    --username "$database_user" --dbname "$target_database" \
    --tuples-only --no-align \
    --command "SELECT value FROM backup_restore_sentinel;")"
[[ "$restored_sentinel" == "$sentinel" ]]

source_revision="$(docker exec "$db_container" psql \
    --username "$database_user" --dbname "$source_database" \
    --tuples-only --no-align --command "SELECT version_num FROM alembic_version;")"
target_revision="$(docker exec "$db_container" psql \
    --username "$database_user" --dbname "$target_database" \
    --tuples-only --no-align --command "SELECT version_num FROM alembic_version;")"
[[ "$target_revision" == "$source_revision" ]]

if compose run --rm --no-deps backup restore \
    --blob "$test_blob" --target-database "$target_database"; then
    echo "Restore unexpectedly accepted an existing target database." >&2
    exit 1
fi

printf '\nBackup/restore integration check passed for %s.\n' "$test_blob"
