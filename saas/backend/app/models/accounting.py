from __future__ import annotations

import uuid
from datetime import date, datetime
from decimal import Decimal
from typing import Any

from sqlalchemy import (
    BigInteger, Boolean, CheckConstraint, Date, DateTime, ForeignKey, ForeignKeyConstraint,
    Index, Integer, JSON, Numeric, SmallInteger, String, Text, UniqueConstraint, Uuid,
    func, text,
)
from sqlalchemy.dialects.postgresql import JSONB
from sqlalchemy.orm import Mapped, mapped_column

from app.models.base import Base
from app.models.identity import TimestampMixin

JSON_VALUE = JSON().with_variant(JSONB, "postgresql")


class FiscalYear(Base, TimestampMixin):
    __tablename__ = "fiscal_years"
    id: Mapped[uuid.UUID] = mapped_column(Uuid(as_uuid=True), primary_key=True, default=uuid.uuid4)
    tenant_id: Mapped[uuid.UUID] = mapped_column(Uuid(as_uuid=True), ForeignKey("tenants.id", ondelete="CASCADE"), nullable=False)
    name: Mapped[str] = mapped_column(String(40), nullable=False)
    starts_on: Mapped[date] = mapped_column(Date, nullable=False)
    ends_on: Mapped[date] = mapped_column(Date, nullable=False)
    is_closed: Mapped[bool] = mapped_column(Boolean, nullable=False, default=False, server_default=text("false"))
    __table_args__ = (
        CheckConstraint("ends_on >= starts_on", name="ck_fiscal_year_dates"),
        UniqueConstraint("tenant_id", "name", name="uq_fiscal_year_tenant_name"),
        UniqueConstraint("tenant_id", "id", name="uq_fiscal_year_tenant_id"),
    )


class AccountingPeriod(Base, TimestampMixin):
    __tablename__ = "accounting_periods"
    id: Mapped[uuid.UUID] = mapped_column(Uuid(as_uuid=True), primary_key=True, default=uuid.uuid4)
    tenant_id: Mapped[uuid.UUID] = mapped_column(Uuid(as_uuid=True), ForeignKey("tenants.id", ondelete="CASCADE"), nullable=False)
    fiscal_year_id: Mapped[uuid.UUID] = mapped_column(Uuid(as_uuid=True), nullable=False)
    code: Mapped[str] = mapped_column(String(30), nullable=False)
    starts_on: Mapped[date] = mapped_column(Date, nullable=False)
    ends_on: Mapped[date] = mapped_column(Date, nullable=False)
    status: Mapped[str] = mapped_column(String(12), nullable=False, default="open", server_default="open")
    closed_at: Mapped[datetime | None] = mapped_column(DateTime(timezone=True))
    closed_by: Mapped[uuid.UUID | None] = mapped_column(Uuid(as_uuid=True), ForeignKey("users.id", ondelete="RESTRICT"))
    reopened_at: Mapped[datetime | None] = mapped_column(DateTime(timezone=True))
    reopened_by: Mapped[uuid.UUID | None] = mapped_column(Uuid(as_uuid=True), ForeignKey("users.id", ondelete="RESTRICT"))
    lock_version: Mapped[int] = mapped_column(Integer, nullable=False, default=0, server_default="0")
    __table_args__ = (
        ForeignKeyConstraint(["tenant_id", "fiscal_year_id"], ["fiscal_years.tenant_id", "fiscal_years.id"], ondelete="RESTRICT", name="fk_period_fiscal_year"),
        CheckConstraint("ends_on >= starts_on", name="ck_accounting_period_dates"),
        CheckConstraint("status IN ('open','closed')", name="ck_accounting_period_status"),
        UniqueConstraint("tenant_id", "code", name="uq_period_tenant_code"),
        UniqueConstraint("tenant_id", "id", name="uq_period_tenant_id"),
        Index("ix_period_tenant_range", "tenant_id", "starts_on", "ends_on"),
    )


class Account(Base, TimestampMixin):
    __tablename__ = "accounts"
    id: Mapped[uuid.UUID] = mapped_column(Uuid(as_uuid=True), primary_key=True, default=uuid.uuid4)
    tenant_id: Mapped[uuid.UUID] = mapped_column(Uuid(as_uuid=True), ForeignKey("tenants.id", ondelete="CASCADE"), nullable=False)
    parent_id: Mapped[uuid.UUID | None] = mapped_column(Uuid(as_uuid=True))
    code: Mapped[str] = mapped_column(String(40), nullable=False)
    name_ar: Mapped[str] = mapped_column(String(180), nullable=False)
    name_en: Mapped[str | None] = mapped_column(String(180))
    account_type: Mapped[str] = mapped_column(String(20), nullable=False)
    statement_type: Mapped[str] = mapped_column(String(20), nullable=False)
    nature: Mapped[str] = mapped_column(String(6), nullable=False)
    level: Mapped[int] = mapped_column(SmallInteger, nullable=False, default=1, server_default="1")
    is_postable: Mapped[bool] = mapped_column(Boolean, nullable=False, default=True, server_default=text("true"))
    is_active: Mapped[bool] = mapped_column(Boolean, nullable=False, default=True, server_default=text("true"))
    is_control_account: Mapped[bool] = mapped_column(Boolean, nullable=False, default=False, server_default=text("false"))
    currency_code: Mapped[str] = mapped_column(String(3), nullable=False, default="SAR", server_default="SAR")
    __table_args__ = (
        ForeignKeyConstraint(["tenant_id", "parent_id"], ["accounts.tenant_id", "accounts.id"], ondelete="RESTRICT", name="fk_accounts_parent"),
        CheckConstraint("account_type IN ('asset','liability','equity','revenue','expense')", name="ck_accounts_type"),
        CheckConstraint("statement_type IN ('balance_sheet','income_statement')", name="ck_accounts_statement_type"),
        CheckConstraint("nature IN ('debit','credit')", name="ck_accounts_nature"),
        CheckConstraint("level >= 1", name="ck_accounts_level"),
        UniqueConstraint("tenant_id", "code", name="uq_accounts_tenant_code"),
        UniqueConstraint("tenant_id", "id", name="uq_accounts_tenant_id"),
        Index("ix_accounts_tenant_parent", "tenant_id", "parent_id"),
    )


class CostCenter(Base, TimestampMixin):
    __tablename__ = "cost_centers"
    id: Mapped[uuid.UUID] = mapped_column(Uuid(as_uuid=True), primary_key=True, default=uuid.uuid4)
    tenant_id: Mapped[uuid.UUID] = mapped_column(Uuid(as_uuid=True), ForeignKey("tenants.id", ondelete="CASCADE"), nullable=False)
    parent_id: Mapped[uuid.UUID | None] = mapped_column(Uuid(as_uuid=True))
    code: Mapped[str] = mapped_column(String(40), nullable=False)
    name_ar: Mapped[str] = mapped_column(String(180), nullable=False)
    name_en: Mapped[str | None] = mapped_column(String(180))
    is_active: Mapped[bool] = mapped_column(Boolean, nullable=False, default=True, server_default=text("true"))
    __table_args__ = (
        ForeignKeyConstraint(["tenant_id", "parent_id"], ["cost_centers.tenant_id", "cost_centers.id"], ondelete="RESTRICT", name="fk_cost_centers_parent"),
        UniqueConstraint("tenant_id", "code", name="uq_cost_centers_tenant_code"),
        UniqueConstraint("tenant_id", "id", name="uq_cost_centers_tenant_id"),
    )


class SequenceCounter(Base, TimestampMixin):
    __tablename__ = "sequence_counters"
    id: Mapped[uuid.UUID] = mapped_column(Uuid(as_uuid=True), primary_key=True, default=uuid.uuid4)
    tenant_id: Mapped[uuid.UUID] = mapped_column(Uuid(as_uuid=True), ForeignKey("tenants.id", ondelete="CASCADE"), nullable=False)
    scope_key: Mapped[str] = mapped_column(String(80), nullable=False, default="GLOBAL", server_default="GLOBAL")
    document_type: Mapped[str] = mapped_column(String(50), nullable=False)
    fiscal_year: Mapped[int] = mapped_column(Integer, nullable=False)
    prefix: Mapped[str] = mapped_column(String(20), nullable=False)
    next_value: Mapped[int] = mapped_column(BigInteger, nullable=False, default=1, server_default="1")
    width: Mapped[int] = mapped_column(SmallInteger, nullable=False, default=6, server_default="6")
    __table_args__ = (
        CheckConstraint("next_value >= 1", name="ck_sequence_next_value"),
        CheckConstraint("width BETWEEN 1 AND 18", name="ck_sequence_width"),
        UniqueConstraint("tenant_id", "scope_key", "document_type", "fiscal_year", name="uq_sequence_scope_type_year"),
    )


class JournalEntry(Base, TimestampMixin):
    __tablename__ = "journal_entries"
    id: Mapped[uuid.UUID] = mapped_column(Uuid(as_uuid=True), primary_key=True, default=uuid.uuid4)
    tenant_id: Mapped[uuid.UUID] = mapped_column(Uuid(as_uuid=True), ForeignKey("tenants.id", ondelete="CASCADE"), nullable=False)
    branch_id: Mapped[uuid.UUID | None] = mapped_column(Uuid(as_uuid=True))
    period_id: Mapped[uuid.UUID] = mapped_column(Uuid(as_uuid=True), nullable=False)
    number: Mapped[str] = mapped_column(String(60), nullable=False)
    entry_date: Mapped[date] = mapped_column(Date, nullable=False)
    memo: Mapped[str] = mapped_column(String(500), nullable=False)
    source_type: Mapped[str] = mapped_column(String(50), nullable=False, default="manual", server_default="manual")
    source_id: Mapped[str | None] = mapped_column(String(100))
    status: Mapped[str] = mapped_column(String(12), nullable=False, default="draft", server_default="draft")
    idempotency_key: Mapped[str | None] = mapped_column(String(100))
    created_by: Mapped[uuid.UUID] = mapped_column(Uuid(as_uuid=True), ForeignKey("users.id", ondelete="RESTRICT"), nullable=False)
    posted_by: Mapped[uuid.UUID | None] = mapped_column(Uuid(as_uuid=True), ForeignKey("users.id", ondelete="RESTRICT"))
    posted_at: Mapped[datetime | None] = mapped_column(DateTime(timezone=True))
    reversal_of_id: Mapped[uuid.UUID | None] = mapped_column(Uuid(as_uuid=True))
    reversed_by_id: Mapped[uuid.UUID | None] = mapped_column(Uuid(as_uuid=True))
    __table_args__ = (
        ForeignKeyConstraint(["tenant_id", "branch_id"], ["branches.tenant_id", "branches.id"], ondelete="RESTRICT", name="fk_journals_branch"),
        ForeignKeyConstraint(["tenant_id", "period_id"], ["accounting_periods.tenant_id", "accounting_periods.id"], ondelete="RESTRICT", name="fk_journals_period"),
        ForeignKeyConstraint(["tenant_id", "reversal_of_id"], ["journal_entries.tenant_id", "journal_entries.id"], ondelete="RESTRICT", name="fk_journals_reversal_of"),
        ForeignKeyConstraint(["tenant_id", "reversed_by_id"], ["journal_entries.tenant_id", "journal_entries.id"], ondelete="RESTRICT", name="fk_journals_reversed_by"),
        CheckConstraint("status IN ('draft','posted','reversed')", name="ck_journals_status"),
        UniqueConstraint("tenant_id", "number", name="uq_journals_tenant_number"),
        UniqueConstraint("tenant_id", "id", name="uq_journals_tenant_id"),
        UniqueConstraint("tenant_id", "idempotency_key", name="uq_journals_idempotency"),
        Index("ix_journals_tenant_date_status", "tenant_id", "entry_date", "status"),
    )


class JournalItem(Base):
    __tablename__ = "journal_items"
    id: Mapped[uuid.UUID] = mapped_column(Uuid(as_uuid=True), primary_key=True, default=uuid.uuid4)
    tenant_id: Mapped[uuid.UUID] = mapped_column(Uuid(as_uuid=True), ForeignKey("tenants.id", ondelete="CASCADE"), nullable=False)
    journal_entry_id: Mapped[uuid.UUID] = mapped_column(Uuid(as_uuid=True), nullable=False)
    line_no: Mapped[int] = mapped_column(SmallInteger, nullable=False)
    account_id: Mapped[uuid.UUID] = mapped_column(Uuid(as_uuid=True), nullable=False)
    cost_center_id: Mapped[uuid.UUID | None] = mapped_column(Uuid(as_uuid=True))
    description: Mapped[str | None] = mapped_column(String(300))
    debit: Mapped[Decimal] = mapped_column(Numeric(20, 2), nullable=False, default=Decimal("0"), server_default="0")
    credit: Mapped[Decimal] = mapped_column(Numeric(20, 2), nullable=False, default=Decimal("0"), server_default="0")
    currency_code: Mapped[str] = mapped_column(String(3), nullable=False, default="SAR", server_default="SAR")
    debit_currency: Mapped[Decimal] = mapped_column(Numeric(20, 2), nullable=False, default=Decimal("0"), server_default="0")
    credit_currency: Mapped[Decimal] = mapped_column(Numeric(20, 2), nullable=False, default=Decimal("0"), server_default="0")
    exchange_rate: Mapped[Decimal] = mapped_column(Numeric(20, 10), nullable=False, default=Decimal("1"), server_default="1")
    __table_args__ = (
        ForeignKeyConstraint(["tenant_id", "journal_entry_id"], ["journal_entries.tenant_id", "journal_entries.id"], ondelete="CASCADE", name="fk_journal_items_entry"),
        ForeignKeyConstraint(["tenant_id", "account_id"], ["accounts.tenant_id", "accounts.id"], ondelete="RESTRICT", name="fk_journal_items_account"),
        ForeignKeyConstraint(["tenant_id", "cost_center_id"], ["cost_centers.tenant_id", "cost_centers.id"], ondelete="RESTRICT", name="fk_journal_items_cost_center"),
        CheckConstraint("line_no >= 1", name="ck_journal_items_line_no"),
        CheckConstraint("(debit > 0 AND credit = 0) OR (credit > 0 AND debit = 0)", name="ck_journal_items_one_sided_amount"),
        CheckConstraint("debit_currency >= 0 AND credit_currency >= 0 AND exchange_rate > 0", name="ck_journal_items_currency_amounts"),
        UniqueConstraint("tenant_id", "journal_entry_id", "line_no", name="uq_journal_items_line"),
        Index("ix_journal_items_tenant_account", "tenant_id", "account_id"),
    )


class AuditLog(Base):
    __tablename__ = "audit_logs"
    id: Mapped[uuid.UUID] = mapped_column(Uuid(as_uuid=True), primary_key=True, default=uuid.uuid4)
    tenant_id: Mapped[uuid.UUID] = mapped_column(Uuid(as_uuid=True), ForeignKey("tenants.id", ondelete="CASCADE"), nullable=False)
    actor_user_id: Mapped[uuid.UUID | None] = mapped_column(Uuid(as_uuid=True), ForeignKey("users.id", ondelete="SET NULL"))
    actor_name_snapshot: Mapped[str] = mapped_column(String(254), nullable=False)
    action: Mapped[str] = mapped_column(String(50), nullable=False)
    entity_type: Mapped[str] = mapped_column(String(80), nullable=False)
    entity_id: Mapped[str] = mapped_column(String(100), nullable=False)
    before_data: Mapped[dict[str, Any] | None] = mapped_column(JSON_VALUE)
    after_data: Mapped[dict[str, Any] | None] = mapped_column(JSON_VALUE)
    ip_address: Mapped[str | None] = mapped_column(String(45))
    user_agent: Mapped[str | None] = mapped_column(String(500))
    correlation_id: Mapped[str | None] = mapped_column(String(80))
    created_at: Mapped[datetime] = mapped_column(DateTime(timezone=True), nullable=False, server_default=func.now())
    __table_args__ = (Index("ix_audit_tenant_created", "tenant_id", "created_at"), Index("ix_audit_tenant_entity", "tenant_id", "entity_type", "entity_id"))
