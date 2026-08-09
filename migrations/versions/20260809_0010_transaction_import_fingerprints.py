"""Add database-enforced transaction import fingerprints.

Revision ID: 20260809_0010
Revises: 20260809_0009
Create Date: 2026-08-09
"""

from typing import Sequence, Union

from alembic import op
import sqlalchemy as sa


revision: str = "20260809_0010"
down_revision: Union[str, None] = "20260809_0009"
branch_labels: Union[str, Sequence[str], None] = None
depends_on: Union[str, Sequence[str], None] = None


def upgrade() -> None:
    op.add_column("transactions", sa.Column("import_fingerprint", sa.String(length=32), nullable=True))
    op.execute(
        r"""
        WITH prepared AS (
            SELECT id, account_id, fitid,
                concat_ws('|',
                    to_char(transaction_date, 'YYYY-MM-DD'),
                    to_char(amount, 'FM9999999990.00'),
                    regexp_replace(lower(trim(coalesce(payee, ''))), '\s+', ' ', 'g'),
                    regexp_replace(lower(trim(coalesce(memo, ''))), '\s+', ' ', 'g'),
                    regexp_replace(lower(trim(coalesce(transaction_type, ''))), '\s+', ' ', 'g'),
                    regexp_replace(lower(trim(coalesce(check_number, ''))), '\s+', ' ', 'g')
                ) AS base_text
            FROM transactions
        ), numbered AS (
            SELECT *, row_number() OVER (
                PARTITION BY account_id, base_text ORDER BY id
            ) AS row_occurrence
            FROM prepared
        ), fingerprinted AS (
            SELECT id, account_id,
                md5(CASE
                    WHEN nullif(trim(fitid), '') IS NOT NULL THEN 'id|' || lower(trim(fitid))
                    ELSE 'row|' || base_text || '|' || row_occurrence::text
                END) AS fingerprint
            FROM numbered
        ), ranked AS (
            SELECT *, row_number() OVER (
                PARTITION BY account_id, fingerprint ORDER BY id
            ) AS duplicate_rank
            FROM fingerprinted
        )
        UPDATE transactions t
        SET import_fingerprint = CASE WHEN ranked.duplicate_rank = 1 THEN ranked.fingerprint ELSE NULL END
        FROM ranked WHERE ranked.id=t.id
        """
    )
    op.create_index(
        "transactions_account_import_fingerprint_key",
        "transactions",
        ["account_id", "import_fingerprint"],
        unique=True,
        postgresql_where="import_fingerprint IS NOT NULL",
    )


def downgrade() -> None:
    op.drop_index("transactions_account_import_fingerprint_key", table_name="transactions")
    op.drop_column("transactions", "import_fingerprint")
