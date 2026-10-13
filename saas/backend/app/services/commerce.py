from datetime import datetime, timezone
from decimal import Decimal, ROUND_HALF_UP
from uuid import UUID

from fastapi import HTTPException
from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from app.models import Contact, Invoice, InvoiceLine, Tenant
from app.schemas import InvoiceCreate
from app.services.accounting import audit, create_journal, get_account_by_code, money, next_number
from app.services.inventory import get_or_create_balance, get_product, get_warehouse, issue_stock, receive_stock
from app.services.zatca import build_phase_one_qr

FOUR_PLACES = Decimal("0.0001")


def unit_amount(value: Decimal) -> Decimal:
    return Decimal(value).quantize(FOUR_PLACES, rounding=ROUND_HALF_UP)


async def post_invoice(
    session: AsyncSession,
    tenant_id: UUID,
    user_id: UUID,
    kind: str,
    payload: InvoiceCreate,
) -> Invoice:
    if kind not in {"SALE", "PURCHASE"}:
        raise HTTPException(status_code=422, detail="Unsupported invoice kind")

    if payload.client_reference:
        existing = await session.scalar(select(Invoice).where(
            Invoice.tenant_id == tenant_id,
            Invoice.client_reference == payload.client_reference,
        ))
        if existing is not None:
            if existing.kind != kind:
                raise HTTPException(status_code=409, detail="Client reference was already used for a different document type")
            return existing

    contact = await session.scalar(select(Contact).where(
        Contact.tenant_id == tenant_id,
        Contact.id == payload.contact_id,
        Contact.is_active.is_(True),
    ))
    if contact is None:
        raise HTTPException(status_code=422, detail="Customer or supplier is missing or inactive")
    if kind == "SALE" and contact.contact_type not in {"CUSTOMER", "BOTH"}:
        raise HTTPException(status_code=422, detail="Sales invoices require an active customer")
    if kind == "PURCHASE" and contact.contact_type not in {"SUPPLIER", "BOTH"}:
        raise HTTPException(status_code=422, detail="Purchase bills require an active supplier")

    tenant = await session.get(Tenant, tenant_id)
    if tenant is None:
        raise HTTPException(status_code=404, detail="Company not found")

    products = []
    needs_warehouse = False
    for item in payload.items:
        product = None
        if item.product_id:
            product = await get_product(session, tenant_id, item.product_id, lock=True)
        if kind == "SALE" and product is None:
            raise HTTPException(status_code=422, detail="Every sales line must reference a registered product or service")
        if kind == "PURCHASE" and product is None and item.account_id is None:
            raise HTTPException(status_code=422, detail="Purchase lines without a product must specify an expense account")
        if product is not None and product.item_type != "SERVICE":
            needs_warehouse = True
        products.append(product)

    warehouse = None
    if payload.warehouse_id:
        warehouse = await get_warehouse(session, tenant_id, payload.warehouse_id, lock=True)
    if needs_warehouse and warehouse is None:
        raise HTTPException(status_code=422, detail="Choose an active warehouse for stock items")

    document_prefix = "SAL" if kind == "SALE" else "PUR"
    number = await next_number(
        session, tenant_id, "SALE" if kind == "SALE" else "PURCHASE", document_prefix, payload.issue_date
    )
    invoice = Invoice(
        tenant_id=tenant_id,
        kind=kind,
        number=number,
        issue_date=payload.issue_date,
        contact_id=contact.id,
        warehouse_id=warehouse.id if warehouse else None,
        is_credit=payload.is_credit,
        status="DRAFT",
        currency_code=payload.currency_code.upper(),
        exchange_rate=payload.exchange_rate,
        client_reference=payload.client_reference,
        requires_review=False,
    )
    session.add(invoice)
    await session.flush()

    line_values: list[dict] = []
    subtotal = Decimal("0.00")
    vat_total = Decimal("0.00")
    receivable_or_payable: list[dict] = []
    revenue_entries: list[dict] = []
    purchase_entries: list[dict] = []
    cogs_entries: list[dict] = []
    output_vat = await get_account_by_code(session, tenant_id, "2201")
    input_vat = await get_account_by_code(session, tenant_id, "1301")
    cash_account = await get_account_by_code(session, tenant_id, "1101")
    control_account = await get_account_by_code(
        session, tenant_id, "1103" if kind == "SALE" and payload.is_credit else
        "2101" if kind == "PURCHASE" and payload.is_credit else
        "1101"
    )

    for item, product in zip(payload.items, products, strict=True):
        if item.quantity <= 0 or item.unit_price < 0:
            raise HTTPException(status_code=422, detail="Quantity must be positive and unit price cannot be negative")

        discount_factor = Decimal("1") - Decimal(item.discount_rate) / Decimal("100")
        net_amount = money(Decimal(item.quantity) * Decimal(item.unit_price) * discount_factor)
        vat_rate = Decimal(item.vat_rate) if item.vat_rate is not None else (Decimal(product.vat_rate) if product else Decimal("15"))
        vat_amount = money(net_amount * vat_rate / Decimal("100"))
        total_amount = money(net_amount + vat_amount)
        if total_amount < 0:
            raise HTTPException(status_code=422, detail="Line total cannot be negative")
        description = (item.description or (product.name if product else "مشتريات / مصروف")).strip()
        if not description:
            raise HTTPException(status_code=422, detail="Line description is required")

        account_id = item.account_id
        cost_center_id = item.cost_center_id
        if cost_center_id:
            from app.models import CostCenter
            center = await session.scalar(select(CostCenter).where(
                CostCenter.tenant_id == tenant_id,
                CostCenter.id == cost_center_id,
                CostCenter.is_active.is_(True),
            ))
            if center is None:
                raise HTTPException(status_code=422, detail="Cost center is missing or inactive")

        if kind == "SALE":
            if product is None:
                raise HTTPException(status_code=422, detail="Sales line product is required")
            revenue_account = await get_account_by_code(session, tenant_id, "4101")
            if product.revenue_account_id:
                from app.models import Account
                custom_revenue = await session.scalar(select(Account).where(
                    Account.tenant_id == tenant_id,
                    Account.id == product.revenue_account_id,
                    Account.is_active.is_(True),
                    Account.allow_posting.is_(True),
                ))
                if custom_revenue is None:
                    raise HTTPException(status_code=422, detail=f"Revenue account for {product.name} is unavailable")
                revenue_account = custom_revenue
            account_id = revenue_account.id
            unit_cost_at_posting = Decimal("0.0000")
            if product.item_type != "SERVICE":
                assert warehouse is not None
                balance = await get_or_create_balance(session, tenant_id, warehouse.id, product.id)
                if Decimal(balance.quantity) < Decimal(item.quantity):
                    raise HTTPException(status_code=422, detail={
                        "message": "Insufficient stock",
                        "product": product.name,
                        "available": str(balance.quantity),
                        "requested": str(item.quantity),
                        "warehouse_id": str(warehouse.id),
                    })
                unit_cost_at_posting = unit_amount(Decimal(balance.average_cost))
                cost_total = money(unit_cost_at_posting * Decimal(item.quantity))
                if cost_total > 0:
                    cogs_account = await get_account_by_code(session, tenant_id, "5101")
                    inventory_account = await get_account_by_code(session, tenant_id, "1201")
                    cogs_entries.append({
                        "account_id": cogs_account.id,
                        "debit": cost_total,
                        "credit": Decimal("0"),
                        "description": f"تكلفة مبيعات {product.sku}",
                        "cost_center_id": cost_center_id,
                    })
                    cogs_entries.append({
                        "account_id": inventory_account.id,
                        "debit": Decimal("0"),
                        "credit": cost_total,
                        "description": f"تخفيض مخزون {product.sku}",
                        "cost_center_id": cost_center_id,
                    })
                await issue_stock(
                    session, tenant_id, product, warehouse.id, Decimal(item.quantity),
                    "SALE", invoice.id, invoice.number,
                )
            revenue_entries.append({
                "account_id": revenue_account.id,
                "debit": Decimal("0"),
                "credit": net_amount,
                "description": description,
                "cost_center_id": cost_center_id,
            })
            line_values.append({
                "product_id": product.id,
                "account_id": account_id,
                "cost_center_id": cost_center_id,
                "description": description,
                "quantity": item.quantity,
                "unit_price": item.unit_price,
                "discount_rate": item.discount_rate,
                "vat_rate": vat_rate,
                "net_amount": net_amount,
                "vat_amount": vat_amount,
                "total_amount": total_amount,
                "unit_cost_at_posting": unit_cost_at_posting,
            })
        else:
            if product is not None and product.item_type != "SERVICE":
                inventory_account = await get_account_by_code(session, tenant_id, "1201")
                account_id = product.inventory_account_id or inventory_account.id
                unit_cost = unit_amount(net_amount / Decimal(item.quantity))
                assert warehouse is not None
                await receive_stock(
                    session, tenant_id, product, warehouse.id, Decimal(item.quantity), unit_cost,
                    "PURCHASE", invoice.id, invoice.number,
                )
                purchase_account = account_id
            elif product is not None:
                account_id = product.expense_account_id or (await get_account_by_code(session, tenant_id, "5201")).id
                purchase_account = account_id
                unit_cost = Decimal("0.0000")
            else:
                from app.models import Account
                account = await session.scalar(select(Account).where(
                    Account.tenant_id == tenant_id,
                    Account.id == item.account_id,
                    Account.is_active.is_(True),
                    Account.allow_posting.is_(True),
                ))
                if account is None:
                    raise HTTPException(status_code=422, detail="Purchase expense account is unavailable")
                account_id = account.id
                purchase_account = account.id
                unit_cost = Decimal("0.0000")
            purchase_entries.append({
                "account_id": purchase_account,
                "debit": net_amount,
                "credit": Decimal("0"),
                "description": description,
                "cost_center_id": cost_center_id,
            })
            line_values.append({
                "product_id": product.id if product else None,
                "account_id": account_id,
                "cost_center_id": cost_center_id,
                "description": description,
                "quantity": item.quantity,
                "unit_price": item.unit_price,
                "discount_rate": item.discount_rate,
                "vat_rate": vat_rate,
                "net_amount": net_amount,
                "vat_amount": vat_amount,
                "total_amount": total_amount,
                "unit_cost_at_posting": unit_cost,
            })

        subtotal += net_amount
        vat_total += vat_amount

    subtotal, vat_total = money(subtotal), money(vat_total)
    invoice.subtotal = subtotal
    invoice.vat_total = vat_total
    invoice.total = money(subtotal + vat_total)

    for values in line_values:
        session.add(InvoiceLine(tenant_id=tenant_id, invoice_id=invoice.id, **values))
    await session.flush()

    if kind == "SALE":
        settlement_account = control_account if payload.is_credit else cash_account
        journal_lines = [{
            "account_id": settlement_account.id,
            "debit": invoice.total,
            "credit": Decimal("0"),
            "description": f"ذمم / تحصيل فاتورة {invoice.number}",
            "contact_id": contact.id if payload.is_credit else None,
        }]
        journal_lines.extend(revenue_entries)
        if vat_total > 0:
            journal_lines.append({
                "account_id": output_vat.id,
                "debit": Decimal("0"),
                "credit": vat_total,
                "description": "ضريبة القيمة المضافة - مخرجات",
            })
        journal_lines.extend(cogs_entries)
    else:
        settlement_account = control_account if payload.is_credit else cash_account
        journal_lines = list(purchase_entries)
        if vat_total > 0:
            journal_lines.append({
                "account_id": input_vat.id,
                "debit": vat_total,
                "credit": Decimal("0"),
                "description": "ضريبة القيمة المضافة - مدخلات",
            })
        journal_lines.append({
            "account_id": settlement_account.id,
            "debit": Decimal("0"),
            "credit": invoice.total,
            "description": f"ذمم / سداد فاتورة {invoice.number}",
            "contact_id": contact.id if payload.is_credit else None,
        })

    journal = await create_journal(
        session, tenant_id, user_id, payload.issue_date,
        f"{'فاتورة بيع' if kind == 'SALE' else 'فاتورة شراء'} {invoice.number}",
        kind, journal_lines, source_id=invoice.id,
    )
    invoice.journal_entry_id = journal.id
    invoice.status = "POSTED"
    invoice.posted_at = datetime.now(timezone.utc)

    if kind == "SALE" and tenant.tax_number and tenant.name:
        invoice.qr_payload = build_phase_one_qr(
            tenant.legal_name or tenant.name,
            tenant.tax_number,
            invoice.posted_at,
            invoice.total,
            invoice.vat_total,
        )

    await audit(session, tenant_id, user_id, f"{kind}_POSTED", "Invoice", invoice.id, {
        "number": invoice.number,
        "contact_id": str(contact.id),
        "journal_entry_id": str(journal.id),
        "subtotal": str(invoice.subtotal),
        "vat_total": str(invoice.vat_total),
        "total": str(invoice.total),
        "currency_code": invoice.currency_code,
    })
    await session.flush()
    return invoice


async def approve_ocr_purchase_draft(
    session: AsyncSession,
    tenant_id: UUID,
    user_id: UUID,
    invoice_id: UUID,
    is_credit: bool = True,
) -> Invoice:
    invoice = await session.scalar(select(Invoice).where(
        Invoice.tenant_id == tenant_id,
        Invoice.id == invoice_id,
        Invoice.kind == "PURCHASE",
    ).with_for_update())
    if invoice is None:
        raise HTTPException(status_code=404, detail="Purchase draft not found")
    if invoice.status != "DRAFT" or not invoice.requires_review:
        raise HTTPException(status_code=409, detail="This invoice is not an OCR draft awaiting approval")

    lines = (await session.scalars(select(InvoiceLine).where(
        InvoiceLine.tenant_id == tenant_id,
        InvoiceLine.invoice_id == invoice.id,
    ))).all()
    if not lines:
        raise HTTPException(status_code=422, detail="Draft must contain at least one line")
    subtotal = money(sum((Decimal(line.net_amount) for line in lines), Decimal("0")))
    vat_total = money(sum((Decimal(line.vat_amount) for line in lines), Decimal("0")))
    if money(subtotal + vat_total) != money(invoice.total):
        raise HTTPException(status_code=422, detail="Draft totals do not reconcile; review the invoice before approval")
    contact = await session.scalar(select(Contact).where(
        Contact.tenant_id == tenant_id,
        Contact.id == invoice.contact_id,
        Contact.is_active.is_(True),
    ))
    if contact is None or contact.contact_type not in {"SUPPLIER", "BOTH"}:
        raise HTTPException(status_code=422, detail="Select a valid supplier before approval")

    from app.models import Account
    expense_fallback = await get_account_by_code(session, tenant_id, "5201")
    input_vat = await get_account_by_code(session, tenant_id, "1301")
    payable = await get_account_by_code(session, tenant_id, "2101")
    cash = await get_account_by_code(session, tenant_id, "1101")
    journal_lines = []
    for line in lines:
        account_id = line.account_id or expense_fallback.id
        account = await session.scalar(select(Account).where(
            Account.tenant_id == tenant_id,
            Account.id == account_id,
            Account.is_active.is_(True),
            Account.allow_posting.is_(True),
        ))
        if account is None:
            raise HTTPException(status_code=422, detail="Draft expense account is missing or inactive")
        journal_lines.append({
            "account_id": account.id,
            "debit": money(line.net_amount),
            "credit": Decimal("0"),
            "description": line.description,
            "cost_center_id": line.cost_center_id,
        })
    if vat_total > 0:
        journal_lines.append({"account_id": input_vat.id, "debit": vat_total, "credit": Decimal("0"), "description": "ضريبة مدخلات"})
    journal_lines.append({
        "account_id": payable.id if is_credit else cash.id,
        "debit": Decimal("0"),
        "credit": money(invoice.total),
        "description": f"اعتماد فاتورة مشتريات {invoice.number}",
        "contact_id": contact.id if is_credit else None,
    })
    journal = await create_journal(
        session, tenant_id, user_id, invoice.issue_date,
        f"اعتماد فاتورة مشتريات OCR {invoice.number}", "PURCHASE", journal_lines, source_id=invoice.id,
    )
    invoice.journal_entry_id = journal.id
    invoice.is_credit = is_credit
    invoice.status = "POSTED"
    invoice.requires_review = False
    invoice.posted_at = datetime.now(timezone.utc)
    await audit(session, tenant_id, user_id, "OCR_PURCHASE_APPROVED", "Invoice", invoice.id, {
        "journal_entry_id": str(journal.id),
        "classification": "expense_only_reviewed",
    })
    await session.flush()
    return invoice
