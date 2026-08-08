"""Model credit-card limits and liability semantics.

Revision ID: 20260808_0004
Revises: 20260808_0003
Create Date: 2026-08-08
"""

from typing import Sequence, Union

from alembic import op
import sqlalchemy as sa


revision: str = "20260808_0004"
down_revision: Union[str, None] = "20260808_0003"
branch_labels: Union[str, Sequence[str], None] = None
depends_on: Union[str, Sequence[str], None] = None


def upgrade() -> None:
    op.add_column("accounts", sa.Column("credit_limit", sa.Numeric(12, 2), nullable=True))
    op.create_check_constraint("accounts_credit_limit_non_negative", "accounts", "credit_limit IS NULL OR credit_limit >= 0")
    op.execute("UPDATE accounts SET include_in_safe_to_spend = false, safe_zone_amount = 0 WHERE account_type = 'credit'")


def downgrade() -> None:
    op.drop_constraint("accounts_credit_limit_non_negative", "accounts", type_="check")
    op.drop_column("accounts", "credit_limit")
