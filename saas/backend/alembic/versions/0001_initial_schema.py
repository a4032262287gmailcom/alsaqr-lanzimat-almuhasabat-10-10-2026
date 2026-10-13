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


def downgrade() -> None:
    bind = op.get_bind()
    for table_name in reversed(TENANT_TABLES):
        op.execute(f'DROP POLICY IF EXISTS "tenant_isolation_{table_name}" ON "{table_name}"')
        op.execute(f'ALTER TABLE "{table_name}" DISABLE ROW LEVEL SECURITY')
    Base.metadata.drop_all(bind=bind)
