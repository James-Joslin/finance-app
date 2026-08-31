#!/usr/bin/env python3

import argparse
import hashlib
import json
import os
import re
import subprocess
import sys
import tempfile
import time
from datetime import datetime, timedelta, timezone
from pathlib import Path
from typing import Any

from azure.core.exceptions import ResourceExistsError
from azure.storage.blob import BlobServiceClient
from croniter import croniter


DATABASE_IDENTIFIER = re.compile(r"^[a-z_][a-z0-9_]*$")


def log_event(event: str, level: str = "information", **values: Any) -> None:
    record = {
        "timestamp": datetime.now(timezone.utc).isoformat(),
        "level": level,
        "event": event,
        **values,
    }
    print(json.dumps(record, separators=(",", ":"), sort_keys=True), flush=True)


def required_environment(name: str) -> str:
    value = os.getenv(name)
    if not value:
        raise RuntimeError(f"Required environment setting {name} is missing.")
    return value


def database_environment() -> dict[str, str]:
    return {
        "host": required_environment("POSTGRES_HOST"),
        "port": os.getenv("POSTGRES_PORT", "5432"),
        "database": required_environment("POSTGRES_DB"),
        "user": required_environment("POSTGRES_USER"),
        "password": required_environment("POSTGRES_PASSWORD"),
    }


def connection_string() -> str:
    configured = os.getenv("BACKUP_STORAGE_CONNECTION_STRING")
    if configured:
        return configured
    account = required_environment("AZURITE_ACCOUNT_NAME")
    key = required_environment("AZURITE_ACCOUNT_KEY")
    endpoint = os.getenv("BACKUP_BLOB_ENDPOINT", f"http://azurite:10000/{account}")
    return (
        "DefaultEndpointsProtocol=http;"
        f"AccountName={account};AccountKey={key};BlobEndpoint={endpoint};"
    )


def blob_container():
    service = BlobServiceClient.from_connection_string(connection_string())
    container = service.get_container_client(
        os.getenv("BACKUP_BLOB_CONTAINER", "database-backups")
    )
    try:
        container.create_container()
        log_event("blob_container_created", container=container.container_name)
    except ResourceExistsError:
        pass
    return container


def postgres_environment(settings: dict[str, str]) -> dict[str, str]:
    environment = os.environ.copy()
    environment["PGPASSWORD"] = settings["password"]
    return environment


def run_command(
    command: list[str],
    settings: dict[str, str],
    *,
    capture_output: bool = False,
) -> subprocess.CompletedProcess[str]:
    return subprocess.run(
        command,
        check=True,
        text=True,
        stdout=subprocess.PIPE if capture_output else subprocess.DEVNULL,
        stderr=subprocess.PIPE,
        env=postgres_environment(settings),
    )


def postgres_arguments(settings: dict[str, str]) -> list[str]:
    return [
        "--host",
        settings["host"],
        "--port",
        settings["port"],
        "--username",
        settings["user"],
    ]


def safe_path_component(value: str) -> str:
    return re.sub(r"[^A-Za-z0-9_.-]", "_", value)


def file_sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def should_delete_blob(
    blob_name: str,
    last_modified: datetime,
    database_prefix: str,
    cutoff: datetime,
) -> bool:
    return blob_name.startswith(f"{database_prefix}/") and last_modified < cutoff


def remove_expired_backups(container, database_prefix: str, now: datetime) -> int:
    retention_days = int(os.getenv("BACKUP_RETENTION_DAYS", "14"))
    if retention_days < 1:
        raise RuntimeError("BACKUP_RETENTION_DAYS must be at least 1.")
    cutoff = now - timedelta(days=retention_days)
    deleted = 0
    for blob in container.list_blobs(name_starts_with=f"{database_prefix}/"):
        if should_delete_blob(blob.name, blob.last_modified, database_prefix, cutoff):
            container.delete_blob(blob.name)
            deleted += 1
            log_event("backup_expired_deleted", blob=blob.name)
    return deleted


def run_backup() -> str:
    settings = database_environment()
    now = datetime.now(timezone.utc)
    database_prefix = safe_path_component(settings["database"])
    blob_name = (
        f"{database_prefix}/{now:%Y/%m/%d}/"
        f"{database_prefix}_{now:%Y%m%dT%H%M%SZ}.dump"
    )
    temporary_path: Path | None = None
    log_event("backup_started", database=settings["database"], blob=blob_name)
    try:
        with tempfile.NamedTemporaryFile(suffix=".dump", delete=False) as temporary:
            temporary_path = Path(temporary.name)
        run_command(
            [
                "pg_dump",
                *postgres_arguments(settings),
                "--dbname",
                settings["database"],
                "--format=custom",
                "--no-owner",
                "--file",
                str(temporary_path),
            ],
            settings,
        )
        checksum = file_sha256(temporary_path)
        size = temporary_path.stat().st_size
        container = blob_container()
        metadata = {
            "sha256": checksum,
            "database": database_prefix,
            "created_utc": now.isoformat(),
            "size": str(size),
        }
        with temporary_path.open("rb") as stream:
            container.upload_blob(
                name=blob_name,
                data=stream,
                metadata=metadata,
                overwrite=False,
            )
        properties = container.get_blob_client(blob_name).get_blob_properties()
        if properties.size != size or properties.metadata.get("sha256") != checksum:
            raise RuntimeError("Uploaded backup verification failed.")
        deleted = remove_expired_backups(container, database_prefix, now)
        log_event(
            "backup_completed",
            database=settings["database"],
            blob=blob_name,
            bytes=size,
            sha256=checksum,
            expired_deleted=deleted,
        )
        return blob_name
    finally:
        if temporary_path is not None:
            temporary_path.unlink(missing_ok=True)


def database_exists(settings: dict[str, str], target: str) -> bool:
    maintenance = os.getenv("POSTGRES_MAINTENANCE_DB", "postgres")
    result = run_command(
        [
            "psql",
            *postgres_arguments(settings),
            "--dbname",
            maintenance,
            "--tuples-only",
            "--no-align",
            "--command",
            f"SELECT 1 FROM pg_database WHERE datname = '{target}';",
        ],
        settings,
        capture_output=True,
    )
    return result.stdout.strip() == "1"


def create_database(settings: dict[str, str], target: str) -> None:
    run_command(
        ["createdb", *postgres_arguments(settings), target],
        settings,
    )


def drop_database(settings: dict[str, str], target: str) -> None:
    maintenance = os.getenv("POSTGRES_MAINTENANCE_DB", "postgres")
    run_command(
        [
            "dropdb",
            *postgres_arguments(settings),
            "--maintenance-db",
            maintenance,
            "--if-exists",
            target,
        ],
        settings,
    )


def validate_restored_database(settings: dict[str, str], target: str) -> str:
    result = run_command(
        [
            "psql",
            *postgres_arguments(settings),
            "--dbname",
            target,
            "--tuples-only",
            "--no-align",
            "--command",
            "SELECT version_num FROM alembic_version;",
        ],
        settings,
        capture_output=True,
    )
    revision = result.stdout.strip()
    if not revision or "\n" in revision:
        raise RuntimeError("Restored database has an invalid Alembic revision.")
    return revision


def run_restore(blob_name: str, target: str) -> str:
    if not DATABASE_IDENTIFIER.fullmatch(target):
        raise RuntimeError(
            "Restore target must be a lowercase PostgreSQL identifier containing only letters, digits, and underscores."
        )
    settings = database_environment()
    if database_exists(settings, target):
        raise RuntimeError("Restore target already exists; existing databases are never overwritten.")

    container = blob_container()
    blob = container.get_blob_client(blob_name)
    properties = blob.get_blob_properties()
    expected_checksum = properties.metadata.get("sha256")
    if not expected_checksum:
        raise RuntimeError("Backup blob has no SHA-256 metadata.")

    temporary_path: Path | None = None
    created = False
    log_event("restore_started", blob=blob_name, target_database=target)
    try:
        with tempfile.NamedTemporaryFile(suffix=".dump", delete=False) as temporary:
            temporary_path = Path(temporary.name)
            blob.download_blob().readinto(temporary)
        actual_checksum = file_sha256(temporary_path)
        if actual_checksum != expected_checksum:
            raise RuntimeError("Downloaded backup checksum does not match blob metadata.")

        create_database(settings, target)
        created = True
        run_command(
            [
                "pg_restore",
                *postgres_arguments(settings),
                "--dbname",
                target,
                "--exit-on-error",
                "--no-owner",
                "--no-privileges",
                str(temporary_path),
            ],
            settings,
        )
        revision = validate_restored_database(settings, target)
        log_event(
            "restore_completed",
            blob=blob_name,
            target_database=target,
            sha256=actual_checksum,
            alembic_revision=revision,
        )
        return revision
    except Exception:
        if created:
            drop_database(settings, target)
            log_event("restore_partial_database_removed", target_database=target)
        raise
    finally:
        if temporary_path is not None:
            temporary_path.unlink(missing_ok=True)


def list_backups() -> None:
    names = sorted(blob.name for blob in blob_container().list_blobs())
    log_event("backups_listed", count=len(names), blobs=names)


def delete_backup(blob_name: str) -> None:
    blob_container().delete_blob(blob_name)
    log_event("backup_deleted", blob=blob_name)


def run_schedule() -> None:
    expression = os.getenv("BACKUP_CRON", "0 2 * * *")
    if not croniter.is_valid(expression):
        raise RuntimeError("BACKUP_CRON is not a valid cron expression.")
    log_event("backup_scheduler_started", cron=expression, timezone="UTC")
    while True:
        now = datetime.now(timezone.utc)
        next_run = croniter(expression, now).get_next(datetime)
        delay = max(0.0, (next_run - now).total_seconds())
        log_event("backup_scheduled", next_run=next_run.isoformat())
        time.sleep(delay)
        try:
            run_backup()
        except Exception as exception:
            log_event(
                "backup_failed",
                level="error",
                error_type=type(exception).__name__,
            )


def parse_arguments() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Finova database backup and restore utility")
    commands = parser.add_subparsers(dest="command", required=True)
    commands.add_parser("backup")
    commands.add_parser("schedule")
    commands.add_parser("list")
    restore = commands.add_parser("restore")
    restore.add_argument("--blob", required=True)
    restore.add_argument("--target-database", required=True)
    delete = commands.add_parser("delete")
    delete.add_argument("--blob", required=True)
    return parser.parse_args()


def main() -> int:
    arguments = parse_arguments()
    try:
        if arguments.command == "backup":
            run_backup()
        elif arguments.command == "schedule":
            run_schedule()
        elif arguments.command == "list":
            list_backups()
        elif arguments.command == "restore":
            run_restore(arguments.blob, arguments.target_database)
        elif arguments.command == "delete":
            delete_backup(arguments.blob)
        return 0
    except Exception as exception:
        log_event(
            f"{arguments.command}_failed",
            level="error",
            error_type=type(exception).__name__,
        )
        return 1


if __name__ == "__main__":
    sys.exit(main())
