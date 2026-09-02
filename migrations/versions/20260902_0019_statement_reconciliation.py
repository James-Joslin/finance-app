"""Add statement reconciliation sessions and cleared transactions.

Revision ID: 20260902_0019
Revises: 20260902_0018
Create Date: 2026-09-02
"""

from typing import Sequence, Union

from alembic import op
import sqlalchemy as sa


revision: str = "20260902_0019"
down_revision: Union[str, None] = "20260902_0018"
branch_labels: Union[str, Sequence[str], None] = None
depends_on: Union[str, Sequence[str], None] = None


def upgrade() -> None:
    op.add_column(
        "transactions",
        sa.Column("cleared", sa.Boolean(), server_default=sa.text("false"), nullable=False),
    )
    op.add_column(
        "transactions",
        sa.Column(
            "is_reconciliation_adjustment",
            sa.Boolean(),
            server_default=sa.text("false"),
            nullable=False,
        ),
    )
    op.execute(
        """
        UPDATE transactions
        SET cleared = true
        WHERE source_file_type IS NOT NULL AND upper(source_file_type) <> 'MANUAL'
        """
    )
    op.create_index("ix_transactions_account_cleared_date", "transactions", ["account_id", "cleared", "transaction_date"])

    op.execute(
        """
        INSERT INTO categories (name, kind, icon_key, color_key, is_system)
        VALUES ('Reconciliation adjustment', 'expense', 'scale', 'slate', true)
        ON CONFLICT (name) DO NOTHING
        """
    )

    op.create_table(
        "statement_sessions",
        sa.Column("id", sa.Integer(), autoincrement=True, nullable=False),
        sa.Column("account_id", sa.Integer(), nullable=False),
        sa.Column("period_start", sa.Date(), nullable=False),
        sa.Column("period_end", sa.Date(), nullable=False),
        sa.Column("statement_opening_balance", sa.Numeric(precision=12, scale=2), nullable=False),
        sa.Column("statement_closing_balance", sa.Numeric(precision=12, scale=2), nullable=False),
        sa.Column("status", sa.String(length=16), server_default="open", nullable=False),
        sa.Column("created_at", sa.DateTime(timezone=True), server_default=sa.text("CURRENT_TIMESTAMP"), nullable=False),
        sa.Column("closed_at", sa.DateTime(timezone=True), nullable=True),
        sa.ForeignKeyConstraint(["account_id"], ["accounts.id"]),
        sa.PrimaryKeyConstraint("id"),
        sa.UniqueConstraint("account_id", "period_start", "period_end", name="statement_sessions_account_period_key"),
        sa.CheckConstraint("period_end >= period_start", name="statement_sessions_period_valid"),
        sa.CheckConstraint("status IN ('open', 'closed')", name="statement_sessions_status_valid"),
    )
    op.create_index(
        "ix_statement_sessions_account_period",
        "statement_sessions",
        ["account_id", "period_start", "period_end"],
    )
    op.create_index(
        "ix_statement_sessions_open_account",
        "statement_sessions",
        ["account_id"],
        postgresql_where=sa.text("status = 'open'"),
        unique=True,
    )

    op.create_table(
        "statement_session_transactions",
        sa.Column("session_id", sa.Integer(), nullable=False),
        sa.Column("transaction_id", sa.Integer(), nullable=False),
        sa.Column("created_at", sa.DateTime(timezone=True), server_default=sa.text("CURRENT_TIMESTAMP"), nullable=False),
        sa.ForeignKeyConstraint(["session_id"], ["statement_sessions.id"], ondelete="CASCADE"),
        sa.ForeignKeyConstraint(["transaction_id"], ["transactions.id"]),
        sa.PrimaryKeyConstraint("session_id", "transaction_id"),
        sa.UniqueConstraint("transaction_id", name="statement_session_transactions_transaction_key"),
    )
    op.create_index(
        "ix_statement_session_transactions_transaction",
        "statement_session_transactions",
        ["transaction_id"],
    )


def downgrade() -> None:
    op.drop_index("ix_statement_session_transactions_transaction", table_name="statement_session_transactions")
    op.drop_table("statement_session_transactions")
    op.drop_index("ix_statement_sessions_open_account", table_name="statement_sessions")
    op.drop_index("ix_statement_sessions_account_period", table_name="statement_sessions")
    op.drop_table("statement_sessions")
    op.drop_index("ix_transactions_account_cleared_date", table_name="transactions")
    op.drop_column("transactions", "is_reconciliation_adjustment")
    op.drop_column("transactions", "cleared")
    op.execute("DELETE FROM categories WHERE name = 'Reconciliation adjustment' AND is_system")
