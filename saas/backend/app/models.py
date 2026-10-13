from __future__ import annotations

from datetime import date, datetime
from decimal import Decimal
from uuid import UUID, uuid4

from sqlalchemy import (
    Boolean, CheckConstraint, Date, DateTime, ForeignKey, ForeignKeyConstraint,
    Index, Integer, JSON, Numeric, String, Text, UniqueConstraint, Uuid, func,
)
from sqlalchemy.orm import DeclarativeBase, Mapped, mapped_column


MONEY = Numeric(20, 2)
UNIT_AMOUNT = Numeric(20, 4)
QUANTITY = Numeric(20, 4)


class Base(DeclarativeBase):
    pass


class Tenant(Base):
    __tablename__ = "tenants"

    id: Mapped[UUID] = mapped_column(Uuid(as_uuid=True), primary_key=True, default=uuid4)
    name: Mapped[str] = mapped_column(String(180), nullable=False)
    slug: Mapped[str] = mapped_column(String(100), unique=True, index=True, nullable=False)
    legal_name: Mapped[str | None] = mapped_column(String(240))
    tax_number: Mapped[str | None] = mapped_column(String(32))
    country_code: Mapped[str] = mapped_column(String(2), default="SA", nullable=False)
    currency_code: Mapped[str] = mapped_column(String(3), default="SAR", nullable=False)
    fiscal_year_start_month: Mapped[int] = mapped_column(Integer, default=1, nullable=False)
    timezone: Mapped[str] = mapped_column(String(64), default="Asia/Riyadh", nullable=False)
    is_active: Mapped[bool] = mapped_column(Boolean, default=True, nullable=False)
    created_at: Mapped[datetime] = mapped_column(DateTime(timezone=True), server_default=func.now(), nullable=False)


class User(Base):
    __tablename__ = "users"

    id: Mapped[UUID] = mapped_column(Uuid(as_uuid=True), primary_key=True, default=uuid4)
    email: Mapped[str] = mapped_column(String(320), unique=True, index=True, nullable=False)
    full_name: Mapped[str] = mapped_column(String(180), nullable=False)
    password_hash: Mapped[str] = mapped_column(String(500), nullable=False)
    is_active: Mapped[bool] = mapped_column(Boolean, default=True, nullable=False)
    created_at: Mapped[datetime] = mapped_column(DateTime(timezone=True), server_default=func.now(), nullable=False)
    last_login_at: Mapped[datetime | None] = mapped_column(DateTime(timezone=True))


class PermissionDefinition(Base):
    __tablename__ = "permission_definitions"

    code: Mapped[str] = mapped_column(String(100), primary_key=True)
    module: Mapped[str] = mapped_column(String(60), nullable=False)
    action: Mapped[str] = mapped_column(String(60), nullable=False)
    description: Mapped[str] = mapped_column(String(240), nullable=False)


class Membership(Base):
    __tablename__ = "memberships"
    __table_args__ = (
        UniqueConstraint("tenant_id", "user_id", name="uq_membership_tenant_user"),
        CheckConstraint("role in ('Owner','Admin','CFO','Accountant','Cashier','Sales','Purchasing','Storekeeper','HR','Auditor','FieldAgent','ReadOnly')", name="ck_membership_role"),
        Index("ix_memberships_user_tenant", "user_id", "tenant_id"),
    )

    id: Mapped[UUID] = mapped_column(Uuid(as_uuid=True), primary_key=True, default=uuid4)
    tenant_id: Mapped[UUID] = mapped_column(Uuid(as_uuid=True), ForeignKey("tenants.id", ondelete="CASCADE"), nullable=False)
    user_id: Mapped[UUID] = mapped_column(Uuid(as_uuid=True), ForeignKey("users.id", ondelete="CASCADE"), nullable=False)
    role: Mapped[str] = mapped_column(String(24), nullable=False, default="Admin")
    branch_scope_all: Mapped[bool] = mapped_column(Boolean, default=False, nullable=False)
    is_active: Mapped[bool] = mapped_column(Boolean, default=True, nullable=False)
    created_at: Mapped[datetime] = mapped_column(DateTime(timezone=True), server_default=func.now(), nullable=False)


class TenantOwned:
    __abstract__ = True

    id: Mapped[UUID] = mapped_column(Uuid(as_uuid=True), primary_key=True, default=uuid4)
    tenant_id: Mapped[UUID] = mapped_column(Uuid(as_uuid=True), ForeignKey("tenants.id", ondelete="CASCADE"), nullable=False, index=True)
    created_at: Mapped[datetime] = mapped_column(DateTime(timezone=True), server_default=func.now(), nullable=False)
    updated_at: Mapped[datetime] = mapped_column(DateTime(timezone=True), server_default=func.now(), onupdate=func.now(), nullable=False)


class RolePermission(Base, TenantOwned):
    __tablename__ = "role_permissions"
    __table_args__ = (
        UniqueConstraint("tenant_id", "role_code", "permission_code", name="uq_role_permission"),
        CheckConstraint(
            "role_code in ('Owner','Admin','CFO','Accountant','Cashier','Sales','Purchasing','Storekeeper','HR','Auditor','FieldAgent','ReadOnly')",
            name="ck_role_permission_role",
        ),
        ForeignKeyConstraint(["permission_code"], ["permission_definitions.code"], ondelete="RESTRICT", name="fk_role_permission_permission"),
        Index("ix_role_permissions_tenant_role", "tenant_id", "role_code"),
    )

    role_code: Mapped[str] = mapped_column(String(24), nullable=False)
    permission_code: Mapped[str] = mapped_column(String(100), nullable=False)
    allowed: Mapped[bool] = mapped_column(Boolean, nullable=False)


class Branch(Base, TenantOwned):
    __tablename__ = "branches"
    __table_args__ = (
        UniqueConstraint("tenant_id", "id", name="uq_branches_tenant_id"),
        UniqueConstraint("tenant_id", "code", name="uq_branches_tenant_code"),
    )

    code: Mapped[str] = mapped_column(String(32), nullable=False)
    name: Mapped[str] = mapped_column(String(180), nullable=False)
    is_active: Mapped[bool] = mapped_column(Boolean, default=True, nullable=False)


class UserBranchAccess(Base, TenantOwned):
    __tablename__ = "user_branch_access"
    __table_args__ = (
        UniqueConstraint("tenant_id", "user_id", "branch_id", name="uq_user_branch_access"),
        ForeignKeyConstraint(
            ["tenant_id", "user_id"],
            ["memberships.tenant_id", "memberships.user_id"],
            ondelete="CASCADE",
            name="fk_user_branch_membership_same_tenant",
        ),
        ForeignKeyConstraint(
            ["tenant_id", "branch_id"],
            ["branches.tenant_id", "branches.id"],
            ondelete="CASCADE",
            name="fk_user_branch_branch_same_tenant",
        ),
        CheckConstraint("access_level in ('VIEW','OPERATE','ADMIN')", name="ck_user_branch_access_level"),
        Index("ix_user_branch_access_tenant_user", "tenant_id", "user_id"),
    )

    user_id: Mapped[UUID] = mapped_column(Uuid(as_uuid=True), nullable=False)
    branch_id: Mapped[UUID] = mapped_column(Uuid(as_uuid=True), nullable=False)
    access_level: Mapped[str] = mapped_column(String(12), default="VIEW", nullable=False)


class Account(Base, TenantOwned):
    __tablename__ = "accounts"
    __table_args__ = (
        UniqueConstraint("tenant_id", "id", name="uq_accounts_tenant_id"),
        UniqueConstraint("tenant_id", "code", name="uq_accounts_tenant_code"),
        ForeignKeyConstraint(["tenant_id", "parent_id"], ["accounts.tenant_id", "accounts.id"], ondelete="RESTRICT", name="fk_account_parent_same_tenant"),
        CheckConstraint("statement_type in ('BALANCE_SHEET','INCOME_STATEMENT')", name="ck_account_statement_type"),
        CheckConstraint("nature in ('DEBIT','CREDIT')", name="ck_account_nature"),
        CheckConstraint("length(code) >= 1", name="ck_account_code_nonempty"),
        Index("ix_accounts_tenant_parent", "tenant_id", "parent_id"),
    )

    code: Mapped[str] = mapped_column(String(32), nullable=False)
    name: Mapped[str] = mapped_column(String(180), nullable=False)
    parent_id: Mapped[UUID | None] = mapped_column(Uuid(as_uuid=True))
    statement_type: Mapped[str] = mapped_column(String(24), nullable=False)
    nature: Mapped[str] = mapped_column(String(8), nullable=False)
    account_category: Mapped[str] = mapped_column(String(24), nullable=False)
    allow_posting: Mapped[bool] = mapped_column(Boolean, default=True, nullable=False)
    is_system: Mapped[bool] = mapped_column(Boolean, default=False, nullable=False)
    is_active: Mapped[bool] = mapped_column(Boolean, default=True, nullable=False)


class CostCenter(Base, TenantOwned):
    __tablename__ = "cost_centers"
    __table_args__ = (
        UniqueConstraint("tenant_id", "id", name="uq_cost_centers_tenant_id"),
        UniqueConstraint("tenant_id", "code", name="uq_cost_centers_tenant_code"),
        ForeignKeyConstraint(["tenant_id", "parent_id"], ["cost_centers.tenant_id", "cost_centers.id"], ondelete="RESTRICT", name="fk_cost_center_parent_same_tenant"),
    )

    code: Mapped[str] = mapped_column(String(32), nullable=False)
    name: Mapped[str] = mapped_column(String(180), nullable=False)
    parent_id: Mapped[UUID | None] = mapped_column(Uuid(as_uuid=True))
    is_active: Mapped[bool] = mapped_column(Boolean, default=True, nullable=False)


class Contact(Base, TenantOwned):
    __tablename__ = "contacts"
    __table_args__ = (
        UniqueConstraint("tenant_id", "id", name="uq_contacts_tenant_id"),
        CheckConstraint("contact_type in ('CUSTOMER','SUPPLIER','BOTH')", name="ck_contact_type"),
    )

    name: Mapped[str] = mapped_column(String(220), nullable=False)
    contact_type: Mapped[str] = mapped_column(String(16), nullable=False)
    email: Mapped[str | None] = mapped_column(String(320))
    phone: Mapped[str | None] = mapped_column(String(40))
    tax_number: Mapped[str | None] = mapped_column(String(32))
    address: Mapped[str | None] = mapped_column(Text)
    is_active: Mapped[bool] = mapped_column(Boolean, default=True, nullable=False)
    receivable_account_id: Mapped[UUID | None] = mapped_column(Uuid(as_uuid=True))
    payable_account_id: Mapped[UUID | None] = mapped_column(Uuid(as_uuid=True))
    __table_args__ = __table_args__ + (
        ForeignKeyConstraint(["tenant_id", "receivable_account_id"], ["accounts.tenant_id", "accounts.id"], name="fk_contact_receivable_same_tenant"),
        ForeignKeyConstraint(["tenant_id", "payable_account_id"], ["accounts.tenant_id", "accounts.id"], name="fk_contact_payable_same_tenant"),
        Index("ix_contacts_tenant_tax", "tenant_id", "tax_number"),
    )


class Warehouse(Base, TenantOwned):
    __tablename__ = "warehouses"
    __table_args__ = (
        UniqueConstraint("tenant_id", "id", name="uq_warehouses_tenant_id"),
        UniqueConstraint("tenant_id", "code", name="uq_warehouses_tenant_code"),
    )

    code: Mapped[str] = mapped_column(String(32), nullable=False)
    name: Mapped[str] = mapped_column(String(160), nullable=False)
    address: Mapped[str | None] = mapped_column(Text)
    is_default: Mapped[bool] = mapped_column(Boolean, default=False, nullable=False)
    is_active: Mapped[bool] = mapped_column(Boolean, default=True, nullable=False)


class Product(Base, TenantOwned):
    __tablename__ = "products"
    __table_args__ = (
        UniqueConstraint("tenant_id", "id", name="uq_products_tenant_id"),
        UniqueConstraint("tenant_id", "sku", name="uq_products_tenant_sku"),
        CheckConstraint("item_type in ('STOCK','SERVICE','MANUFACTURED')", name="ck_product_item_type"),
        CheckConstraint("vat_rate >= 0 AND vat_rate <= 100", name="ck_product_vat_rate"),
        CheckConstraint("unit_price >= 0 AND standard_cost >= 0", name="ck_product_amounts_nonnegative"),
        ForeignKeyConstraint(["tenant_id", "inventory_account_id"], ["accounts.tenant_id", "accounts.id"], name="fk_product_inventory_account"),
        ForeignKeyConstraint(["tenant_id", "revenue_account_id"], ["accounts.tenant_id", "accounts.id"], name="fk_product_revenue_account"),
        ForeignKeyConstraint(["tenant_id", "expense_account_id"], ["accounts.tenant_id", "accounts.id"], name="fk_product_expense_account"),
        Index("ix_products_tenant_barcode", "tenant_id", "barcode"),
    )

    sku: Mapped[str] = mapped_column(String(80), nullable=False)
    name: Mapped[str] = mapped_column(String(220), nullable=False)
    description: Mapped[str | None] = mapped_column(Text)
    item_type: Mapped[str] = mapped_column(String(20), default="STOCK", nullable=False)
    barcode: Mapped[str | None] = mapped_column(String(128))
    unit_of_measure: Mapped[str] = mapped_column(String(32), default="حبة", nullable=False)
    standard_cost: Mapped[Decimal] = mapped_column(UNIT_AMOUNT, default=Decimal("0"), nullable=False)
    unit_price: Mapped[Decimal] = mapped_column(UNIT_AMOUNT, default=Decimal("0"), nullable=False)
    vat_rate: Mapped[Decimal] = mapped_column(Numeric(6, 3), default=Decimal("15"), nullable=False)
    inventory_account_id: Mapped[UUID | None] = mapped_column(Uuid(as_uuid=True))
    revenue_account_id: Mapped[UUID | None] = mapped_column(Uuid(as_uuid=True))
    expense_account_id: Mapped[UUID | None] = mapped_column(Uuid(as_uuid=True))
    is_active: Mapped[bool] = mapped_column(Boolean, default=True, nullable=False)


class ProductVariant(Base, TenantOwned):
    __tablename__ = "product_variants"
    __table_args__ = (
        UniqueConstraint("tenant_id", "id", name="uq_product_variants_tenant_id"),
        UniqueConstraint("tenant_id", "sku", name="uq_product_variants_tenant_sku"),
        ForeignKeyConstraint(["tenant_id", "product_id"], ["products.tenant_id", "products.id"], ondelete="CASCADE", name="fk_variant_product_same_tenant"),
        Index("ix_product_variants_tenant_barcode", "tenant_id", "barcode"),
    )

    product_id: Mapped[UUID] = mapped_column(Uuid(as_uuid=True), nullable=False)
    sku: Mapped[str] = mapped_column(String(80), nullable=False)
    name: Mapped[str] = mapped_column(String(180), nullable=False)
    barcode: Mapped[str | None] = mapped_column(String(128))
    attributes: Mapped[dict] = mapped_column(JSON, default=dict, nullable=False)
    unit_price_override: Mapped[Decimal | None] = mapped_column(UNIT_AMOUNT)


class StockBalance(Base, TenantOwned):
    __tablename__ = "stock_balances"
    __table_args__ = (
        UniqueConstraint("tenant_id", "id", name="uq_stock_balances_tenant_id"),
        UniqueConstraint("tenant_id", "warehouse_id", "product_id", name="uq_stock_balance_warehouse_product"),
        ForeignKeyConstraint(["tenant_id", "warehouse_id"], ["warehouses.tenant_id", "warehouses.id"], ondelete="RESTRICT", name="fk_stock_balance_warehouse_same_tenant"),
        ForeignKeyConstraint(["tenant_id", "product_id"], ["products.tenant_id", "products.id"], ondelete="RESTRICT", name="fk_stock_balance_product_same_tenant"),
        CheckConstraint("quantity >= 0", name="ck_stock_balance_nonnegative"),
        CheckConstraint("average_cost >= 0", name="ck_stock_balance_cost_nonnegative"),
    )

    warehouse_id: Mapped[UUID] = mapped_column(Uuid(as_uuid=True), nullable=False)
    product_id: Mapped[UUID] = mapped_column(Uuid(as_uuid=True), nullable=False)
    quantity: Mapped[Decimal] = mapped_column(QUANTITY, default=Decimal("0"), nullable=False)
    average_cost: Mapped[Decimal] = mapped_column(UNIT_AMOUNT, default=Decimal("0"), nullable=False)


class StockMove(Base, TenantOwned):
    __tablename__ = "stock_moves"
    __table_args__ = (
        UniqueConstraint("tenant_id", "id", name="uq_stock_moves_tenant_id"),
        ForeignKeyConstraint(["tenant_id", "product_id"], ["products.tenant_id", "products.id"], ondelete="RESTRICT", name="fk_stock_move_product_same_tenant"),
        ForeignKeyConstraint(["tenant_id", "source_warehouse_id"], ["warehouses.tenant_id", "warehouses.id"], ondelete="RESTRICT", name="fk_stock_move_source_warehouse_same_tenant"),
        ForeignKeyConstraint(["tenant_id", "destination_warehouse_id"], ["warehouses.tenant_id", "warehouses.id"], ondelete="RESTRICT", name="fk_stock_move_destination_warehouse_same_tenant"),
        CheckConstraint("quantity > 0", name="ck_stock_move_quantity_positive"),
        CheckConstraint("unit_cost >= 0", name="ck_stock_move_cost_nonnegative"),
        CheckConstraint("move_type in ('RECEIPT','ISSUE','TRANSFER','ADJUSTMENT')", name="ck_stock_move_type"),
        Index("ix_stock_moves_tenant_date", "tenant_id", "moved_at"),
    )

    product_id: Mapped[UUID] = mapped_column(Uuid(as_uuid=True), nullable=False)
    source_warehouse_id: Mapped[UUID | None] = mapped_column(Uuid(as_uuid=True))
    destination_warehouse_id: Mapped[UUID | None] = mapped_column(Uuid(as_uuid=True))
    move_type: Mapped[str] = mapped_column(String(20), nullable=False)
    quantity: Mapped[Decimal] = mapped_column(QUANTITY, nullable=False)
    unit_cost: Mapped[Decimal] = mapped_column(UNIT_AMOUNT, nullable=False)
    source_document_type: Mapped[str | None] = mapped_column(String(40))
    source_document_id: Mapped[UUID | None] = mapped_column(Uuid(as_uuid=True))
    reference: Mapped[str | None] = mapped_column(String(120))
    moved_at: Mapped[datetime] = mapped_column(DateTime(timezone=True), server_default=func.now(), nullable=False)
    memo: Mapped[str | None] = mapped_column(Text)


class NumberSequence(Base, TenantOwned):
    __tablename__ = "number_sequences"
    __table_args__ = (
        UniqueConstraint("tenant_id", "sequence_key", name="uq_number_sequence_key"),
        CheckConstraint("next_value >= 1", name="ck_number_sequence_positive"),
    )

    sequence_key: Mapped[str] = mapped_column(String(32), nullable=False)
    next_value: Mapped[int] = mapped_column(Integer, default=1, nullable=False)


class FiscalPeriod(Base, TenantOwned):
    __tablename__ = "fiscal_periods"
    __table_args__ = (
        UniqueConstraint("tenant_id", "id", name="uq_fiscal_periods_tenant_id"),
        UniqueConstraint("tenant_id", "code", name="uq_fiscal_periods_tenant_code"),
        CheckConstraint("end_date >= start_date", name="ck_fiscal_period_date_range"),
        CheckConstraint("status in ('OPEN','CLOSED')", name="ck_fiscal_period_status"),
        Index("ix_fiscal_periods_tenant_dates", "tenant_id", "start_date", "end_date"),
    )

    code: Mapped[str] = mapped_column(String(40), nullable=False)
    name: Mapped[str] = mapped_column(String(180), nullable=False)
    start_date: Mapped[date] = mapped_column(Date, nullable=False)
    end_date: Mapped[date] = mapped_column(Date, nullable=False)
    status: Mapped[str] = mapped_column(String(8), default="OPEN", nullable=False)
    closed_by: Mapped[UUID | None] = mapped_column(Uuid(as_uuid=True), ForeignKey("users.id", ondelete="SET NULL"))
    closed_at: Mapped[datetime | None] = mapped_column(DateTime(timezone=True))
    reopened_by: Mapped[UUID | None] = mapped_column(Uuid(as_uuid=True), ForeignKey("users.id", ondelete="SET NULL"))
    reopened_at: Mapped[datetime | None] = mapped_column(DateTime(timezone=True))
    close_reason: Mapped[str | None] = mapped_column(String(500))
    reopen_reason: Mapped[str | None] = mapped_column(String(500))


class JournalEntry(Base, TenantOwned):
    __tablename__ = "journal_entries"
    __table_args__ = (
        UniqueConstraint("tenant_id", "id", name="uq_journal_entries_tenant_id"),
        UniqueConstraint("tenant_id", "number", name="uq_journal_entries_tenant_number"),
        UniqueConstraint("tenant_id", "reversal_of_id", name="uq_journal_single_reversal"),
        ForeignKeyConstraint(["tenant_id", "reversal_of_id"], ["journal_entries.tenant_id", "journal_entries.id"], name="fk_journal_reversal_same_tenant"),
        CheckConstraint("status in ('DRAFT','POSTED','REVERSED')", name="ck_journal_status"),
        Index("ix_journal_entries_tenant_date", "tenant_id", "entry_date"),
    )

    number: Mapped[str] = mapped_column(String(80), nullable=False)
    entry_date: Mapped[date] = mapped_column(Date, nullable=False)
    memo: Mapped[str] = mapped_column(String(500), nullable=False)
    status: Mapped[str] = mapped_column(String(16), default="DRAFT", nullable=False)
    source_type: Mapped[str] = mapped_column(String(40), default="MANUAL", nullable=False)
    source_id: Mapped[UUID | None] = mapped_column(Uuid(as_uuid=True))
    reversal_of_id: Mapped[UUID | None] = mapped_column(Uuid(as_uuid=True))
    posted_by: Mapped[UUID | None] = mapped_column(Uuid(as_uuid=True), ForeignKey("users.id", ondelete="SET NULL"))
    posted_at: Mapped[datetime | None] = mapped_column(DateTime(timezone=True))


class JournalLine(Base, TenantOwned):
    __tablename__ = "journal_lines"
    __table_args__ = (
        UniqueConstraint("tenant_id", "id", name="uq_journal_lines_tenant_id"),
        ForeignKeyConstraint(["tenant_id", "entry_id"], ["journal_entries.tenant_id", "journal_entries.id"], ondelete="CASCADE", name="fk_journal_line_entry_same_tenant"),
        ForeignKeyConstraint(["tenant_id", "account_id"], ["accounts.tenant_id", "accounts.id"], ondelete="RESTRICT", name="fk_journal_line_account_same_tenant"),
        ForeignKeyConstraint(["tenant_id", "contact_id"], ["contacts.tenant_id", "contacts.id"], ondelete="RESTRICT", name="fk_journal_line_contact_same_tenant"),
        ForeignKeyConstraint(["tenant_id", "cost_center_id"], ["cost_centers.tenant_id", "cost_centers.id"], ondelete="RESTRICT", name="fk_journal_line_cost_center_same_tenant"),
        CheckConstraint("debit >= 0 AND credit >= 0", name="ck_journal_line_nonnegative"),
        CheckConstraint("NOT (debit > 0 AND credit > 0)", name="ck_journal_line_single_sided"),
        Index("ix_journal_lines_tenant_entry", "tenant_id", "entry_id"),
        Index("ix_journal_lines_tenant_account", "tenant_id", "account_id"),
    )

    entry_id: Mapped[UUID] = mapped_column(Uuid(as_uuid=True), nullable=False)
    account_id: Mapped[UUID] = mapped_column(Uuid(as_uuid=True), nullable=False)
    contact_id: Mapped[UUID | None] = mapped_column(Uuid(as_uuid=True))
    cost_center_id: Mapped[UUID | None] = mapped_column(Uuid(as_uuid=True))
    description: Mapped[str | None] = mapped_column(String(500))
    debit: Mapped[Decimal] = mapped_column(MONEY, default=Decimal("0"), nullable=False)
    credit: Mapped[Decimal] = mapped_column(MONEY, default=Decimal("0"), nullable=False)
    currency_code: Mapped[str] = mapped_column(String(3), default="SAR", nullable=False)


class Invoice(Base, TenantOwned):
    __tablename__ = "invoices"
    __table_args__ = (
        UniqueConstraint("tenant_id", "id", name="uq_invoices_tenant_id"),
        UniqueConstraint("tenant_id", "kind", "number", name="uq_invoice_tenant_kind_number"),
        UniqueConstraint("tenant_id", "client_reference", name="uq_invoice_tenant_client_reference"),
        ForeignKeyConstraint(["tenant_id", "contact_id"], ["contacts.tenant_id", "contacts.id"], ondelete="RESTRICT", name="fk_invoice_contact_same_tenant"),
        ForeignKeyConstraint(["tenant_id", "warehouse_id"], ["warehouses.tenant_id", "warehouses.id"], ondelete="RESTRICT", name="fk_invoice_warehouse_same_tenant"),
        ForeignKeyConstraint(["tenant_id", "journal_entry_id"], ["journal_entries.tenant_id", "journal_entries.id"], name="fk_invoice_journal_same_tenant"),
        CheckConstraint("kind in ('SALE','PURCHASE')", name="ck_invoice_kind"),
        CheckConstraint("status in ('DRAFT','POSTED','CANCELLED')", name="ck_invoice_status"),
        CheckConstraint("subtotal >= 0 AND vat_total >= 0 AND total >= 0", name="ck_invoice_totals_nonnegative"),
        Index("ix_invoices_tenant_date", "tenant_id", "issue_date"),
    )

    kind: Mapped[str] = mapped_column(String(12), nullable=False)
    number: Mapped[str] = mapped_column(String(80), nullable=False)
    issue_date: Mapped[date] = mapped_column(Date, nullable=False)
    contact_id: Mapped[UUID] = mapped_column(Uuid(as_uuid=True), nullable=False)
    warehouse_id: Mapped[UUID | None] = mapped_column(Uuid(as_uuid=True))
    is_credit: Mapped[bool] = mapped_column(Boolean, default=True, nullable=False)
    status: Mapped[str] = mapped_column(String(16), default="DRAFT", nullable=False)
    currency_code: Mapped[str] = mapped_column(String(3), default="SAR", nullable=False)
    exchange_rate: Mapped[Decimal] = mapped_column(Numeric(20, 8), default=Decimal("1"), nullable=False)
    subtotal: Mapped[Decimal] = mapped_column(MONEY, default=Decimal("0"), nullable=False)
    vat_total: Mapped[Decimal] = mapped_column(MONEY, default=Decimal("0"), nullable=False)
    total: Mapped[Decimal] = mapped_column(MONEY, default=Decimal("0"), nullable=False)
    journal_entry_id: Mapped[UUID | None] = mapped_column(Uuid(as_uuid=True))
    client_reference: Mapped[str | None] = mapped_column(String(120))
    qr_payload: Mapped[str | None] = mapped_column(Text)
    source_document_name: Mapped[str | None] = mapped_column(String(255))
    ocr_metadata: Mapped[dict | None] = mapped_column(JSON)
    requires_review: Mapped[bool] = mapped_column(Boolean, default=False, nullable=False)
    posted_at: Mapped[datetime | None] = mapped_column(DateTime(timezone=True))


class InvoiceLine(Base, TenantOwned):
    __tablename__ = "invoice_lines"
    __table_args__ = (
        ForeignKeyConstraint(["tenant_id", "invoice_id"], ["invoices.tenant_id", "invoices.id"], ondelete="CASCADE", name="fk_invoice_line_invoice_same_tenant"),
        ForeignKeyConstraint(["tenant_id", "product_id"], ["products.tenant_id", "products.id"], ondelete="RESTRICT", name="fk_invoice_line_product_same_tenant"),
        ForeignKeyConstraint(["tenant_id", "account_id"], ["accounts.tenant_id", "accounts.id"], ondelete="RESTRICT", name="fk_invoice_line_account_same_tenant"),
        ForeignKeyConstraint(["tenant_id", "cost_center_id"], ["cost_centers.tenant_id", "cost_centers.id"], ondelete="RESTRICT", name="fk_invoice_line_cost_center_same_tenant"),
        CheckConstraint("quantity > 0", name="ck_invoice_line_quantity_positive"),
        CheckConstraint("unit_price >= 0 AND discount_rate >= 0 AND discount_rate <= 100", name="ck_invoice_line_price_discount"),
        CheckConstraint("vat_rate >= 0 AND vat_rate <= 100", name="ck_invoice_line_vat_rate"),
        CheckConstraint("net_amount >= 0 AND vat_amount >= 0 AND total_amount >= 0", name="ck_invoice_line_amounts"),
    )

    invoice_id: Mapped[UUID] = mapped_column(Uuid(as_uuid=True), nullable=False)
    product_id: Mapped[UUID | None] = mapped_column(Uuid(as_uuid=True))
    account_id: Mapped[UUID | None] = mapped_column(Uuid(as_uuid=True))
    cost_center_id: Mapped[UUID | None] = mapped_column(Uuid(as_uuid=True))
    description: Mapped[str] = mapped_column(String(400), nullable=False)
    quantity: Mapped[Decimal] = mapped_column(QUANTITY, default=Decimal("1"), nullable=False)
    unit_price: Mapped[Decimal] = mapped_column(UNIT_AMOUNT, default=Decimal("0"), nullable=False)
    discount_rate: Mapped[Decimal] = mapped_column(Numeric(6, 3), default=Decimal("0"), nullable=False)
    vat_rate: Mapped[Decimal] = mapped_column(Numeric(6, 3), default=Decimal("15"), nullable=False)
    net_amount: Mapped[Decimal] = mapped_column(MONEY, default=Decimal("0"), nullable=False)
    vat_amount: Mapped[Decimal] = mapped_column(MONEY, default=Decimal("0"), nullable=False)
    total_amount: Mapped[Decimal] = mapped_column(MONEY, default=Decimal("0"), nullable=False)
    unit_cost_at_posting: Mapped[Decimal] = mapped_column(UNIT_AMOUNT, default=Decimal("0"), nullable=False)


class AuditLog(Base, TenantOwned):
    __tablename__ = "audit_logs"
    __table_args__ = (
        Index("ix_audit_logs_tenant_time", "tenant_id", "created_at"),
    )

    actor_user_id: Mapped[UUID | None] = mapped_column(Uuid(as_uuid=True), ForeignKey("users.id", ondelete="SET NULL"))
    action: Mapped[str] = mapped_column(String(80), nullable=False)
    entity_type: Mapped[str] = mapped_column(String(80), nullable=False)
    entity_id: Mapped[UUID | None] = mapped_column(Uuid(as_uuid=True))
    details: Mapped[dict] = mapped_column(JSON, default=dict, nullable=False)
    before_data: Mapped[dict | None] = mapped_column(JSON, nullable=True)
    after_data: Mapped[dict | None] = mapped_column(JSON, nullable=True)
    remote_address: Mapped[str | None] = mapped_column(String(64))
    user_agent: Mapped[str | None] = mapped_column(String(500))
    correlation_id: Mapped[str | None] = mapped_column(String(80))


class BillOfMaterials(Base, TenantOwned):
    __tablename__ = "bills_of_materials"
    __table_args__ = (
        UniqueConstraint("tenant_id", "id", name="uq_bom_tenant_id"),
        ForeignKeyConstraint(["tenant_id", "product_id"], ["products.tenant_id", "products.id"], ondelete="RESTRICT", name="fk_bom_product_same_tenant"),
        CheckConstraint("output_quantity > 0", name="ck_bom_output_quantity"),
    )

    product_id: Mapped[UUID] = mapped_column(Uuid(as_uuid=True), nullable=False)
    code: Mapped[str] = mapped_column(String(80), nullable=False)
    name: Mapped[str] = mapped_column(String(180), nullable=False)
    output_quantity: Mapped[Decimal] = mapped_column(QUANTITY, default=Decimal("1"), nullable=False)
    is_active: Mapped[bool] = mapped_column(Boolean, default=True, nullable=False)


class BillOfMaterialsLine(Base, TenantOwned):
    __tablename__ = "bill_of_material_lines"
    __table_args__ = (
        ForeignKeyConstraint(["tenant_id", "bom_id"], ["bills_of_materials.tenant_id", "bills_of_materials.id"], ondelete="CASCADE", name="fk_bom_line_bom_same_tenant"),
        ForeignKeyConstraint(["tenant_id", "component_product_id"], ["products.tenant_id", "products.id"], ondelete="RESTRICT", name="fk_bom_line_component_same_tenant"),
        CheckConstraint("quantity > 0", name="ck_bom_line_quantity_positive"),
    )

    bom_id: Mapped[UUID] = mapped_column(Uuid(as_uuid=True), nullable=False)
    component_product_id: Mapped[UUID] = mapped_column(Uuid(as_uuid=True), nullable=False)
    quantity: Mapped[Decimal] = mapped_column(QUANTITY, nullable=False)
    scrap_rate: Mapped[Decimal] = mapped_column(Numeric(6, 3), default=Decimal("0"), nullable=False)
