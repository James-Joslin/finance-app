"""Add the Finova household planning schema.

Revision ID: 20260808_0002
Revises: 20260807_0001
Create Date: 2026-08-08
"""

from typing import Sequence, Union

from alembic import op
import sqlalchemy as sa


revision: str = "20260808_0002"
down_revision: Union[str, None] = "20260807_0001"
branch_labels: Union[str, Sequence[str], None] = None
depends_on: Union[str, Sequence[str], None] = None


def upgrade() -> None:
    op.create_table(
        "household_settings",
        sa.Column("id", sa.SmallInteger(), nullable=False),
        sa.Column("household_name", sa.Text(), nullable=False),
        sa.Column("currency_code", sa.String(length=3), nullable=False),
        sa.Column("locale", sa.String(length=20), nullable=False),
        sa.Column("timezone", sa.String(length=80), nullable=False),
        sa.Column("updated_at", sa.DateTime(timezone=True), server_default=sa.text("CURRENT_TIMESTAMP"), nullable=False),
        sa.CheckConstraint("id = 1", name="household_settings_singleton"),
        sa.PrimaryKeyConstraint("id"),
    )
    op.execute(
        """INSERT INTO household_settings (id, household_name, currency_code, locale, timezone)
        VALUES (1, 'Matthews Household', 'GBP', 'en-GB', 'Europe/London')"""
    )

    op.add_column("accounts", sa.Column("account_type", sa.String(length=24), server_default="current", nullable=False))
    op.add_column("accounts", sa.Column("institution", sa.Text(), nullable=True))
    op.add_column("accounts", sa.Column("last_four", sa.String(length=4), nullable=True))
    op.add_column("accounts", sa.Column("safe_zone_amount", sa.Numeric(12, 2), server_default="0", nullable=False))
    op.add_column("accounts", sa.Column("include_in_safe_to_spend", sa.Boolean(), server_default=sa.text("true"), nullable=False))
    op.add_column("accounts", sa.Column("is_archived", sa.Boolean(), server_default=sa.text("false"), nullable=False))
    op.create_check_constraint("accounts_safe_zone_non_negative", "accounts", "safe_zone_amount >= 0")

    op.create_table(
        "categories",
        sa.Column("id", sa.Integer(), autoincrement=True, nullable=False),
        sa.Column("name", sa.Text(), nullable=False),
        sa.Column("kind", sa.String(length=16), nullable=False),
        sa.Column("icon_key", sa.String(length=40), nullable=False),
        sa.Column("color_key", sa.String(length=24), nullable=False),
        sa.Column("is_system", sa.Boolean(), server_default=sa.text("false"), nullable=False),
        sa.Column("is_archived", sa.Boolean(), server_default=sa.text("false"), nullable=False),
        sa.UniqueConstraint("name", name="categories_name_key"),
        sa.PrimaryKeyConstraint("id"),
    )
    categories = [
        ("Income", "income", "wallet-cards", "mint"),
        ("Housing", "expense", "house", "blue"),
        ("Food & Groceries", "expense", "shopping-basket", "amber"),
        ("Transport", "expense", "car", "cyan"),
        ("Shopping", "expense", "shopping-bag", "violet"),
        ("Bills & Utilities", "expense", "receipt", "orange"),
        ("Entertainment", "expense", "popcorn", "pink"),
        ("Health", "expense", "heart-pulse", "red"),
        ("Transfers", "transfer", "arrow-left-right", "slate"),
        ("Uncategorised", "expense", "circle-help", "slate"),
    ]
    for name, kind, icon, color in categories:
        op.execute(
            sa.text("INSERT INTO categories (name, kind, icon_key, color_key, is_system) VALUES (:name, :kind, :icon, :color, true)")
            .bindparams(name=name, kind=kind, icon=icon, color=color)
        )

    op.add_column("transactions", sa.Column("category_id", sa.Integer(), nullable=True))
    op.add_column("transactions", sa.Column("status", sa.String(length=16), server_default="completed", nullable=False))
    op.add_column("transactions", sa.Column("is_transfer", sa.Boolean(), server_default=sa.text("false"), nullable=False))
    op.create_foreign_key("transactions_category_id_fkey", "transactions", "categories", ["category_id"], ["id"])
    op.create_index("ix_transactions_account_date", "transactions", ["account_id", "transaction_date"])
    op.create_index("ix_transactions_category", "transactions", ["category_id"])
    op.execute(
        """UPDATE transactions t SET category_id = c.id
        FROM categories c WHERE lower(trim(t.category)) = lower(c.name)"""
    )
    op.execute(
        """UPDATE transactions SET category_id = (SELECT id FROM categories WHERE name = 'Uncategorised')
        WHERE category_id IS NULL"""
    )
    op.execute(
        """UPDATE transactions SET is_transfer = true
        WHERE category_id = (SELECT id FROM categories WHERE name = 'Transfers')"""
    )

    op.create_table(
        "transaction_rules",
        sa.Column("id", sa.Integer(), autoincrement=True, nullable=False),
        sa.Column("match_text", sa.Text(), nullable=False),
        sa.Column("category_id", sa.Integer(), nullable=False),
        sa.Column("priority", sa.Integer(), server_default="100", nullable=False),
        sa.Column("is_active", sa.Boolean(), server_default=sa.text("true"), nullable=False),
        sa.ForeignKeyConstraint(["category_id"], ["categories.id"]),
        sa.PrimaryKeyConstraint("id"),
    )

    op.create_table(
        "goal_images",
        sa.Column("id", sa.Integer(), autoincrement=True, nullable=False),
        sa.Column("content_type", sa.String(length=20), nullable=False),
        sa.Column("file_name", sa.Text(), nullable=False),
        sa.Column("content", sa.LargeBinary(), nullable=False),
        sa.Column("content_hash", sa.String(length=64), nullable=False),
        sa.Column("created_at", sa.DateTime(timezone=True), server_default=sa.text("CURRENT_TIMESTAMP"), nullable=False),
        sa.PrimaryKeyConstraint("id"),
    )

    op.create_table(
        "savings_goals",
        sa.Column("id", sa.Integer(), autoincrement=True, nullable=False),
        sa.Column("name", sa.Text(), nullable=False),
        sa.Column("description", sa.Text(), nullable=True),
        sa.Column("target_amount", sa.Numeric(12, 2), nullable=False),
        sa.Column("target_date", sa.Date(), nullable=True),
        sa.Column("account_id", sa.Integer(), nullable=False),
        sa.Column("priority_order", sa.Integer(), nullable=False),
        sa.Column("icon_key", sa.String(length=40), server_default="general_target", nullable=False),
        sa.Column("color_key", sa.String(length=24), server_default="blue", nullable=False),
        sa.Column("image_id", sa.Integer(), nullable=True),
        sa.Column("status", sa.String(length=16), server_default="active", nullable=False),
        sa.Column("created_at", sa.DateTime(timezone=True), server_default=sa.text("CURRENT_TIMESTAMP"), nullable=False),
        sa.Column("updated_at", sa.DateTime(timezone=True), server_default=sa.text("CURRENT_TIMESTAMP"), nullable=False),
        sa.CheckConstraint("target_amount > 0", name="savings_goals_target_positive"),
        sa.ForeignKeyConstraint(["account_id"], ["accounts.id"]),
        sa.ForeignKeyConstraint(["image_id"], ["goal_images.id"], ondelete="SET NULL"),
        sa.PrimaryKeyConstraint("id"),
    )
    op.create_index("ix_savings_goals_priority", "savings_goals", ["priority_order"])

    op.create_table(
        "recurring_items",
        sa.Column("id", sa.Integer(), autoincrement=True, nullable=False),
        sa.Column("name", sa.Text(), nullable=False),
        sa.Column("kind", sa.String(length=12), nullable=False),
        sa.Column("account_id", sa.Integer(), nullable=False),
        sa.Column("category_id", sa.Integer(), nullable=True),
        sa.Column("amount", sa.Numeric(12, 2), nullable=False),
        sa.Column("frequency", sa.String(length=16), nullable=False),
        sa.Column("next_date", sa.Date(), nullable=False),
        sa.Column("source", sa.String(length=16), server_default="manual", nullable=False),
        sa.Column("is_active", sa.Boolean(), server_default=sa.text("true"), nullable=False),
        sa.Column("created_at", sa.DateTime(timezone=True), server_default=sa.text("CURRENT_TIMESTAMP"), nullable=False),
        sa.ForeignKeyConstraint(["account_id"], ["accounts.id"]),
        sa.ForeignKeyConstraint(["category_id"], ["categories.id"]),
        sa.PrimaryKeyConstraint("id"),
    )
    op.create_index("ix_recurring_items_account_date", "recurring_items", ["account_id", "next_date"])

    op.create_table(
        "budget_definitions",
        sa.Column("id", sa.Integer(), autoincrement=True, nullable=False),
        sa.Column("category_id", sa.Integer(), nullable=False),
        sa.Column("monthly_amount", sa.Numeric(12, 2), nullable=False),
        sa.Column("rollover_enabled", sa.Boolean(), server_default=sa.text("false"), nullable=False),
        sa.Column("effective_from", sa.Date(), nullable=False),
        sa.Column("is_active", sa.Boolean(), server_default=sa.text("true"), nullable=False),
        sa.Column("updated_at", sa.DateTime(timezone=True), server_default=sa.text("CURRENT_TIMESTAMP"), nullable=False),
        sa.CheckConstraint("monthly_amount >= 0", name="budget_definitions_amount_non_negative"),
        sa.ForeignKeyConstraint(["category_id"], ["categories.id"]),
        sa.UniqueConstraint("category_id", name="budget_definitions_category_key"),
        sa.PrimaryKeyConstraint("id"),
    )

    op.create_table(
        "budget_months",
        sa.Column("id", sa.Integer(), autoincrement=True, nullable=False),
        sa.Column("budget_id", sa.Integer(), nullable=False),
        sa.Column("month", sa.Date(), nullable=False),
        sa.Column("base_amount", sa.Numeric(12, 2), nullable=False),
        sa.Column("rollover_in", sa.Numeric(12, 2), server_default="0", nullable=False),
        sa.Column("spent_amount", sa.Numeric(12, 2), server_default="0", nullable=False),
        sa.ForeignKeyConstraint(["budget_id"], ["budget_definitions.id"], ondelete="CASCADE"),
        sa.UniqueConstraint("budget_id", "month", name="budget_months_budget_month_key"),
        sa.PrimaryKeyConstraint("id"),
    )


def downgrade() -> None:
    op.drop_table("budget_months")
    op.drop_table("budget_definitions")
    op.drop_index("ix_recurring_items_account_date", table_name="recurring_items")
    op.drop_table("recurring_items")
    op.drop_index("ix_savings_goals_priority", table_name="savings_goals")
    op.drop_table("savings_goals")
    op.drop_table("goal_images")
    op.drop_table("transaction_rules")
    op.drop_index("ix_transactions_category", table_name="transactions")
    op.drop_index("ix_transactions_account_date", table_name="transactions")
    op.drop_constraint("transactions_category_id_fkey", "transactions", type_="foreignkey")
    op.drop_column("transactions", "is_transfer")
    op.drop_column("transactions", "status")
    op.drop_column("transactions", "category_id")
    op.drop_table("categories")
    op.drop_constraint("accounts_safe_zone_non_negative", "accounts", type_="check")
    op.drop_column("accounts", "is_archived")
    op.drop_column("accounts", "include_in_safe_to_spend")
    op.drop_column("accounts", "safe_zone_amount")
    op.drop_column("accounts", "last_four")
    op.drop_column("accounts", "institution")
    op.drop_column("accounts", "account_type")
    op.drop_table("household_settings")
