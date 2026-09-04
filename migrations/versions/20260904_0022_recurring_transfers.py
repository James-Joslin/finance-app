"""Allow recurring transfer plans."""

from typing import Sequence, Union

from alembic import op


revision: str = "20260904_0022"
down_revision: Union[str, None] = "20260902_0021"
branch_labels: Union[str, Sequence[str], None] = None
depends_on: Union[str, Sequence[str], None] = None


def upgrade() -> None:
    op.drop_constraint("recurring_items_kind_valid", "recurring_items", type_="check")
    op.create_check_constraint(
        "recurring_items_kind_valid",
        "recurring_items",
        "kind IN ('bill', 'income', 'transfer')",
    )


def downgrade() -> None:
    op.drop_constraint("recurring_items_kind_valid", "recurring_items", type_="check")
    op.create_check_constraint(
        "recurring_items_kind_valid",
        "recurring_items",
        "kind IN ('bill', 'income')",
    )
