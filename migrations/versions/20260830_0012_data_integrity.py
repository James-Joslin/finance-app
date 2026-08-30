"""Preserve budget history and strengthen data integrity.

Revision ID: 20260830_0012
Revises: 20260809_0011
Create Date: 2026-08-30
"""

from typing import Sequence, Union

from alembic import op
import sqlalchemy as sa


revision: str = "20260830_0012"
down_revision: Union[str, None] = "20260809_0011"
branch_labels: Union[str, Sequence[str], None] = None
depends_on: Union[str, Sequence[str], None] = None


def upgrade() -> None:
    op.add_column(
        "budget_months",
        sa.Column("rollover_enabled", sa.Boolean(), server_default=sa.text("false"), nullable=False),
    )
    op.execute(
        """UPDATE budget_months bm SET rollover_enabled = b.rollover_enabled
        FROM budget_definitions b WHERE b.id = bm.budget_id"""
    )

    op.create_check_constraint(
        "accounts_type_valid", "accounts", "account_type IN ('current', 'savings', 'credit', 'cash', 'investment')"
    )
    op.create_check_constraint(
        "categories_kind_valid", "categories", "kind IN ('income', 'expense', 'transfer')"
    )
    op.create_check_constraint(
        "savings_goals_status_valid", "savings_goals", "status IN ('active', 'completed', 'archived')"
    )
    op.create_check_constraint(
        "recurring_items_kind_valid", "recurring_items", "kind IN ('bill', 'income')"
    )
    op.create_check_constraint(
        "recurring_items_frequency_valid",
        "recurring_items",
        "frequency IN ('weekly', 'fortnightly', 'monthly', 'quarterly', 'yearly')",
    )


def downgrade() -> None:
    op.drop_constraint("recurring_items_frequency_valid", "recurring_items", type_="check")
    op.drop_constraint("recurring_items_kind_valid", "recurring_items", type_="check")
    op.drop_constraint("savings_goals_status_valid", "savings_goals", type_="check")
    op.drop_constraint("categories_kind_valid", "categories", type_="check")
    op.drop_constraint("accounts_type_valid", "accounts", type_="check")
    op.drop_column("budget_months", "rollover_enabled")
