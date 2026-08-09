"""Add transaction type abbreviation lookup data.

Revision ID: 20260809_0005
Revises: 20260808_0004
Create Date: 2026-08-09
"""

from typing import Sequence, Union

from alembic import op
import sqlalchemy as sa


revision: str = "20260809_0005"
down_revision: Union[str, None] = "20260808_0004"
branch_labels: Union[str, Sequence[str], None] = None
depends_on: Union[str, Sequence[str], None] = None


def upgrade() -> None:
    op.create_table(
        "transaction_type_codes",
        sa.Column("code", sa.String(length=8), nullable=False),
        sa.Column("meaning", sa.Text(), nullable=False),
        sa.Column("institution", sa.Text(), nullable=False),
        sa.Column("is_active", sa.Boolean(), server_default=sa.text("true"), nullable=False),
        sa.PrimaryKeyConstraint("code"),
    )
    codes = [
        ("BGC", "Bank Giro Credit"),
        ("BP", "Bill Payment"),
        ("CD", "Card transaction (shows last 4 digits of card)"),
        ("CSQ", "Cash/Cheque"),
        ("DD", "Direct Debit"),
        ("DEB", "Debit Card"),
        ("MPI", "Mobile Payment Incoming"),
        ("MPO", "Mobile Payment Outgoing"),
        ("MTU", "Mobile Top-Up"),
        ("DEP", "Deposit"),
        ("DR", "Overdrawn Balance (Debit)"),
        ("EUR", "Euro Cheque"),
        ("FPI", "Faster Payment In"),
        ("FPO", "Faster Payment Out"),
        ("IB", "Internet Banking"),
        ("PAY", "Payment"),
        ("PSV", "Paysave"),
        ("SAL", "Salary"),
        ("SO", "Standing Order"),
        ("TFR", "Transfer"),
    ]
    for code, meaning in codes:
        op.execute(
            sa.text(
                "INSERT INTO transaction_type_codes (code, meaning, institution) "
                "VALUES (:code, :meaning, 'Halifax')"
            ).bindparams(code=code, meaning=meaning)
        )


def downgrade() -> None:
    op.drop_table("transaction_type_codes")
