"""Add categorized split lines for manual transactions."""

from typing import Sequence, Union

from alembic import op
import sqlalchemy as sa


revision: str = "20260902_0018"
down_revision: Union[str, None] = "20260902_0017"
branch_labels: Union[str, Sequence[str], None] = None
depends_on: Union[str, None] = None


def upgrade() -> None:
    op.create_table(
        "transaction_splits",
        sa.Column("id", sa.Integer(), autoincrement=True, nullable=False),
        sa.Column("transaction_id", sa.Integer(), nullable=False),
        sa.Column("category_id", sa.Integer(), nullable=False),
        sa.Column("amount", sa.Numeric(precision=12, scale=2), nullable=False),
        sa.Column("memo", sa.Text(), nullable=True),
        sa.Column("line_order", sa.SmallInteger(), nullable=False),
        sa.ForeignKeyConstraint(["transaction_id"], ["transactions.id"], ondelete="CASCADE"),
        sa.ForeignKeyConstraint(["category_id"], ["categories.id"]),
        sa.PrimaryKeyConstraint("id"),
        sa.UniqueConstraint("transaction_id", "line_order", name="transaction_splits_transaction_order_key"),
        sa.CheckConstraint("amount > 0", name="transaction_splits_amount_positive"),
        sa.CheckConstraint("line_order >= 0", name="transaction_splits_order_non_negative"),
    )
    op.create_index("ix_transaction_splits_transaction", "transaction_splits", ["transaction_id"])
    op.create_index("ix_transaction_splits_category", "transaction_splits", ["category_id"])


def downgrade() -> None:
    op.drop_index("ix_transaction_splits_category", table_name="transaction_splits")
    op.drop_index("ix_transaction_splits_transaction", table_name="transaction_splits")
    op.drop_table("transaction_splits")
