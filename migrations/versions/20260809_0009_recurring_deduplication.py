"""Prevent one transaction from creating multiple recurring plans.

Revision ID: 20260809_0009
Revises: 20260809_0008
Create Date: 2026-08-09
"""

from typing import Sequence, Union

from alembic import op


revision: str = "20260809_0009"
down_revision: Union[str, None] = "20260809_0008"
branch_labels: Union[str, Sequence[str], None] = None
depends_on: Union[str, Sequence[str], None] = None


def upgrade() -> None:
    op.create_index(
        "recurring_items_source_transaction_key",
        "recurring_items",
        ["source_transaction_id"],
        unique=True,
        postgresql_where="source_transaction_id IS NOT NULL",
    )


def downgrade() -> None:
    op.drop_index("recurring_items_source_transaction_key", table_name="recurring_items")
