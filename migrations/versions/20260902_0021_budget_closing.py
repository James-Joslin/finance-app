"""Add durable budget month closures and finalized snapshots."""

from typing import Sequence, Union

from alembic import op
import sqlalchemy as sa


revision: str = "20260902_0021"
down_revision: Union[str, None] = "20260902_0020"
branch_labels: Union[str, Sequence[str], None] = None
depends_on: Union[str, None] = None


def upgrade() -> None:
    op.create_table(
        "budget_month_closures",
        sa.Column("month", sa.Date(), nullable=False),
        sa.Column("closed_at", sa.DateTime(timezone=True), server_default=sa.text("CURRENT_TIMESTAMP"), nullable=False),
        sa.PrimaryKeyConstraint("month"),
    )
    for name in (
        "available_amount",
        "scheduled_amount",
        "remaining_after_scheduled",
        "remaining_amount",
        "progress_percent",
    ):
        op.add_column("budget_months", sa.Column(name, sa.Numeric(12, 2), nullable=True))
    op.create_check_constraint(
        "budget_months_progress_non_negative",
        "budget_months",
        "progress_percent IS NULL OR progress_percent >= 0",
    )
    op.execute(
        """
        CREATE FUNCTION prevent_finalized_budget_month_mutation()
        RETURNS trigger AS $$
        BEGIN
            IF EXISTS (
                SELECT 1 FROM budget_month_closures WHERE month = OLD.month
            ) THEN
                RAISE EXCEPTION 'Finalized budget months are immutable'
                    USING ERRCODE = 'restrict_violation';
            END IF;
            RETURN CASE WHEN TG_OP = 'DELETE' THEN OLD ELSE NEW END;
        END;
        $$ LANGUAGE plpgsql;

        CREATE TRIGGER budget_months_finalized_immutable
        BEFORE UPDATE OR DELETE ON budget_months
        FOR EACH ROW EXECUTE FUNCTION prevent_finalized_budget_month_mutation();
        """
    )


def downgrade() -> None:
    op.execute("DROP TRIGGER budget_months_finalized_immutable ON budget_months")
    op.execute("DROP FUNCTION prevent_finalized_budget_month_mutation()")
    op.drop_constraint("budget_months_progress_non_negative", "budget_months", type_="check")
    for name in (
        "progress_percent",
        "remaining_amount",
        "remaining_after_scheduled",
        "scheduled_amount",
        "available_amount",
    ):
        op.drop_column("budget_months", name)
    op.drop_table("budget_month_closures")
