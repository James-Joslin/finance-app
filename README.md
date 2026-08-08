# Finova

A private, responsive household finance hub for the Matthews Household. Finova combines:

- Safe-to-spend balances after per-account buffers and confirmed near-term bills.
- Multiple prioritised savings goals with account-backed waterfall allocation and countdowns.
- Recurring bills and paydays with transaction-pattern suggestions.
- Monthly category budgets with optional positive rollover.
- Typed transaction review, categorisation rules, OFX/QIF import, and CSV export.
- Household insights, global search, responsive layouts, and persistent light/dark themes.

Finova is intentionally login-free for use on a trusted private network. It does not connect to banks
or move money; balances come from opening values and imported transactions.

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
- API health: http://localhost:5153/status/health
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

Create a compressed production dump on the Docker host:

```sh
docker compose --env-file .env.prod -f compose.prod.yml exec -T db \
  pg_dump -U finances_app -d finances_db -Fc > finances_db.dump
```

If `POSTGRES_USER` or `POSTGRES_DB` differs in `.env.prod`, use those values in the command.

Periodically test restoring backups into a separate disposable PostgreSQL instance. Do not test restoration against the production volume.

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
curl --fail http://localhost:5173/api/status/health
```

If a published development port is already in use, change its corresponding value in `.env.dev`.

