"""Add immutable audit snapshots and request metadata.

Revision ID: 0002_audit_snapshots
Revises: 0001_initial_saas_schema
"""
from alembic import op
import sqlalchemy as sa

revision = "0002_audit_snapshots"
down_revision = "0001_initial_saas_schema"
branch_labels = None
depends_on = None


def upgrade() -> None:
    op.add_column("audit_logs", sa.Column("before_data", sa.JSON(), nullable=True))
    op.add_column("audit_logs", sa.Column("after_data", sa.JSON(), nullable=True))
    op.add_column("audit_logs", sa.Column("user_agent", sa.String(length=500), nullable=True))
    op.add_column("audit_logs", sa.Column("correlation_id", sa.String(length=80), nullable=True))


def downgrade() -> None:
    op.drop_column("audit_logs", "correlation_id")
    op.drop_column("audit_logs", "user_agent")
    op.drop_column("audit_logs", "after_data")
    op.drop_column("audit_logs", "before_data")
