"""Add editable recurring matching and occurrence tracking.

Revision ID: 20260809_0008
Revises: 20260809_0007
Create Date: 2026-08-09
"""

from typing import Sequence, Union

from alembic import op
import sqlalchemy as sa


revision: str = "20260809_0008"
down_revision: Union[str, None] = "20260809_0007"
branch_labels: Union[str, Sequence[str], None] = None
depends_on: Union[str, Sequence[str], None] = None


def upgrade() -> None:
    op.add_column("recurring_items", sa.Column("match_text", sa.Text(), nullable=True))
    op.add_column(
        "recurring_items",
        sa.Column("amount_tolerance", sa.Numeric(12, 2), server_default="5.00", nullable=False),
    )
    op.add_column(
        "recurring_items",
        sa.Column("date_window_days", sa.SmallInteger(), server_default="5", nullable=False),
    )
    op.add_column(
        "recurring_items",
        sa.Column("source_transaction_id", sa.Integer(), nullable=True),
    )
    op.add_column(
        "recurring_items",
        sa.Column(
            "updated_at",
            sa.DateTime(timezone=True),
            server_default=sa.text("CURRENT_TIMESTAMP"),
            nullable=False,
        ),
    )
    op.create_foreign_key(
        "recurring_items_source_transaction_id_fkey",
        "recurring_items",
        "transactions",
        ["source_transaction_id"],
        ["id"],
        ondelete="SET NULL",
    )
    op.create_check_constraint(
        "recurring_items_amount_tolerance_non_negative",
        "recurring_items",
        "amount_tolerance >= 0",
    )
    op.create_check_constraint(
        "recurring_items_date_window_valid",
        "recurring_items",
        "date_window_days BETWEEN 0 AND 31",
    )

    op.create_table(
        "recurring_occurrences",
        sa.Column("id", sa.Integer(), autoincrement=True, nullable=False),
        sa.Column("recurring_item_id", sa.Integer(), nullable=False),
        sa.Column("due_date", sa.Date(), nullable=False),
        sa.Column("expected_amount", sa.Numeric(12, 2), nullable=False),
        sa.Column("status", sa.String(length=16), server_default="expected", nullable=False),
        sa.Column("transaction_id", sa.Integer(), nullable=True),
        sa.Column("actual_amount", sa.Numeric(12, 2), nullable=True),
        sa.Column("matched_at", sa.DateTime(timezone=True), nullable=True),
        sa.Column("note", sa.Text(), nullable=True),
        sa.Column("created_at", sa.DateTime(timezone=True), server_default=sa.text("CURRENT_TIMESTAMP"), nullable=False),
        sa.Column("updated_at", sa.DateTime(timezone=True), server_default=sa.text("CURRENT_TIMESTAMP"), nullable=False),
        sa.CheckConstraint("expected_amount >= 0", name="recurring_occurrences_amount_non_negative"),
        sa.CheckConstraint(
            "status IN ('expected', 'matched', 'paid', 'skipped')",
            name="recurring_occurrences_status_valid",
        ),
        sa.ForeignKeyConstraint(["recurring_item_id"], ["recurring_items.id"], ondelete="CASCADE"),
        sa.ForeignKeyConstraint(["transaction_id"], ["transactions.id"], ondelete="SET NULL"),
        sa.PrimaryKeyConstraint("id"),
        sa.UniqueConstraint("recurring_item_id", "due_date", name="recurring_occurrences_item_date_key"),
        sa.UniqueConstraint("transaction_id", name="recurring_occurrences_transaction_key"),
    )
    op.create_index(
        "ix_recurring_occurrences_status_date",
        "recurring_occurrences",
        ["status", "due_date"],
    )


def downgrade() -> None:
    op.drop_index("ix_recurring_occurrences_status_date", table_name="recurring_occurrences")
    op.drop_table("recurring_occurrences")
    op.drop_constraint("recurring_items_date_window_valid", "recurring_items", type_="check")
    op.drop_constraint("recurring_items_amount_tolerance_non_negative", "recurring_items", type_="check")
    op.drop_constraint("recurring_items_source_transaction_id_fkey", "recurring_items", type_="foreignkey")
    op.drop_column("recurring_items", "updated_at")
    op.drop_column("recurring_items", "source_transaction_id")
    op.drop_column("recurring_items", "date_window_days")
    op.drop_column("recurring_items", "amount_tolerance")
    op.drop_column("recurring_items", "match_text")
