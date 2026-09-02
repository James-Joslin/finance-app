"""Add durable transaction transfer pairs."""

from typing import Sequence, Union

from alembic import op
import sqlalchemy as sa


revision: str = "20260902_0017"
down_revision: Union[str, None] = "20260831_0016"
branch_labels: Union[str, Sequence[str], None] = None
depends_on: Union[str, Sequence[str], None] = None


def upgrade() -> None:
    op.create_table(
        "transaction_transfer_pairs",
        sa.Column("id", sa.Integer(), autoincrement=True, nullable=False),
        sa.Column("transaction_id_a", sa.Integer(), nullable=False),
        sa.Column("transaction_id_b", sa.Integer(), nullable=False),
        sa.Column("created_at", sa.DateTime(timezone=True), server_default=sa.text("CURRENT_TIMESTAMP"), nullable=False),
        sa.CheckConstraint("transaction_id_a < transaction_id_b", name="transfer_pairs_ordered"),
        sa.ForeignKeyConstraint(["transaction_id_a"], ["transactions.id"], ondelete="CASCADE"),
        sa.ForeignKeyConstraint(["transaction_id_b"], ["transactions.id"], ondelete="CASCADE"),
        sa.PrimaryKeyConstraint("id"),
        sa.UniqueConstraint("transaction_id_a", name="transfer_pairs_a_key"),
        sa.UniqueConstraint("transaction_id_b", name="transfer_pairs_b_key"),
    )
    op.create_index("ix_transfer_pairs_transactions", "transaction_transfer_pairs", ["transaction_id_a", "transaction_id_b"])


def downgrade() -> None:
    op.drop_index("ix_transfer_pairs_transactions", table_name="transaction_transfer_pairs")
    op.drop_table("transaction_transfer_pairs")
