"""Add explicit joint account holder names.

Revision ID: 20260808_0003
Revises: 20260808_0002
Create Date: 2026-08-08
"""

from typing import Sequence, Union

from alembic import op
import sqlalchemy as sa


revision: str = "20260808_0003"
down_revision: Union[str, None] = "20260808_0002"
branch_labels: Union[str, Sequence[str], None] = None
depends_on: Union[str, Sequence[str], None] = None


def upgrade() -> None:
    op.add_column("accounts", sa.Column("primary_holder_name", sa.Text(), nullable=True))
    op.add_column("accounts", sa.Column("secondary_holder_name", sa.Text(), nullable=True))
    op.execute(
        """UPDATE accounts a
        SET primary_holder_name = nullif(trim(concat_ws(' ', p.first_name, p.last_name)), '')
        FROM people p
        WHERE p.id = a.owner_id AND a.primary_holder_name IS NULL"""
    )


def downgrade() -> None:
    op.drop_column("accounts", "secondary_holder_name")
    op.drop_column("accounts", "primary_holder_name")
