# Finance App

A private finance application composed of:

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

The initial Alembic revision creates the dump-derived `people`, `accounts`, and `transactions` tables without seed data. It intentionally adds no extra indexes, uniqueness constraints, cascade behavior, authentication tables, or imported legacy data.

The two API reporting queries are stored under `api/SqlQueries/`; MinIO is no longer required.

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

