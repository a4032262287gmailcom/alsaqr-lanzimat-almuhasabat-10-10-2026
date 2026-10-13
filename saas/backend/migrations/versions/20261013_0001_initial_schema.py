"""Initial multi-tenant accounting schema and PostgreSQL RLS policies."""
from alembic import op

from app.models import Base  # Frozen initial bootstrap; future changes must use explicit revisions.

revision = "20261013_0001"
down_revision = None
branch_labels = None
depends_on = None

TENANT_TABLES = (
    "roles",
    "membership_roles",
    "role_permissions",
    "branches",
    "fiscal_years",
    "accounting_periods",
    "accounts",
    "cost_centers",
    "sequence_counters",
    "journal_entries",
    "journal_items",
    "audit_logs",
    "contacts",
    "products",
    "product_variants",
    "warehouses",
    "stock_moves",
    "invoices",
    "invoice_lines",
    "exchange_rates",
)


def upgrade() -> None:
    bind = op.get_bind()
    Base.metadata.create_all(bind=bind)
    for table_name in TENANT_TABLES:
        op.execute(f'ALTER TABLE "{table_name}" ENABLE ROW LEVEL SECURITY')
        op.execute(f'ALTER TABLE "{table_name}" FORCE ROW LEVEL SECURITY')
        op.execute(
            f'CREATE POLICY tenant_isolation ON "{table_name}" '
            "USING (tenant_id = nullif(current_setting('app.current_tenant_id', true), '')::uuid) "
            "WITH CHECK (tenant_id = nullif(current_setting('app.current_tenant_id', true), '')::uuid)"
        )


def downgrade() -> None:
    bind = op.get_bind()
    for table_name in reversed(TENANT_TABLES):
        op.execute(f'DROP POLICY IF EXISTS tenant_isolation ON "{table_name}"')
        op.execute(f'ALTER TABLE "{table_name}" DISABLE ROW LEVEL SECURITY')
    Base.metadata.drop_all(bind=bind)
