"""Backfill projected balances for open import previews.

Revision ID: 20260831_0015
Revises: 20260831_0014
Create Date: 2026-08-31
"""

from alembic import op


revision = "20260831_0015"
down_revision = "20260831_0014"
branch_labels = None
depends_on = None


def upgrade() -> None:
    op.execute(
        """
        WITH account_balances AS (
            SELECT
                accounts.id AS account_id,
                COALESCE(SUM(transactions.amount), 0)::numeric(12, 2) AS balance
            FROM accounts
            LEFT JOIN transactions ON transactions.account_id = accounts.id
            GROUP BY accounts.id
        )
        UPDATE transaction_import_batches AS batches
        SET starting_balance = account_balances.balance
        FROM account_balances
        WHERE batches.account_id = account_balances.account_id
          AND batches.status = 'preview'
        """
    )

    op.execute(
        """
        UPDATE transaction_import_rows AS rows
        SET balance_after = batches.starting_balance + COALESCE((
            SELECT SUM(previous.amount)
            FROM transaction_import_rows AS previous
            WHERE previous.batch_id = rows.batch_id
              AND previous.ordinal <= rows.ordinal
              AND previous.outcome = 'ready'
        ), 0)
        FROM transaction_import_batches AS batches
        WHERE rows.batch_id = batches.id
          AND batches.status = 'preview'
        """
    )


def downgrade() -> None:
    # This is a data-only compatibility backfill; there is no reliable way to
    # distinguish these values from previews created after the schema change.
    pass
