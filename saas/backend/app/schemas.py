from datetime import date, datetime
from decimal import Decimal
from typing import Literal
from uuid import UUID

from pydantic import BaseModel, ConfigDict, EmailStr, Field, StringConstraints
from typing_extensions import Annotated


Slug = Annotated[str, StringConstraints(strip_whitespace=True, min_length=3, max_length=100, pattern=r"^[a-z0-9]+(?:-[a-z0-9]+)*$")]
Money = Annotated[Decimal, Field(ge=Decimal("0"), max_digits=20, decimal_places=2)]
Quantity = Annotated[Decimal, Field(gt=Decimal("0"), max_digits=20, decimal_places=4)]
Rate = Annotated[Decimal, Field(ge=Decimal("0"), le=Decimal("100"), max_digits=6, decimal_places=3)]


class APIModel(BaseModel):
    model_config = ConfigDict(from_attributes=True, str_strip_whitespace=True, extra="forbid")


class UserOut(APIModel):
    id: UUID
    email: EmailStr
    full_name: str


class MembershipOut(APIModel):
    tenant_id: UUID
    company_name: str
    company_slug: str
    role: Literal["Admin", "Accountant", "Cashier", "ReadOnly"]


class RegisterRequest(APIModel):
    email: EmailStr
    full_name: Annotated[str, Field(min_length=2, max_length=180)]
    password: Annotated[str, Field(min_length=12, max_length=128)]
    company_name: Annotated[str, Field(min_length=2, max_length=180)]
    company_slug: Slug | None = None
    legal_name: Annotated[str | None, Field(max_length=240)] = None
    tax_number: Annotated[str | None, Field(max_length=32)] = None


class LoginRequest(APIModel):
    email: EmailStr
    password: Annotated[str, Field(min_length=1, max_length=128)]


class AuthResponse(APIModel):
    access_token: str
    token_type: str = "bearer"
    expires_in_seconds: int
    user: UserOut
    memberships: list[MembershipOut]


class AccountCreate(APIModel):
    code: Annotated[str | None, Field(min_length=1, max_length=32)] = None
    name: Annotated[str, Field(min_length=2, max_length=180)]
    parent_id: UUID | None = None
    statement_type: Literal["BALANCE_SHEET", "INCOME_STATEMENT"]
    nature: Literal["DEBIT", "CREDIT"]
    account_category: Literal["ASSET", "LIABILITY", "EQUITY", "REVENUE", "EXPENSE"]
    allow_posting: bool = True


class AccountOut(APIModel):
    id: UUID
    code: str
    name: str
    parent_id: UUID | None
    statement_type: str
    nature: str
    account_category: str
    allow_posting: bool
    is_system: bool
    is_active: bool


class CostCenterCreate(APIModel):
    code: Annotated[str, Field(min_length=1, max_length=32)]
    name: Annotated[str, Field(min_length=2, max_length=180)]
    parent_id: UUID | None = None


class CostCenterOut(APIModel):
    id: UUID
    code: str
    name: str
    parent_id: UUID | None
    is_active: bool


class JournalLineInput(APIModel):
    account_id: UUID
    debit: Money = Decimal("0")
    credit: Money = Decimal("0")
    description: Annotated[str | None, Field(max_length=500)] = None
    contact_id: UUID | None = None
    cost_center_id: UUID | None = None

    @classmethod
    def validate_side(cls, values):
        return values


class JournalCreate(APIModel):
    entry_date: date
    memo: Annotated[str, Field(min_length=2, max_length=500)]
    lines: Annotated[list[JournalLineInput], Field(min_length=2, max_length=500)]


class JournalLineOut(APIModel):
    id: UUID
    account_id: UUID
    contact_id: UUID | None
    cost_center_id: UUID | None
    description: str | None
    debit: Decimal
    credit: Decimal


class JournalOut(APIModel):
    id: UUID
    number: str
    entry_date: date
    memo: str
    status: str
    source_type: str
    posted_at: datetime | None


class ContactCreate(APIModel):
    name: Annotated[str, Field(min_length=2, max_length=220)]
    contact_type: Literal["CUSTOMER", "SUPPLIER", "BOTH"] = "CUSTOMER"
    email: EmailStr | None = None
    phone: Annotated[str | None, Field(max_length=40)] = None
    tax_number: Annotated[str | None, Field(max_length=32)] = None
    address: Annotated[str | None, Field(max_length=2000)] = None


class ContactOut(APIModel):
    id: UUID
    name: str
    contact_type: str
    email: str | None
    phone: str | None
    tax_number: str | None
    address: str | None
    is_active: bool


class WarehouseCreate(APIModel):
    code: Annotated[str, Field(min_length=1, max_length=32)]
    name: Annotated[str, Field(min_length=2, max_length=160)]
    address: Annotated[str | None, Field(max_length=2000)] = None
    is_default: bool = False


class WarehouseOut(APIModel):
    id: UUID
    code: str
    name: str
    address: str | None
    is_default: bool
    is_active: bool


class ProductCreate(APIModel):
    sku: Annotated[str, Field(min_length=1, max_length=80)]
    name: Annotated[str, Field(min_length=2, max_length=220)]
    description: Annotated[str | None, Field(max_length=2000)] = None
    item_type: Literal["STOCK", "SERVICE", "MANUFACTURED"] = "STOCK"
    barcode: Annotated[str | None, Field(max_length=128)] = None
    unit_of_measure: Annotated[str, Field(min_length=1, max_length=32)] = "حبة"
    standard_cost: Money = Decimal("0")
    unit_price: Money
    vat_rate: Rate = Decimal("15")


class ProductOut(APIModel):
    id: UUID
    sku: str
    name: str
    description: str | None
    item_type: str
    barcode: str | None
    unit_of_measure: str
    standard_cost: Decimal
    unit_price: Decimal
    vat_rate: Decimal
    is_active: bool


class InvoiceItemInput(APIModel):
    product_id: UUID | None = None
    account_id: UUID | None = None
    cost_center_id: UUID | None = None
    description: Annotated[str | None, Field(max_length=400)] = None
    quantity: Quantity
    unit_price: Money
    discount_rate: Rate = Decimal("0")
    vat_rate: Rate | None = None


class InvoiceCreate(APIModel):
    issue_date: date = Field(default_factory=date.today)
    contact_id: UUID
    warehouse_id: UUID | None = None
    is_credit: bool = True
    currency_code: Annotated[str, Field(min_length=3, max_length=3)] = "SAR"
    exchange_rate: Annotated[Decimal, Field(gt=Decimal("0"), max_digits=20, decimal_places=8)] = Decimal("1")
    client_reference: Annotated[str | None, Field(max_length=120)] = None
    items: Annotated[list[InvoiceItemInput], Field(min_length=1, max_length=500)]


class InvoiceLineOut(APIModel):
    id: UUID
    product_id: UUID | None
    account_id: UUID | None
    description: str
    quantity: Decimal
    unit_price: Decimal
    discount_rate: Decimal
    vat_rate: Decimal
    net_amount: Decimal
    vat_amount: Decimal
    total_amount: Decimal


class InvoiceOut(APIModel):
    id: UUID
    kind: str
    number: str
    issue_date: date
    contact_id: UUID
    warehouse_id: UUID | None
    is_credit: bool
    status: str
    currency_code: str
    subtotal: Decimal
    vat_total: Decimal
    total: Decimal
    journal_entry_id: UUID | None
    client_reference: str | None
    qr_payload: str | None
    requires_review: bool
    posted_at: datetime | None


class StockTransferRequest(APIModel):
    product_id: UUID
    source_warehouse_id: UUID
    destination_warehouse_id: UUID
    quantity: Quantity
    reference: Annotated[str | None, Field(max_length=120)] = None
    memo: Annotated[str | None, Field(max_length=500)] = None


class StockBalanceOut(APIModel):
    id: UUID
    warehouse_id: UUID
    product_id: UUID
    quantity: Decimal
    average_cost: Decimal


class OCRDraftLineReview(APIModel):
    product_id: UUID | None = None
    account_id: UUID | None = None
    cost_center_id: UUID | None = None
    description: Annotated[str, Field(min_length=2, max_length=400)]
    quantity: Quantity
    unit_price: Money
    discount_rate: Rate = Decimal("0")
    vat_rate: Rate = Decimal("15")


class OCRDraftReviewRequest(APIModel):
    warehouse_id: UUID | None = None
    lines: Annotated[list[OCRDraftLineReview], Field(min_length=1, max_length=500)]


class OCRApprovalRequest(APIModel):
    is_credit: bool = True
    confirm_expense_only: bool = False


class OCRDraftOut(APIModel):
    invoice: InvoiceOut
    extracted_fields: dict
    warnings: list[str]


class HealthOut(APIModel):
    status: str
    service: str
