"""Add staged transaction import batches and row outcomes.

Revision ID: 20260831_0013
Revises: 20260830_0012
Create Date: 2026-08-31
"""

from typing import Sequence, Union

from alembic import op
import sqlalchemy as sa


revision: str = "20260831_0013"
down_revision: Union[str, None] = "20260830_0012"
branch_labels: Union[str, Sequence[str], None] = None
depends_on: Union[str, Sequence[str], None] = None


def upgrade() -> None:
    op.create_table(
        "transaction_import_batches",
        sa.Column("id", sa.BigInteger(), autoincrement=True, nullable=False),
        sa.Column("account_id", sa.Integer(), nullable=False),
        sa.Column("file_name", sa.Text(), nullable=False),
        sa.Column("file_type", sa.String(length=10), nullable=False),
        sa.Column("file_size", sa.BigInteger(), nullable=False),
        sa.Column("file_sha256", sa.String(length=64), nullable=False),
        sa.Column("status", sa.String(length=16), server_default="preview", nullable=False),
        sa.Column("total_rows", sa.Integer(), server_default="0", nullable=False),
        sa.Column("importable_rows", sa.Integer(), server_default="0", nullable=False),
        sa.Column("imported_rows", sa.Integer(), server_default="0", nullable=False),
        sa.Column("skipped_rows", sa.Integer(), server_default="0", nullable=False),
        sa.Column("rejected_rows", sa.Integer(), server_default="0", nullable=False),
        sa.Column("created_at", sa.DateTime(timezone=True), server_default=sa.text("CURRENT_TIMESTAMP"), nullable=False),
        sa.Column("expires_at", sa.DateTime(timezone=True), nullable=False),
        sa.Column("completed_at", sa.DateTime(timezone=True), nullable=True),
        sa.Column("undone_at", sa.DateTime(timezone=True), nullable=True),
        sa.CheckConstraint("status IN ('preview', 'completed', 'undone')", name="transaction_import_batches_status_valid"),
        sa.CheckConstraint("file_size >= 0", name="transaction_import_batches_file_size_non_negative"),
        sa.ForeignKeyConstraint(["account_id"], ["accounts.id"]),
        sa.PrimaryKeyConstraint("id"),
    )
    op.create_index(
        "ix_transaction_import_batches_account_created",
        "transaction_import_batches",
        ["account_id", "created_at"],
    )
    op.create_index(
        "ix_transaction_import_batches_preview_expiry",
        "transaction_import_batches",
        ["expires_at"],
        postgresql_where="status = 'preview'",
    )

    op.add_column("transactions", sa.Column("import_batch_id", sa.BigInteger(), nullable=True))
    op.create_foreign_key(
        "transactions_import_batch_id_fkey",
        "transactions",
        "transaction_import_batches",
        ["import_batch_id"],
        ["id"],
        ondelete="SET NULL",
    )
    op.create_index("ix_transactions_import_batch", "transactions", ["import_batch_id"])

    op.create_table(
        "transaction_import_rows",
        sa.Column("id", sa.BigInteger(), autoincrement=True, nullable=False),
        sa.Column("batch_id", sa.BigInteger(), nullable=False),
        sa.Column("ordinal", sa.Integer(), nullable=False),
        sa.Column("source_label", sa.Text(), nullable=False),
        sa.Column("transaction_date", sa.DateTime(timezone=False), nullable=True),
        sa.Column("display_date", sa.Text(), nullable=True),
        sa.Column("amount", sa.Numeric(precision=12, scale=2), nullable=True),
        sa.Column("display_amount", sa.Text(), nullable=True),
        sa.Column("payee", sa.Text(), nullable=True),
        sa.Column("memo", sa.Text(), nullable=True),
        sa.Column("fitid", sa.Text(), nullable=True),
        sa.Column("transaction_type", sa.Text(), nullable=True),
        sa.Column("category", sa.Text(), nullable=True),
        sa.Column("check_number", sa.String(length=50), nullable=True),
        sa.Column("source_file_type", sa.String(length=10), nullable=False),
        sa.Column("statement_balance", sa.Numeric(precision=12, scale=2), nullable=True),
        sa.Column("fingerprint", sa.String(length=32), nullable=True),
        sa.Column("outcome", sa.String(length=16), nullable=False),
        sa.Column("reason_code", sa.String(length=40), nullable=True),
        sa.Column("reason_message", sa.Text(), nullable=True),
        sa.Column("transaction_id", sa.Integer(), nullable=True),
        sa.CheckConstraint(
            "outcome IN ('ready', 'imported', 'skipped', 'rejected', 'undone')",
            name="transaction_import_rows_outcome_valid",
        ),
        sa.ForeignKeyConstraint(["batch_id"], ["transaction_import_batches.id"], ondelete="CASCADE"),
        sa.ForeignKeyConstraint(["transaction_id"], ["transactions.id"], ondelete="SET NULL"),
        sa.PrimaryKeyConstraint("id"),
        sa.UniqueConstraint("batch_id", "ordinal", name="transaction_import_rows_batch_ordinal_key"),
    )
    op.create_index("ix_transaction_import_rows_batch_outcome", "transaction_import_rows", ["batch_id", "outcome"])


def downgrade() -> None:
    op.drop_index("ix_transaction_import_rows_batch_outcome", table_name="transaction_import_rows")
    op.drop_table("transaction_import_rows")
    op.drop_index("ix_transactions_import_batch", table_name="transactions")
    op.drop_constraint("transactions_import_batch_id_fkey", "transactions", type_="foreignkey")
    op.drop_column("transactions", "import_batch_id")
    op.drop_index("ix_transaction_import_batches_preview_expiry", table_name="transaction_import_batches")
    op.drop_index("ix_transaction_import_batches_account_created", table_name="transaction_import_batches")
    op.drop_table("transaction_import_batches")
