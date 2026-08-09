"""Add Halifax transaction codes shown on current statements.

Revision ID: 20260809_0007
Revises: 20260809_0006
Create Date: 2026-08-09
"""

from typing import Sequence, Union

from alembic import op
import sqlalchemy as sa


revision: str = "20260809_0007"
down_revision: Union[str, None] = "20260809_0006"
branch_labels: Union[str, Sequence[str], None] = None
depends_on: Union[str, Sequence[str], None] = None


def upgrade() -> None:
    codes = [
        ("BGC", "Bank Giro Credit"),
        ("BP", "Bill Payments"),
        ("CHG", "Charge"),
        ("CHQ", "Cheque"),
        ("COR", "Correction"),
        ("CPT", "Cashpoint"),
        ("DD", "Direct Debit"),
        ("DEB", "Debit Card"),
        ("DEP", "Deposit"),
        ("FEE", "Fixed Service"),
        ("FPI", "Faster Payment In"),
        ("FPO", "Faster Payment Out"),
        ("MPI", "Mobile Payment In"),
        ("MPO", "Mobile Payment Out"),
        ("PAY", "Payment"),
        ("SO", "Standing Order"),
        ("TFR", "Transfer"),
    ]
    statement = sa.text(
        """INSERT INTO transaction_type_codes (code, meaning, institution)
        VALUES (:code, :meaning, 'Halifax')
        ON CONFLICT (code) DO UPDATE SET
            meaning = excluded.meaning, institution = excluded.institution, is_active = true"""
    )
    for code, meaning in codes:
        op.execute(statement.bindparams(code=code, meaning=meaning))


def downgrade() -> None:
    op.execute("DELETE FROM transaction_type_codes WHERE code IN ('CHG', 'CHQ', 'COR', 'CPT', 'FEE')")
    op.execute("UPDATE transaction_type_codes SET meaning = 'Bill Payment' WHERE code = 'BP'")
    op.execute("UPDATE transaction_type_codes SET meaning = 'Mobile Payment Incoming' WHERE code = 'MPI'")
    op.execute("UPDATE transaction_type_codes SET meaning = 'Mobile Payment Outgoing' WHERE code = 'MPO'")
