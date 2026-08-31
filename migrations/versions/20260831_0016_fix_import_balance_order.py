"""Calculate import preview balances in chronological order.

Revision ID: 20260831_0016
Revises: 20260831_0015
Create Date: 2026-08-31
"""

from alembic import op


revision = "20260831_0016"
down_revision = "20260831_0015"
branch_labels = None
depends_on = None


def upgrade() -> None:
    op.execute(
        """
        WITH directions AS (
            SELECT
                batches.id AS batch_id,
                (
                    SELECT first_row.transaction_date
                    FROM transaction_import_rows AS first_row
                    WHERE first_row.batch_id = batches.id
                      AND first_row.transaction_date IS NOT NULL
                    ORDER BY first_row.ordinal
                    LIMIT 1
                ) > (
                    SELECT last_row.transaction_date
                    FROM transaction_import_rows AS last_row
                    WHERE last_row.batch_id = batches.id
                      AND last_row.transaction_date IS NOT NULL
                    ORDER BY last_row.ordinal DESC
                    LIMIT 1
                ) AS source_descending
            FROM transaction_import_batches AS batches
            WHERE batches.status = 'preview'
        )
        UPDATE transaction_import_rows AS rows
        SET balance_after = CASE
            WHEN rows.transaction_date IS NULL OR rows.amount IS NULL THEN NULL
            ELSE batches.starting_balance + COALESCE((
                SELECT SUM(previous.amount)
                FROM transaction_import_rows AS previous
                WHERE previous.batch_id = rows.batch_id
                  AND previous.outcome = 'ready'
                  AND (
                      previous.transaction_date < rows.transaction_date
                      OR (previous.transaction_date = rows.transaction_date AND (
                          (COALESCE(directions.source_descending, false)
                              AND previous.ordinal >= rows.ordinal)
                          OR (NOT COALESCE(directions.source_descending, false)
                              AND previous.ordinal <= rows.ordinal)
                      ))
                  )
            ), 0)
        END
        FROM transaction_import_batches AS batches, directions
        WHERE rows.batch_id = batches.id
          AND directions.batch_id = batches.id
          AND batches.status = 'preview'
        """
    )


def downgrade() -> None:
    # Data-only correction; the prior source-order values cannot be recovered
    # reliably after previews have been created or committed.
    pass
