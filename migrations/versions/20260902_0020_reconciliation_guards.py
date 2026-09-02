"""Guard imported cleared defaults and closed reconciliation transactions."""

from typing import Sequence, Union

from alembic import op


revision: str = "20260902_0020"
down_revision: Union[str, None] = "20260902_0019"
branch_labels: Union[str, Sequence[str], None] = None
depends_on: Union[str, None] = None


def upgrade() -> None:
    op.execute(
        """
        CREATE FUNCTION set_reconciliation_cleared_default()
        RETURNS trigger AS $$
        BEGIN
            IF NEW.source_file_type IS NOT NULL
               AND upper(NEW.source_file_type) <> 'MANUAL' THEN
                NEW.cleared := true;
            END IF;
            RETURN NEW;
        END;
        $$ LANGUAGE plpgsql;

        CREATE TRIGGER transactions_reconciliation_cleared_default
        BEFORE INSERT ON transactions
        FOR EACH ROW EXECUTE FUNCTION set_reconciliation_cleared_default();

        CREATE FUNCTION prevent_closed_statement_transaction_mutation()
        RETURNS trigger AS $$
        BEGIN
            IF EXISTS (
                SELECT 1
                FROM statement_session_transactions link
                JOIN statement_sessions session ON session.id = link.session_id
                WHERE link.transaction_id = OLD.id AND session.status = 'closed'
            ) THEN
                RAISE EXCEPTION 'Transactions in a closed statement session are immutable'
                    USING ERRCODE = 'restrict_violation';
            END IF;
            RETURN CASE WHEN TG_OP = 'DELETE' THEN OLD ELSE NEW END;
        END;
        $$ LANGUAGE plpgsql;

        CREATE TRIGGER transactions_closed_statement_immutable
        BEFORE UPDATE OR DELETE ON transactions
        FOR EACH ROW EXECUTE FUNCTION prevent_closed_statement_transaction_mutation();
        """
    )


def downgrade() -> None:
    op.execute("DROP TRIGGER transactions_closed_statement_immutable ON transactions")
    op.execute("DROP FUNCTION prevent_closed_statement_transaction_mutation()")
    op.execute("DROP TRIGGER transactions_reconciliation_cleared_default ON transactions")
    op.execute("DROP FUNCTION set_reconciliation_cleared_default()")
