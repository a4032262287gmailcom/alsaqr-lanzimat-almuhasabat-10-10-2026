from datetime import date
from uuid import UUID

from fastapi import APIRouter, Depends, HTTPException, Query, status
from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from app.database import get_db
from app.dependencies import TenantContext, admin_or_accountant, can_sell, get_tenant_context
from app.models import Invoice, InvoiceLine
from app.schemas import InvoiceCreate, InvoiceLineOut, InvoiceOut, OCRApprovalRequest, OCRDraftReviewRequest
from app.services.accounting import audit
from app.services.commerce import approve_ocr_purchase_draft, post_invoice, update_ocr_purchase_draft

router = APIRouter(prefix="/commerce", tags=["Sales and procurement"])


@router.post("/sales", response_model=InvoiceOut, status_code=status.HTTP_201_CREATED)
async def create_sale(
    payload: InvoiceCreate,
    context: TenantContext = Depends(can_sell),
    session: AsyncSession = Depends(get_db),
) -> InvoiceOut:
    invoice = await post_invoice(session, context.tenant_id, context.user_id, "SALE", payload)
    return InvoiceOut.model_validate(invoice)


@router.post("/purchases", response_model=InvoiceOut, status_code=status.HTTP_201_CREATED)
async def create_purchase(
    payload: InvoiceCreate,
    context: TenantContext = Depends(admin_or_accountant),
    session: AsyncSession = Depends(get_db),
) -> InvoiceOut:
    invoice = await post_invoice(session, context.tenant_id, context.user_id, "PURCHASE", payload)
    return InvoiceOut.model_validate(invoice)


@router.get("/invoices", response_model=list[InvoiceOut])
async def list_invoices(
    context: TenantContext = Depends(get_tenant_context),
    session: AsyncSession = Depends(get_db),
    kind: str | None = Query(default=None, pattern="^(SALE|PURCHASE)$"),
    invoice_status: str | None = Query(default=None, alias="status", pattern="^(DRAFT|POSTED|CANCELLED)$"),
    from_date: date | None = None,
    to_date: date | None = None,
    limit: int = Query(100, ge=1, le=500),
) -> list[InvoiceOut]:
    statement = select(Invoice).where(Invoice.tenant_id == context.tenant_id)
    if kind:
        statement = statement.where(Invoice.kind == kind)
    if invoice_status:
        statement = statement.where(Invoice.status == invoice_status)
    if from_date:
        statement = statement.where(Invoice.issue_date >= from_date)
    if to_date:
        statement = statement.where(Invoice.issue_date <= to_date)
    invoices = (await session.scalars(statement.order_by(Invoice.issue_date.desc(), Invoice.number.desc()).limit(limit))).all()
    return [InvoiceOut.model_validate(invoice) for invoice in invoices]


@router.get("/invoices/{invoice_id}", response_model=InvoiceOut)
async def get_invoice(
    invoice_id: UUID,
    context: TenantContext = Depends(get_tenant_context),
    session: AsyncSession = Depends(get_db),
) -> InvoiceOut:
    invoice = await session.scalar(select(Invoice).where(Invoice.tenant_id == context.tenant_id, Invoice.id == invoice_id))
    if invoice is None:
        raise HTTPException(status_code=404, detail="Invoice not found")
    return InvoiceOut.model_validate(invoice)


@router.get("/invoices/{invoice_id}/lines", response_model=list[InvoiceLineOut])
async def invoice_lines(
    invoice_id: UUID,
    context: TenantContext = Depends(get_tenant_context),
    session: AsyncSession = Depends(get_db),
) -> list[InvoiceLineOut]:
    if not await session.scalar(select(Invoice.id).where(Invoice.tenant_id == context.tenant_id, Invoice.id == invoice_id)):
        raise HTTPException(status_code=404, detail="Invoice not found")
    lines = (await session.scalars(select(InvoiceLine).where(
        InvoiceLine.tenant_id == context.tenant_id,
        InvoiceLine.invoice_id == invoice_id,
    ).order_by(InvoiceLine.created_at, InvoiceLine.id))).all()
    return [InvoiceLineOut.model_validate(line) for line in lines]


@router.put("/purchases/{invoice_id}/review-ocr-draft", response_model=InvoiceOut)
async def review_ocr_draft(
    invoice_id: UUID,
    payload: OCRDraftReviewRequest,
    context: TenantContext = Depends(admin_or_accountant),
    session: AsyncSession = Depends(get_db),
) -> InvoiceOut:
    invoice = await update_ocr_purchase_draft(session, context.tenant_id, context.user_id, invoice_id, payload)
    return InvoiceOut.model_validate(invoice)


@router.post("/purchases/{invoice_id}/approve-ocr-draft", response_model=InvoiceOut)
async def approve_ocr_draft(
    invoice_id: UUID,
    payload: OCRApprovalRequest,
    context: TenantContext = Depends(admin_or_accountant),
    session: AsyncSession = Depends(get_db),
) -> InvoiceOut:
    invoice = await approve_ocr_purchase_draft(
        session, context.tenant_id, context.user_id, invoice_id,
        is_credit=payload.is_credit,
        confirm_expense_only=payload.confirm_expense_only,
    )
    return InvoiceOut.model_validate(invoice)
