"""Persist projected balances for import previews.

Revision ID: 20260831_0014
Revises: 20260831_0013
Create Date: 2026-08-31
"""

from typing import Sequence, Union

from alembic import op
import sqlalchemy as sa


revision: str = "20260831_0014"
down_revision: Union[str, None] = "20260831_0013"
branch_labels: Union[str, Sequence[str], None] = None
depends_on: Union[str, Sequence[str], None] = None


def upgrade() -> None:
    op.add_column(
        "transaction_import_batches",
        sa.Column("starting_balance", sa.Numeric(precision=12, scale=2), server_default="0", nullable=False),
    )
    op.add_column(
        "transaction_import_rows",
        sa.Column("balance_after", sa.Numeric(precision=12, scale=2), nullable=True),
    )


def downgrade() -> None:
    op.drop_column("transaction_import_rows", "balance_after")
    op.drop_column("transaction_import_batches", "starting_balance")
