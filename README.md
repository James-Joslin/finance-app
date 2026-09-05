# Finova

Finova is a private, responsive household finance hub for budgeting, planning, and reviewing transactions on a trusted private network.

> [!IMPORTANT]
> Finova is intentionally login-free. It does not connect to banks or move money; balances are calculated from opening values and imported transactions.

<img width="1920" height="921" alt="Finova welcome screen" src="https://github.com/user-attachments/assets/d883a1a9-4b47-4071-9e85-62395294f52c" />
<img width="1920" height="918" alt="Finova overview dashboard" src="https://github.com/user-attachments/assets/b94ea0a1-7c4e-4461-b04a-cedce8ef6212" />
<img width="1920" height="917" alt="Finova savings goals" src="https://github.com/user-attachments/assets/611e2b38-26b7-45c5-9ce4-45cc53867188" />
<img width="1920" height="916" alt="Finova household insights" src="https://github.com/user-attachments/assets/052ba89f-2859-4056-9069-86d10d3797c6" />

## Getting started

### Production

Create the production environment file, secure it, and replace the example passwords with a long random value:

```sh
cp .env.prod.example .env.prod
chmod 600 .env.prod
```

Pull the selected release and start the stack:

```sh
docker compose --env-file .env.prod -f compose.prod.yml pull
docker compose --env-file .env.prod -f compose.prod.yml up --detach
```

Finova is available at <http://localhost:8080> by default. Change `APP_PORT` to publish a different host port.

Only Nginx is published in production. PostgreSQL and the API are accessible only over the private Compose network. The stack serves HTTP only.

Production images are published for `linux/amd64` hosts. The `latest` tag tracks the newest successful `main` release and is intended for convenient installations. For a fixed, coordinated release, set `FINOVA_IMAGE_TAG` in `.env.prod` to the full `sha-<commit>` tag shown in GHCR, then run the same `pull` and `up` commands.

Upgrade to the newest successful release:

```sh
docker compose --env-file .env.prod -f compose.prod.yml pull
docker compose --env-file .env.prod -f compose.prod.yml up --detach
```

Changing `FINOVA_IMAGE_TAG` to an older commit rolls back all four application containers together, but does not downgrade PostgreSQL. Only roll back to an application version compatible with the current schema. Otherwise, use a tested database restore or downgrade procedure.

View service status and follow logs:

```sh
docker compose --env-file .env.prod -f compose.prod.yml ps
docker compose --env-file .env.prod -f compose.prod.yml logs --follow
```

API logs are single-line JSON with UTC timestamps. Request-completion and error events include a trace ID; unexpected API error responses return the same value as `traceId` for correlation. Request headers, bodies, query values, uploaded filenames, financial values, SQL parameters, and connection strings are not logged.

Stop production while retaining its database:

```sh
docker compose --env-file .env.prod -f compose.prod.yml down
```

## Features

- **Safe to spend:** See what remains after per-account buffers and confirmed near-term bills.
- **Savings goals:** Prioritize multiple account-backed goals with waterfall allocation and countdowns.
- **Planning:** Track recurring bills and paydays, with suggestions based on transaction patterns.
- **Budgets:** Set monthly category budgets with optional positive rollover.
- **Transaction management:** Review typed transactions, apply categorization rules, import OFX, QIF, and multi-page Halifax PDF statements, and export CSV files.
- **Reconciliation:** Compare account ledgers with statements, clear matched transactions, and resolve discrepancies.
- **Household experience:** Use global search, household insights, responsive layouts, and persistent light or dark themes.
- **Portability:** Back up and restore household data, including private savings-goal images.

## Using Finova

On first use, Finova asks for a first name, last name, and household display name. Enrollment is complete when the singleton profile row exists. These details can be updated later in Settings, and enrollment does not change existing accounts or transactions.

After enrollment, open **Help & support** in the application sidebar for the in-app household guide. The main workflows are:

- **Overview:** Review safe-to-spend balances after account buffers and confirmed near-term bills.
- **Transactions:** Import OFX, QIF, or selectable-text Halifax PDF statements. Preview and review rows before committing an import, and export transaction data as CSV when needed.
- **Plan:** Configure account safety floors, recurring bills and paydays, transaction-pattern suggestions, monthly category budgets, and optional positive rollover. Only unmatched confirmed occurrences affect safe to spend.
- **Goals:** Create account-backed savings targets and reorder their priority. Finova calculates progress and allocation paths without transferring funds.
- **Reconciliation:** Compare an account ledger with a statement, clear matching transactions, and resolve the closing discrepancy before completing a session.
- **Settings:** Manage household preferences, accounts and opening balances, categories, automatic rules, themes, and data portability.

Finova calculates planning balances from account opening values and imported activity. It does not connect to banks or move money.

Halifax PDF import supports statements containing selectable text. Image-only scans must be processed with OCR before import; Finova rejects them instead of risking an incomplete import.

Private goal images are included in household archives. Restored archives may be up to 50 MB compressed and 100 MB expanded.

If a page cannot load, use its retry action and check the service health endpoints in [Troubleshooting](#troubleshooting). For import problems, confirm the file format and verify that PDF statements contain selectable text.

## Technology

- React and Vite frontend
- ASP.NET Core 8 API
- PostgreSQL 17
- Alembic database migrations
- Nginx production frontend and API gateway
- Docker Compose development and production stacks

### Requirements

Install Docker with the Compose plugin. No host installation of .NET, Node.js, Python, Alembic, or PostgreSQL is required.

## Contributing

Contributions are welcome. Read [CONTRIBUTING.md](CONTRIBUTING.md) before opening a pull request and follow the [Code of Conduct](CODE_OF_CONDUCT.md).

For general help, see [SUPPORT.md](SUPPORT.md). Report suspected vulnerabilities privately according to [SECURITY.md](SECURITY.md); never post credentials, financial records, database dumps, backup archives, or private images in a public issue.

### Development

Create the local environment file:

```sh
cp .env.dev.example .env.dev
```

Start the complete development stack:

```sh
docker compose --env-file .env.dev -f compose.dev.yml up --build
```

The development services are available at:

| Service | Address |
| --- | --- |
| Frontend | <http://localhost:5173> |
| API liveness | <http://localhost:5153/status/live> |
| API readiness | <http://localhost:5153/status/ready> |
| Swagger | <http://localhost:5153/swagger> |
| PostgreSQL | `localhost:55432` |

Browser API requests use `/api` through the Vite proxy. The API and frontend source directories are bind-mounted into their containers for hot reload.

Stop the stack while retaining the development database:

```sh
docker compose --env-file .env.dev -f compose.dev.yml down
```

To permanently delete the development database and all other development volumes:

```sh
docker compose --env-file .env.dev -f compose.dev.yml down --volumes
```

> [!WARNING]
> The development and production stacks use different Compose project names and Docker volumes. They do not share database data.

## Database migrations

The PostgreSQL image creates the database named by `POSTGRES_DB`. Alembic manages everything inside that database.

Every Compose startup runs `alembic upgrade head` after PostgreSQL passes its health check. The API starts only after the migration completes successfully.

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

> [!CAUTION]
> Test downgrades only against a disposable database. A downgrade can destroy application data.

## Development workflows

### Formatting

The development stack must be running before you use these scripts:

```sh
./scripts/format-backend.sh
./scripts/format-frontend.sh
./scripts/format-all.sh
```

Backend formatting uses `dotnet format` for the API and test projects. Frontend formatting uses Prettier and then runs the existing formatting check.

### Tests

Run the complete local CI-equivalent suite through the development containers:

```sh
./scripts/check-all.sh
```

The orchestrator builds and starts the development stack, then runs backend, frontend, migration, backup and restore, production-image, container-pin-policy, and Semgrep checks. The recovery check:

1. Migrates a disposable source database.
2. Uploads its dump to Azurite.
3. Restores the dump under a new name.
4. Validates its data and revision.
5. Confirms that overwrite attempts are refused.
6. Removes all test data.

CodeQL remains GitHub-only.

Each `scripts/check-*.sh` entry point can also be run independently. Set `FINOVA_ENV_FILE` to use an environment file other than `.env.dev` or `.env.dev.example`.

Run the disposable PostgreSQL API integration tests and Playwright household workflow:

```sh
./scripts/check-integration.sh
```

The runner creates a uniquely named Compose project and database, runs the browser workflow before the serial database tests on clean state, and removes its containers and volumes on exit.

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

The development stack exposes Swagger at <http://localhost:5153/swagger> for the typed Finova APIs. Legacy upload and reporting routes remain available as compatibility adapters.

## Release automation

Pull requests and pushes to `main` run the complete hosted CI gate. A successful `push` run publishes four commit-tagged images using GitHub's short-lived, repository-scoped `GITHUB_TOKEN`; no personal access token or repository secret is required.

After all four images exist, the publisher moves the four convenience `latest` tags to that commit. Commit tags remain the authoritative coordinated release references.

After the first publish, the repository owner must make these packages public in each package's settings so deployments can pull them anonymously:

- `finance-app-api`
- `finance-app-frontend`
- `finance-app-migrations`
- `finance-app-backup`

> [!WARNING]
> GitHub does not allow a public package to be made private again. Keep the packages private until the first published images have been inspected.

Configure the `main` branch ruleset to require the uniquely named `CI gate` status. If administrators may bypass human approval, keep the CI requirement in a ruleset with no bypass actors, and put the required-review rule in a second ruleset with repository administrators set to pull-request-only bypass.

## Backups and recovery

Production runs a backup scheduler at 02:00 UTC each day and retains 14 days by default. Each PostgreSQL custom-format dump is uploaded as an immutable Blob with a SHA-256 checksum under:

```text
<database>/YYYY/MM/DD/<database>_YYYYMMDDTHHMMSSZ.dump
```

Azurite is private to the Compose network and persists to `azurite_prod_data`, separately from the PostgreSQL volume. Before the first production start, generate a base64 account key and place it in `.env.prod`:

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

The restore command verifies Blob checksum metadata, refuses an existing target, creates the new database, restores without ownership or privilege statements, and verifies its Alembic revision. If restoration fails, it removes only the new partial database.

Inspect the restored database before cutover:

```sh
docker compose --env-file .env.prod -f compose.prod.yml exec db \
  psql -U finances_app -d finances_restore_20260831 -c "SELECT version_num FROM alembic_version;"
```

Use the configured `POSTGRES_USER` if it differs from `finances_app`.

### Cut over or roll back

After validating the restored database:

1. Stop writes:

   ```sh
   docker compose --env-file .env.prod -f compose.prod.yml stop frontend api
   ```

2. Change `POSTGRES_DB` in `.env.prod` to the restored database name.

3. Start the required services:

   ```sh
   docker compose --env-file .env.prod -f compose.prod.yml up --detach migrations api frontend
   ```

4. Confirm readiness:

   ```sh
   curl --fail http://localhost:8080/api/status/ready
   ```

The original database is not changed or deleted. To roll back, stop the frontend and API, restore the original `POSTGRES_DB` value, and start migrations, the API, and the frontend again.

> [!NOTE]
> Azurite is a development-oriented storage emulator running on the same Docker host. These backups protect against PostgreSQL-volume corruption and accidental database loss, but not total host loss. Snapshot or copy the `azurite_prod_data` volume off-host for host-level disaster recovery.

## Environment variables

| Variable | Purpose | Example or default |
| --- | --- | --- |
| `POSTGRES_HOST` | Database hostname used by the API and Alembic | `db` |
| `POSTGRES_PORT` | Database container port | `5432` |
| `POSTGRES_DB` | Database name | `finances_db` |
| `POSTGRES_USER` | Application/database role | `finances_app` |
| `POSTGRES_PASSWORD` | Database password | Development-only example |
| `POSTGRES_SSL_MODE` | Npgsql SSL mode | `Disable` |
| `POSTGRES_HOST_PORT` | Development host database port | `55432` |
| `API_PORT` | Development host API port | `5153` |
| `FRONTEND_PORT` | Development host frontend port | `5173` |
| `APP_PORT` | Production Nginx host port | `8080` |
| `FINOVA_IMAGE_PREFIX` | GHCR prefix shared by the four application images | `ghcr.io/james-joslin/finance-app` |
| `FINOVA_IMAGE_TAG` | Coordinated application release tag | `latest` |
| `AZURITE_ACCOUNT_NAME` | Private Blob account used by the backup service | `finovadev` |
| `AZURITE_ACCOUNT_KEY` | Base64 account key shared by Azurite and backup tooling | Development emulator key |
| `BACKUP_BLOB_CONTAINER` | Blob container that holds database dumps | `database-backups` |
| `BACKUP_CRON` | Five-field UTC backup schedule | `0 2 * * *` |
| `BACKUP_RETENTION_DAYS` | Age after which matching database blobs are deleted | `14` |

Real `.env.dev` and `.env.prod` files are ignored by Git. Only the example files should be committed.

## Schema and uploads

The initial Alembic revision creates the dump-derived `people`, `accounts`, and `transactions` tables. The additive Finova revision preserves those records and adds household settings, account safety fields, categories and payee rules, savings goals and private images, recurring items, and budget definitions and snapshots.

Goal images are stored in PostgreSQL so the documented database backup includes all private application data. Uploads accept PNG, JPEG, and WebP files up to 2 MB; SVG uploads are rejected.

## Troubleshooting

If the API does not start, check migration output first:

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

## License

Copyright (C) 2026 James Joslin.

Finova is licensed under the [GNU Affero General Public License version 3](LICENSE). Modified versions made available to users over a network are subject to the source-availability requirements in the license.
