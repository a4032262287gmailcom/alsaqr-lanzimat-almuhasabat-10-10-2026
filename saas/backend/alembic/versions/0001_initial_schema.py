"""Initial multi-tenant ERP schema and PostgreSQL RLS policies."""
from alembic import op

from app.models import Base

revision = "0001_initial_saas_schema"
down_revision = None
branch_labels = None
depends_on = None

TENANT_TABLES = (
    "accounts",
    "cost_centers",
    "contacts",
    "warehouses",
    "products",
    "product_variants",
    "stock_balances",
    "stock_moves",
    "number_sequences",
    "fiscal_periods",
    "journal_entries",
    "journal_lines",
    "invoices",
    "invoice_lines",
    "audit_logs",
    "bills_of_materials",
    "bill_of_material_lines",
)


def upgrade() -> None:
    bind = op.get_bind()
    Base.metadata.create_all(bind=bind)
    for table_name in TENANT_TABLES:
        policy_name = f"tenant_isolation_{table_name}"
        op.execute(f'ALTER TABLE "{table_name}" ENABLE ROW LEVEL SECURITY')
        op.execute(f'ALTER TABLE "{table_name}" FORCE ROW LEVEL SECURITY')
        expression = "tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid"
        op.execute(
            f'CREATE POLICY "{policy_name}" ON "{table_name}" '
            f"USING ({expression}) WITH CHECK ({expression})"
        )

    op.execute("""
    CREATE OR REPLACE FUNCTION saas_block_posted_journal_mutation()
    RETURNS trigger
    LANGUAGE plpgsql
    AS $
    BEGIN
        IF TG_OP = 'DELETE' THEN
            IF OLD.status IN ('POSTED', 'REVERSED') THEN
                RAISE EXCEPTION 'Posted or reversed journal entries cannot be deleted'
                    USING ERRCODE = '55000';
            END IF;
            RETURN OLD;
        END IF;

        IF OLD.status IN ('POSTED', 'REVERSED') THEN
            IF OLD.status = 'POSTED' AND NEW.status = 'REVERSED' THEN
                IF NEW.tenant_id IS DISTINCT FROM OLD.tenant_id
                   OR NEW.number IS DISTINCT FROM OLD.number
                   OR NEW.entry_date IS DISTINCT FROM OLD.entry_date
                   OR NEW.memo IS DISTINCT FROM OLD.memo
                   OR NEW.source_type IS DISTINCT FROM OLD.source_type
                   OR NEW.source_id IS DISTINCT FROM OLD.source_id
                   OR NEW.reversal_of_id IS DISTINCT FROM OLD.reversal_of_id
                   OR NEW.posted_by IS DISTINCT FROM OLD.posted_by
                   OR NEW.posted_at IS DISTINCT FROM OLD.posted_at
                   OR NEW.created_at IS DISTINCT FROM OLD.created_at THEN
                    RAISE EXCEPTION 'A journal reversal may change only the original status'
                        USING ERRCODE = '55000';
                END IF;
            ELSE
                RAISE EXCEPTION 'Posted or reversed journal entries are immutable; create a reversal instead'
                    USING ERRCODE = '55000';
            END IF;
        END IF;
        RETURN NEW;
    END;
    $;
    """)

    op.execute("""
    CREATE TRIGGER trg_block_posted_journal_mutation
    BEFORE UPDATE OR DELETE ON journal_entries
    FOR EACH ROW EXECUTE FUNCTION saas_block_posted_journal_mutation();
    """)

    op.execute("""
    CREATE OR REPLACE FUNCTION saas_assert_posted_journal_balanced()
    RETURNS trigger
    LANGUAGE plpgsql
    AS $
    DECLARE
        target_tenant uuid;
        target_entry uuid;
        entry_status text;
        line_count bigint;
        debit_total numeric;
        credit_total numeric;
    BEGIN
        IF TG_TABLE_NAME = 'journal_entries' THEN
            IF TG_OP = 'DELETE' THEN
                target_tenant := OLD.tenant_id;
                target_entry := OLD.id;
            ELSE
                target_tenant := NEW.tenant_id;
                target_entry := NEW.id;
            END IF;
        ELSE
            IF TG_OP = 'DELETE' THEN
                target_tenant := OLD.tenant_id;
                target_entry := OLD.entry_id;
            ELSE
                target_tenant := NEW.tenant_id;
                target_entry := NEW.entry_id;
            END IF;
        END IF;

        SELECT status
          INTO entry_status
          FROM journal_entries
         WHERE tenant_id = target_tenant
           AND id = target_entry;

        IF NOT FOUND OR entry_status = 'DRAFT' THEN
            RETURN NULL;
        END IF;

        SELECT COUNT(*),
               COALESCE(SUM(debit), 0::numeric),
               COALESCE(SUM(credit), 0::numeric)
          INTO line_count, debit_total, credit_total
          FROM journal_lines
         WHERE tenant_id = target_tenant
           AND entry_id = target_entry;

        IF line_count < 2 OR debit_total <= 0 OR debit_total <> credit_total THEN
            RAISE EXCEPTION 'Posted journal % must contain at least two lines and balance debit % to credit %',
                target_entry, debit_total, credit_total
                USING ERRCODE = '23514';
        END IF;
        RETURN NULL;
    END;
    $;
    """)

    op.execute("""
    CREATE CONSTRAINT TRIGGER trg_journal_entry_balanced
    AFTER INSERT OR UPDATE ON journal_entries
    DEFERRABLE INITIALLY DEFERRED
    FOR EACH ROW EXECUTE FUNCTION saas_assert_posted_journal_balanced();
    """)

    op.execute("""
    CREATE CONSTRAINT TRIGGER trg_journal_line_balanced
    AFTER INSERT OR UPDATE OR DELETE ON journal_lines
    DEFERRABLE INITIALLY DEFERRED
    FOR EACH ROW EXECUTE FUNCTION saas_assert_posted_journal_balanced();
    """)

    op.execute("""
    CREATE OR REPLACE FUNCTION saas_reject_audit_log_mutation()
    RETURNS trigger
    LANGUAGE plpgsql
    AS $
    BEGIN
        RAISE EXCEPTION 'Audit log rows are append-only'
            USING ERRCODE = '55000';
    END;
    $;
    """)

    op.execute("""
    CREATE TRIGGER trg_audit_logs_append_only
    BEFORE UPDATE OR DELETE ON audit_logs
    FOR EACH ROW EXECUTE FUNCTION saas_reject_audit_log_mutation();
    """)


def downgrade() -> None:
    op.execute('DROP TRIGGER IF EXISTS trg_audit_logs_append_only ON audit_logs')
    op.execute('DROP FUNCTION IF EXISTS saas_reject_audit_log_mutation()')
    op.execute('DROP TRIGGER IF EXISTS trg_journal_line_balanced ON journal_lines')
    op.execute('DROP TRIGGER IF EXISTS trg_journal_entry_balanced ON journal_entries')
    op.execute('DROP FUNCTION IF EXISTS saas_assert_posted_journal_balanced()')
    op.execute('DROP TRIGGER IF EXISTS trg_block_posted_journal_mutation ON journal_entries')
    op.execute('DROP FUNCTION IF EXISTS saas_block_posted_journal_mutation()')
    bind = op.get_bind()
    for table_name in reversed(TENANT_TABLES):
        op.execute(f'DROP POLICY IF EXISTS "tenant_isolation_{table_name}" ON "{table_name}"')
        op.execute(f'ALTER TABLE "{table_name}" DISABLE ROW LEVEL SECURITY')
    Base.metadata.drop_all(bind=bind)
