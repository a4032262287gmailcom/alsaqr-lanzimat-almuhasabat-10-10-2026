"""Add audit before/after snapshots and request metadata.

Revision ID: 0002_audit_snapshots
Revises: 0001_initial_saas_schema
"""
from alembic import op

revision = "0002_audit_snapshots"
down_revision = "0001_initial_saas_schema"
branch_labels = None
depends_on = None


def upgrade() -> None:
    # The original bootstrap migration imports current model metadata, so these
    # guarded DDL statements work both for a clean install and an upgraded DB.
    op.execute("ALTER TABLE audit_logs ADD COLUMN IF NOT EXISTS before_data JSON")
    op.execute("ALTER TABLE audit_logs ADD COLUMN IF NOT EXISTS after_data JSON")
    op.execute("ALTER TABLE audit_logs ADD COLUMN IF NOT EXISTS user_agent VARCHAR(500)")
    op.execute("ALTER TABLE audit_logs ADD COLUMN IF NOT EXISTS correlation_id VARCHAR(80)")


def downgrade() -> None:
    op.execute("ALTER TABLE audit_logs DROP COLUMN IF EXISTS correlation_id")
    op.execute("ALTER TABLE audit_logs DROP COLUMN IF EXISTS user_agent")
    op.execute("ALTER TABLE audit_logs DROP COLUMN IF EXISTS after_data")
    op.execute("ALTER TABLE audit_logs DROP COLUMN IF EXISTS before_data")
