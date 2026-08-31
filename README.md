# Finova

A private, responsive household finance hub. Finova combines:

- Safe-to-spend balances after per-account buffers and confirmed near-term bills.
- Multiple prioritised savings goals with account-backed waterfall allocation and countdowns.
- Recurring bills and paydays with transaction-pattern suggestions.
- Monthly category budgets with optional positive rollover.
- Typed transaction review, categorisation rules, OFX/QIF and multi-page Halifax PDF import, and CSV export.
- Household insights, global search, responsive layouts, and persistent light/dark themes.

Finova is intentionally login-free for use on a trusted private network. It does not connect to banks
or move money; balances come from opening values and imported transactions.

On first use, Finova asks for a first name, last name, and household display name. Enrollment is
complete when the singleton profile row exists; the details can be updated later in Settings.
Existing accounts and transactions are not changed by enrollment.

Halifax PDF import supports statements containing selectable text. Image-only scans must be processed
with OCR before import; Finova rejects them rather than silently importing incomplete financial data.

The application is composed of:

- A React/Vite frontend.
- An ASP.NET Core 8 API.
- PostgreSQL 17.
- Alembic database migrations.
- Nginx as the production frontend and API gateway.

The development and production stacks use different Compose project names and Docker volumes. They do not share database data.

## Requirements

Install Docker with the Compose plugin. No host installation of .NET, Node, Python, Alembic, or PostgreSQL is required.

## Development

Create the local environment file:

```sh
cp .env.dev.example .env.dev
```

Start the complete development stack:

```sh
docker compose --env-file .env.dev -f compose.dev.yml up --build
```

The services are available at:

- Frontend: http://localhost:5173
- API liveness: http://localhost:5153/status/live
- API readiness: http://localhost:5153/status/ready
- Swagger: http://localhost:5153/swagger
- PostgreSQL: localhost:55432

Browser API requests use `/api` through the Vite proxy. Source code is bind-mounted into the API and frontend containers for hot reload.

Stop the stack while retaining its database:

```sh
docker compose --env-file .env.dev -f compose.dev.yml down
```

To permanently delete the development database and all other development volumes:

```sh
docker compose --env-file .env.dev -f compose.dev.yml down --volumes
```

## Production

Create the production environment file and replace the example password with a long random value:

```sh
cp .env.prod.example .env.prod
```

Start the stack:

```sh
docker compose --env-file .env.prod -f compose.prod.yml up --build --detach
```

The application is available at http://localhost:8080 by default. Change `APP_PORT` to publish a different host port.

Only Nginx is published in production. PostgreSQL and the API are reachable solely over the private Compose network. The stack serves HTTP only.

View status and logs:

```sh
docker compose --env-file .env.prod -f compose.prod.yml ps
docker compose --env-file .env.prod -f compose.prod.yml logs --follow
```

API logs are single-line JSON with UTC timestamps. Request-completion and error events include a
trace ID; unexpected API error responses return the same value as `traceId` for correlation.
Request headers, bodies, query values, uploaded filenames, financial values, SQL parameters, and
connection strings are not logged.

Stop production while retaining its database:

```sh
docker compose --env-file .env.prod -f compose.prod.yml down
```

## Database migrations

The PostgreSQL image creates the database named by `POSTGRES_DB`. Alembic owns everything inside that database.

Every Compose startup runs `alembic upgrade head` after PostgreSQL passes its health check. The API starts only when the migration exits successfully.

Inspect the current development revision:

```sh
docker compose --env-file .env.dev -f compose.dev.yml run --rm migrations current
```

Create a new empty revision:

```sh
docker compose --env-file .env.dev -f compose.dev.yml run --rm migrations revision -m "describe the schema change"
```

Edit the generated file under `migrations/versions/`, then apply it:

```sh
docker compose --env-file .env.dev -f compose.dev.yml run --rm migrations upgrade head
```

Test downgrades only against a disposable database. A downgrade can destroy application data.

## Tests

Run the complete local CI-equivalent suite through the development containers:

```sh
./scripts/check-all.sh
```

The orchestrator builds and starts the development stack, then runs the backend, frontend, migration,
backup/restore, production-image, and Semgrep checks. The recovery check migrates a disposable source
database, uploads its dump to Azurite, restores it under a new name, validates its data and revision,
proves overwrite refusal, and removes all test data. CodeQL remains GitHub-only.

Each `scripts/check-*.sh` entry point can also be run independently. Set `FINOVA_ENV_FILE` to use an environment file other than `.env.dev` or `.env.dev.example`.

Run backend finance-calculation tests in the same .NET toolchain used by the API:

```sh
docker run --rm -v "$PWD:/repo" -w /repo/api.Tests finance-app-dev-api \
  dotnet test --configuration Release
```

Run frontend unit and component tests:

```sh
docker run --rm -v "$PWD/frontend:/app" -w /app node:22-alpine \
  node node_modules/vitest/vitest.mjs run
```

The development stack exposes Swagger at http://localhost:5153/swagger for the typed Finova APIs.
Legacy upload and reporting routes remain available as compatibility adapters.

## Backups

Production runs a backup scheduler at 02:00 UTC each day and retains 14 days by default. Each
PostgreSQL custom-format dump is uploaded as an immutable Blob with a SHA-256 checksum under:

```text
<database>/YYYY/MM/DD/<database>_YYYYMMDDTHHMMSSZ.dump
```

Azurite is private to the Compose network and persists to `azurite_prod_data`, separately from the
PostgreSQL volume. Generate a base64 account key before the first production start and place it in
`.env.prod`:

```sh
openssl rand -base64 64
```

Create an immediate backup through the same path used by the scheduler:

```sh
./scripts/backup-now.sh
```

List available backup blobs:

```sh
docker compose --env-file .env.prod -f compose.prod.yml run --rm backup list
```

Set `FINOVA_ENV_FILE` when the production environment file is not `.env.prod`.

### Test a restore

Restore a selected blob into a new, lowercase database name:

```sh
./scripts/restore-backup.sh \
  "finances_db/2026/08/31/finances_db_20260831T020000Z.dump" \
  "finances_restore_20260831"
```

The restore command verifies Blob checksum metadata, refuses an existing target, creates the new
database, restores without ownership or privilege statements, and verifies its Alembic revision.
If restoration fails, it removes only the new partial database.

Inspect the restored database before cutover:

```sh
docker compose --env-file .env.prod -f compose.prod.yml exec db \
  psql -U finances_app -d finances_restore_20260831 -c "SELECT version_num FROM alembic_version;"
```

Use the configured `POSTGRES_USER` if it differs from `finances_app`.

### Cut over or roll back

After validating the restored database:

1. Stop writes with
   `docker compose --env-file .env.prod -f compose.prod.yml stop frontend api`.
2. Change `POSTGRES_DB` in `.env.prod` to the restored database name.
3. Run
   `docker compose --env-file .env.prod -f compose.prod.yml up --detach migrations api frontend`.
4. Confirm `curl --fail http://localhost:8080/api/status/ready` returns healthy.

The original database is not changed or deleted. To roll back, stop frontend/API, restore the
original `POSTGRES_DB` value, and start migrations/API/frontend again.

Azurite is a development-oriented storage emulator running on the same Docker host. These backups
protect against PostgreSQL-volume corruption and accidental database loss, but not total host loss.
Snapshot or copy the `azurite_prod_data` volume off-host for host-level disaster recovery.

## Environment variables

| Variable | Purpose | Development default |
| --- | --- | --- |
| `POSTGRES_HOST` | Database hostname used by API and Alembic | `db` |
| `POSTGRES_PORT` | Database container port | `5432` |
| `POSTGRES_DB` | Database name | `finances_db` |
| `POSTGRES_USER` | Application/database role | `finances_app` |
| `POSTGRES_PASSWORD` | Database password | Development-only example |
| `POSTGRES_SSL_MODE` | Npgsql SSL mode | `Disable` |
| `POSTGRES_HOST_PORT` | Development host database port | `55432` |
| `API_PORT` | Development host API port | `5153` |
| `FRONTEND_PORT` | Development host frontend port | `5173` |
| `APP_PORT` | Production Nginx host port | `8080` |
| `AZURITE_ACCOUNT_NAME` | Private Blob account used by the backup service | `finovadev` |
| `AZURITE_ACCOUNT_KEY` | Base64 account key shared by Azurite and backup tooling | Development emulator key |
| `BACKUP_BLOB_CONTAINER` | Blob container that holds database dumps | `database-backups` |
| `BACKUP_CRON` | Five-field UTC backup schedule | `0 2 * * *` |
| `BACKUP_RETENTION_DAYS` | Age after which matching database blobs are deleted | `14` |

Real `.env.dev` and `.env.prod` files are ignored by Git. Only the example files should be committed.


## Schema

The initial Alembic revision creates the dump-derived `people`, `accounts`, and `transactions`
tables. The additive Finova revision preserves those records and adds household settings, account
safety fields, categories and payee rules, savings goals and private images, recurring items, and
budget definitions/snapshots.

Goal images are stored in PostgreSQL so the documented database backup includes all private app data.
Uploads accept PNG, JPEG, and WebP files up to 2 MB; SVG uploads are rejected.

## Troubleshooting

Check migration output first if the API does not start:

```sh
docker compose --env-file .env.dev -f compose.dev.yml logs migrations db
```

Check service health:

```sh
docker compose --env-file .env.dev -f compose.dev.yml ps
curl --fail http://localhost:5173/api/status/live
curl --fail http://localhost:5173/api/status/ready
```

If a published development port is already in use, change its corresponding value in `.env.dev`.
