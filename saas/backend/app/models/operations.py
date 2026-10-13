from __future__ import annotations

import uuid
from datetime import date
from decimal import Decimal
from typing import Any

from sqlalchemy import (
    BigInteger, Boolean, CheckConstraint, Date, ForeignKey, ForeignKeyConstraint, Index,
    JSON, Numeric, SmallInteger, String, Text, UniqueConstraint, Uuid, text,
)
from sqlalchemy.dialects.postgresql import JSONB
from sqlalchemy.orm import Mapped, mapped_column

from app.models.base import Base
from app.models.identity import JSON_VALUE, TimestampMixin


class Contact(Base, TimestampMixin):
    __tablename__ = "contacts"
    id: Mapped[uuid.UUID] = mapped_column(Uuid(as_uuid=True), primary_key=True, default=uuid.uuid4)
    tenant_id: Mapped[uuid.UUID] = mapped_column(Uuid(as_uuid=True), ForeignKey("tenants.id", ondelete="CASCADE"), nullable=False)
    code: Mapped[str] = mapped_column(String(40), nullable=False)
    name: Mapped[str] = mapped_column(String(220), nullable=False)
    contact_type: Mapped[str] = mapped_column(String(12), nullable=False)
    tax_number: Mapped[str | None] = mapped_column(String(20))
    email: Mapped[str | None] = mapped_column(String(254))
    phone: Mapped[str | None] = mapped_column(String(30))
    credit_limit: Mapped[Decimal] = mapped_column(Numeric(20, 2), nullable=False, default=Decimal("0"), server_default="0")
    is_active: Mapped[bool] = mapped_column(Boolean, nullable=False, default=True, server_default=text("true"))
    __table_args__ = (
        CheckConstraint("contact_type IN ('customer','supplier','both')", name="ck_contacts_type"),
        CheckConstraint("credit_limit >= 0", name="ck_contacts_credit_limit"),
        UniqueConstraint("tenant_id", "code", name="uq_contacts_tenant_code"),
        UniqueConstraint("tenant_id", "id", name="uq_contacts_tenant_id"),
        Index("ix_contacts_tenant_tax_number", "tenant_id", "tax_number"),
    )


class Product(Base, TimestampMixin):
    __tablename__ = "products"
    id: Mapped[uuid.UUID] = mapped_column(Uuid(as_uuid=True), primary_key=True, default=uuid.uuid4)
    tenant_id: Mapped[uuid.UUID] = mapped_column(Uuid(as_uuid=True), ForeignKey("tenants.id", ondelete="CASCADE"), nullable=False)
    sku: Mapped[str] = mapped_column(String(60), nullable=False)
    name_ar: Mapped[str] = mapped_column(String(220), nullable=False)
    name_en: Mapped[str | None] = mapped_column(String(220))
    product_type: Mapped[str] = mapped_column(String(15), nullable=False, default="stockable", server_default="stockable")
    costing_method: Mapped[str] = mapped_column(String(20), nullable=False, default="weighted_average", server_default="weighted_average")
    barcode: Mapped[str | None] = mapped_column(String(100))
    unit_of_measure: Mapped[str] = mapped_column(String(30), nullable=False, default="piece", server_default="piece")
    standard_cost: Mapped[Decimal] = mapped_column(Numeric(20, 4), nullable=False, default=Decimal("0"), server_default="0")
    average_cost: Mapped[Decimal] = mapped_column(Numeric(20, 4), nullable=False, default=Decimal("0"), server_default="0")
    sale_price: Mapped[Decimal] = mapped_column(Numeric(20, 4), nullable=False, default=Decimal("0"), server_default="0")
    tax_code: Mapped[str] = mapped_column(String(20), nullable=False, default="standard_15", server_default="standard_15")
    tax_rate: Mapped[Decimal] = mapped_column(Numeric(7, 4), nullable=False, default=Decimal("15"), server_default="15")
    reorder_level: Mapped[Decimal] = mapped_column(Numeric(20, 4), nullable=False, default=Decimal("0"), server_default="0")
    is_active: Mapped[bool] = mapped_column(Boolean, nullable=False, default=True, server_default=text("true"))
    __table_args__ = (
        CheckConstraint("product_type IN ('stockable','service','manufactured')", name="ck_products_type"),
        CheckConstraint("costing_method IN ('weighted_average','fifo','specific')", name="ck_products_costing"),
        CheckConstraint("standard_cost >= 0 AND average_cost >= 0 AND sale_price >= 0 AND reorder_level >= 0", name="ck_products_prices"),
        CheckConstraint("tax_rate >= 0 AND tax_rate <= 100", name="ck_products_tax_rate"),
        UniqueConstraint("tenant_id", "sku", name="uq_products_tenant_sku"),
        UniqueConstraint("tenant_id", "id", name="uq_products_tenant_id"),
        Index("ix_products_tenant_barcode", "tenant_id", "barcode"),
    )


class ProductVariant(Base, TimestampMixin):
    __tablename__ = "product_variants"
    id: Mapped[uuid.UUID] = mapped_column(Uuid(as_uuid=True), primary_key=True, default=uuid.uuid4)
    tenant_id: Mapped[uuid.UUID] = mapped_column(Uuid(as_uuid=True), ForeignKey("tenants.id", ondelete="CASCADE"), nullable=False)
    product_id: Mapped[uuid.UUID] = mapped_column(Uuid(as_uuid=True), nullable=False)
    sku: Mapped[str] = mapped_column(String(60), nullable=False)
    barcode: Mapped[str | None] = mapped_column(String(100))
    name_ar: Mapped[str] = mapped_column(String(220), nullable=False)
    attributes_json: Mapped[dict[str, Any]] = mapped_column(JSON_VALUE, nullable=False, default=dict, server_default=text("'{}'"))
    sale_price_override: Mapped[Decimal | None] = mapped_column(Numeric(20, 4))
    __table_args__ = (
        ForeignKeyConstraint(["tenant_id", "product_id"], ["products.tenant_id", "products.id"], ondelete="CASCADE", name="fk_product_variants_product"),
        UniqueConstraint("tenant_id", "sku", name="uq_product_variants_sku"),
        UniqueConstraint("tenant_id", "id", name="uq_product_variants_tenant_id"),
    )


class Warehouse(Base, TimestampMixin):
    __tablename__ = "warehouses"
    id: Mapped[uuid.UUID] = mapped_column(Uuid(as_uuid=True), primary_key=True, default=uuid.uuid4)
    tenant_id: Mapped[uuid.UUID] = mapped_column(Uuid(as_uuid=True), ForeignKey("tenants.id", ondelete="CASCADE"), nullable=False)
    branch_id: Mapped[uuid.UUID | None] = mapped_column(Uuid(as_uuid=True))
    code: Mapped[str] = mapped_column(String(40), nullable=False)
    name: Mapped[str] = mapped_column(String(160), nullable=False)
    is_active: Mapped[bool] = mapped_column(Boolean, nullable=False, default=True, server_default=text("true"))
    __table_args__ = (
        ForeignKeyConstraint(["tenant_id", "branch_id"], ["branches.tenant_id", "branches.id"], ondelete="RESTRICT", name="fk_warehouses_branch"),
        UniqueConstraint("tenant_id", "code", name="uq_warehouses_tenant_code"),
        UniqueConstraint("tenant_id", "id", name="uq_warehouses_tenant_id"),
    )


class StockMove(Base, TimestampMixin):
    __tablename__ = "stock_moves"
    id: Mapped[uuid.UUID] = mapped_column(Uuid(as_uuid=True), primary_key=True, default=uuid.uuid4)
    tenant_id: Mapped[uuid.UUID] = mapped_column(Uuid(as_uuid=True), ForeignKey("tenants.id", ondelete="CASCADE"), nullable=False)
    product_id: Mapped[uuid.UUID] = mapped_column(Uuid(as_uuid=True), nullable=False)
    source_warehouse_id: Mapped[uuid.UUID | None] = mapped_column(Uuid(as_uuid=True))
    destination_warehouse_id: Mapped[uuid.UUID | None] = mapped_column(Uuid(as_uuid=True))
    movement_date: Mapped[date] = mapped_column(Date, nullable=False)
    quantity: Mapped[Decimal] = mapped_column(Numeric(20, 4), nullable=False)
    unit_cost: Mapped[Decimal] = mapped_column(Numeric(20, 4), nullable=False, default=Decimal("0"), server_default="0")
    source_type: Mapped[str] = mapped_column(String(50), nullable=False)
    source_id: Mapped[str | None] = mapped_column(String(100))
    status: Mapped[str] = mapped_column(String(12), nullable=False, default="draft", server_default="draft")
    __table_args__ = (
        ForeignKeyConstraint(["tenant_id", "product_id"], ["products.tenant_id", "products.id"], ondelete="RESTRICT", name="fk_stock_moves_product"),
        ForeignKeyConstraint(["tenant_id", "source_warehouse_id"], ["warehouses.tenant_id", "warehouses.id"], ondelete="RESTRICT", name="fk_stock_moves_source_warehouse"),
        ForeignKeyConstraint(["tenant_id", "destination_warehouse_id"], ["warehouses.tenant_id", "warehouses.id"], ondelete="RESTRICT", name="fk_stock_moves_destination_warehouse"),
        CheckConstraint("quantity > 0 AND unit_cost >= 0", name="ck_stock_moves_values"),
        CheckConstraint("source_warehouse_id IS NOT NULL OR destination_warehouse_id IS NOT NULL", name="ck_stock_moves_direction"),
        CheckConstraint("source_warehouse_id IS NULL OR destination_warehouse_id IS NULL OR source_warehouse_id <> destination_warehouse_id", name="ck_stock_moves_different_warehouses"),
        CheckConstraint("status IN ('draft','posted','cancelled')", name="ck_stock_moves_status"),
        UniqueConstraint("tenant_id", "id", name="uq_stock_moves_tenant_id"),
        Index("ix_stock_moves_tenant_product_date", "tenant_id", "product_id", "movement_date"),
    )


class Invoice(Base, TimestampMixin):
    __tablename__ = "invoices"
    id: Mapped[uuid.UUID] = mapped_column(Uuid(as_uuid=True), primary_key=True, default=uuid.uuid4)
    tenant_id: Mapped[uuid.UUID] = mapped_column(Uuid(as_uuid=True), ForeignKey("tenants.id", ondelete="CASCADE"), nullable=False)
    branch_id: Mapped[uuid.UUID | None] = mapped_column(Uuid(as_uuid=True))
    warehouse_id: Mapped[uuid.UUID | None] = mapped_column(Uuid(as_uuid=True))
    contact_id: Mapped[uuid.UUID] = mapped_column(Uuid(as_uuid=True), nullable=False)
    journal_entry_id: Mapped[uuid.UUID | None] = mapped_column(Uuid(as_uuid=True))
    number: Mapped[str] = mapped_column(String(60), nullable=False)
    invoice_type: Mapped[str] = mapped_column(String(20), nullable=False)
    issue_date: Mapped[date] = mapped_column(Date, nullable=False)
    due_date: Mapped[date | None] = mapped_column(Date)
    currency_code: Mapped[str] = mapped_column(String(3), nullable=False, default="SAR", server_default="SAR")
    exchange_rate: Mapped[Decimal] = mapped_column(Numeric(20, 10), nullable=False, default=Decimal("1"), server_default="1")
    status: Mapped[str] = mapped_column(String(12), nullable=False, default="draft", server_default="draft")
    subtotal: Mapped[Decimal] = mapped_column(Numeric(20, 2), nullable=False, default=Decimal("0"), server_default="0")
    discount_total: Mapped[Decimal] = mapped_column(Numeric(20, 2), nullable=False, default=Decimal("0"), server_default="0")
    vat_total: Mapped[Decimal] = mapped_column(Numeric(20, 2), nullable=False, default=Decimal("0"), server_default="0")
    grand_total: Mapped[Decimal] = mapped_column(Numeric(20, 2), nullable=False, default=Decimal("0"), server_default="0")
    zatca_uuid: Mapped[str | None] = mapped_column(String(36))
    zatca_icv: Mapped[int | None] = mapped_column(BigInteger)
    previous_invoice_hash: Mapped[str | None] = mapped_column(String(128))
    qr_tlv_base64: Mapped[str | None] = mapped_column(Text)
    signed_xml_object_key: Mapped[str | None] = mapped_column(String(500))
    zatca_status: Mapped[str] = mapped_column(String(20), nullable=False, default="not_configured", server_default="not_configured")
    __table_args__ = (
        ForeignKeyConstraint(["tenant_id", "branch_id"], ["branches.tenant_id", "branches.id"], ondelete="RESTRICT", name="fk_invoices_branch"),
        ForeignKeyConstraint(["tenant_id", "warehouse_id"], ["warehouses.tenant_id", "warehouses.id"], ondelete="RESTRICT", name="fk_invoices_warehouse"),
        ForeignKeyConstraint(["tenant_id", "contact_id"], ["contacts.tenant_id", "contacts.id"], ondelete="RESTRICT", name="fk_invoices_contact"),
        ForeignKeyConstraint(["tenant_id", "journal_entry_id"], ["journal_entries.tenant_id", "journal_entries.id"], ondelete="RESTRICT", name="fk_invoices_journal"),
        CheckConstraint("invoice_type IN ('sales_invoice','purchase_bill','sales_credit_note','purchase_debit_note')", name="ck_invoices_type"),
        CheckConstraint("status IN ('draft','posted','cancelled')", name="ck_invoices_status"),
        CheckConstraint("exchange_rate > 0 AND subtotal >= 0 AND discount_total >= 0 AND vat_total >= 0 AND grand_total >= 0", name="ck_invoices_amounts"),
        CheckConstraint("zatca_status IN ('not_configured','pending','submitted','reported','cleared','rejected','failed')", name="ck_invoices_zatca_status"),
        UniqueConstraint("tenant_id", "number", name="uq_invoices_tenant_number"),
        UniqueConstraint("tenant_id", "zatca_uuid", name="uq_invoices_zatca_uuid"),
        UniqueConstraint("tenant_id", "id", name="uq_invoices_tenant_id"),
        Index("ix_invoices_tenant_type_date", "tenant_id", "invoice_type", "issue_date"),
    )


class InvoiceLine(Base):
    __tablename__ = "invoice_lines"
    id: Mapped[uuid.UUID] = mapped_column(Uuid(as_uuid=True), primary_key=True, default=uuid.uuid4)
    tenant_id: Mapped[uuid.UUID] = mapped_column(Uuid(as_uuid=True), ForeignKey("tenants.id", ondelete="CASCADE"), nullable=False)
    invoice_id: Mapped[uuid.UUID] = mapped_column(Uuid(as_uuid=True), nullable=False)
    line_no: Mapped[int] = mapped_column(SmallInteger, nullable=False)
    product_id: Mapped[uuid.UUID | None] = mapped_column(Uuid(as_uuid=True))
    description: Mapped[str] = mapped_column(String(300), nullable=False)
    quantity: Mapped[Decimal] = mapped_column(Numeric(20, 4), nullable=False)
    unit_price: Mapped[Decimal] = mapped_column(Numeric(20, 4), nullable=False)
    discount_percent: Mapped[Decimal] = mapped_column(Numeric(7, 4), nullable=False, default=Decimal("0"), server_default="0")
    tax_code: Mapped[str] = mapped_column(String(20), nullable=False, default="standard_15", server_default="standard_15")
    tax_rate: Mapped[Decimal] = mapped_column(Numeric(7, 4), nullable=False, default=Decimal("15"), server_default="15")
    net_amount: Mapped[Decimal] = mapped_column(Numeric(20, 2), nullable=False)
    tax_amount: Mapped[Decimal] = mapped_column(Numeric(20, 2), nullable=False)
    gross_amount: Mapped[Decimal] = mapped_column(Numeric(20, 2), nullable=False)
    __table_args__ = (
        ForeignKeyConstraint(["tenant_id", "invoice_id"], ["invoices.tenant_id", "invoices.id"], ondelete="CASCADE", name="fk_invoice_lines_invoice"),
        ForeignKeyConstraint(["tenant_id", "product_id"], ["products.tenant_id", "products.id"], ondelete="RESTRICT", name="fk_invoice_lines_product"),
        CheckConstraint("line_no >= 1 AND quantity > 0 AND unit_price >= 0", name="ck_invoice_lines_quantity_price"),
        CheckConstraint("discount_percent >= 0 AND discount_percent <= 100 AND tax_rate >= 0 AND tax_rate <= 100", name="ck_invoice_lines_rates"),
        CheckConstraint("net_amount >= 0 AND tax_amount >= 0 AND gross_amount >= 0", name="ck_invoice_lines_amounts"),
        UniqueConstraint("tenant_id", "invoice_id", "line_no", name="uq_invoice_lines_line"),
    )


class ExchangeRate(Base):
    __tablename__ = "exchange_rates"
    id: Mapped[uuid.UUID] = mapped_column(Uuid(as_uuid=True), primary_key=True, default=uuid.uuid4)
    tenant_id: Mapped[uuid.UUID] = mapped_column(Uuid(as_uuid=True), ForeignKey("tenants.id", ondelete="CASCADE"), nullable=False)
    base_currency: Mapped[str] = mapped_column(String(3), nullable=False)
    quote_currency: Mapped[str] = mapped_column(String(3), nullable=False)
    rate_date: Mapped[date] = mapped_column(Date, nullable=False)
    rate: Mapped[Decimal] = mapped_column(Numeric(20, 10), nullable=False)
    source: Mapped[str] = mapped_column(String(100), nullable=False, default="manual", server_default="manual")
    created_by: Mapped[uuid.UUID | None] = mapped_column(Uuid(as_uuid=True), ForeignKey("users.id", ondelete="SET NULL"))
    __table_args__ = (
        CheckConstraint("rate > 0 AND base_currency <> quote_currency", name="ck_exchange_rates_rate"),
        UniqueConstraint("tenant_id", "base_currency", "quote_currency", "rate_date", name="uq_exchange_rates_pair_date"),
    )
