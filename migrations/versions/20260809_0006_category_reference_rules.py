"""Make category reference rules directional and auditable.

Revision ID: 20260809_0006
Revises: 20260809_0005
Create Date: 2026-08-09
"""

from typing import Sequence, Union

from alembic import op
import sqlalchemy as sa


revision: str = "20260809_0006"
down_revision: Union[str, None] = "20260809_0005"
branch_labels: Union[str, Sequence[str], None] = None
depends_on: Union[str, Sequence[str], None] = None


def upgrade() -> None:
    op.add_column("transaction_rules", sa.Column("direction", sa.String(length=3), server_default="any", nullable=False))
    op.add_column(
        "transaction_rules",
        sa.Column("created_at", sa.DateTime(timezone=True), server_default=sa.text("CURRENT_TIMESTAMP"), nullable=False),
    )
    op.add_column(
        "transaction_rules",
        sa.Column("updated_at", sa.DateTime(timezone=True), server_default=sa.text("CURRENT_TIMESTAMP"), nullable=False),
    )
    op.create_check_constraint(
        "transaction_rules_direction_valid", "transaction_rules", "direction IN ('in', 'out', 'any')"
    )


def downgrade() -> None:
    op.drop_constraint("transaction_rules_direction_valid", "transaction_rules", type_="check")
    op.drop_column("transaction_rules", "updated_at")
    op.drop_column("transaction_rules", "created_at")
    op.drop_column("transaction_rules", "direction")
