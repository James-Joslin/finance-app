#!/usr/bin/env bash

set -euo pipefail

SCRIPT_DIR="$(CDPATH= cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=_compose.sh
source "$SCRIPT_DIR/_compose.sh"

db_container="$(service_container db)"
database_user="$(docker exec "$db_container" printenv POSTGRES_USER)"
check_database="finova_ci_check_$$"
check_container="finova-migration-check-$$"

cleanup() {
    local exit_code=$?
    set +e
    docker rm --force "$check_container" >/dev/null 2>&1
    docker exec "$db_container" sh -c \
        'dropdb --if-exists --username "$POSTGRES_USER" --maintenance-db "$POSTGRES_DB" "$1"' \
        sh "$check_database" >/dev/null 2>&1
    exit "$exit_code"
}
trap cleanup EXIT INT TERM

docker exec "$db_container" sh -c \
    'createdb --username "$POSTGRES_USER" --maintenance-db "$POSTGRES_DB" "$1"' \
    sh "$check_database"

compose run \
    --detach \
    --no-deps \
    --name "$check_container" \
    --entrypoint sleep \
    --env "POSTGRES_DB=$check_database" \
    migrations infinity >/dev/null

docker exec --workdir /migrations "$check_container" alembic upgrade head
expected_head="$(docker exec "$check_container" alembic heads | sed -n 's/^\([0-9A-Za-z_]*\) (head)$/\1/p')"
if [[ -z "$expected_head" ]]; then
    echo "The migration check could not determine the Alembic head revision." >&2
    exit 1
fi

upgraded_revision="$(docker exec "$db_container" psql \
    --username "$database_user" --dbname "$check_database" \
    --tuples-only --no-align --command "SELECT version_num FROM alembic_version;")"
[[ "$upgraded_revision" == "$expected_head" ]]
docker exec "$db_container" psql --username "$database_user" --dbname "$check_database" \
    --tuples-only --no-align --command "SELECT to_regclass('public.accounts'), to_regclass('public.transaction_import_rows');" \
    | grep -q '^accounts|transaction_import_rows$'

docker exec --workdir /migrations "$check_container" alembic downgrade base
remaining_tables="$(docker exec "$db_container" psql \
    --username "$database_user" --dbname "$check_database" \
    --tuples-only --no-align --command "SELECT count(*) FROM pg_tables WHERE schemaname='public' AND tablename IN ('people', 'accounts', 'transactions');")"
[[ "$remaining_tables" == "0" ]]

docker exec --workdir /migrations "$check_container" alembic upgrade head
restored_revision="$(docker exec "$db_container" psql \
    --username "$database_user" --dbname "$check_database" \
    --tuples-only --no-align --command "SELECT version_num FROM alembic_version;")"
[[ "$restored_revision" == "$expected_head" ]]
docker exec "$db_container" psql --username "$database_user" --dbname "$check_database" \
    --tuples-only --no-align --command "SELECT to_regclass('public.budget_month_closures'), to_regclass('public.statement_sessions');" \
    | grep -q '^budget_month_closures|statement_sessions$'
