from datetime import date
from decimal import Decimal
from uuid import UUID

from fastapi import APIRouter, Depends, HTTPException, Query, Request, status
from sqlalchemy import and_, case, func, select
from sqlalchemy.ext.asyncio import AsyncSession

from app.api.setup import create_account as create_setup_account
from app.database import get_db
from app.dependencies import TenantContext, admin_or_accountant, get_tenant_context
from app.models import Account, JournalEntry, JournalLine
from app.schemas import AccountCreate, AccountOut, JournalCreate, JournalLineOut, JournalOut, JournalReverseRequest
from app.services.accounting import audit, create_journal, money, reverse_journal

router = APIRouter(prefix="/accounting", tags=["General ledger"])


@router.get("/accounts", response_model=list[AccountOut])
async def accounts(
    context: TenantContext = Depends(get_tenant_context),
    session: AsyncSession = Depends(get_db),
    include_inactive: bool = Query(False),
) -> list[AccountOut]:
    statement = select(Account).where(Account.tenant_id == context.tenant_id)
    if not include_inactive:
        statement = statement.where(Account.is_active.is_(True))
    rows = (await session.scalars(statement.order_by(Account.code))).all()
    return [AccountOut.model_validate(row) for row in rows]


@router.post("/accounts", response_model=AccountOut, status_code=status.HTTP_201_CREATED)
async def add_account(
    payload: AccountCreate,
    context: TenantContext = Depends(admin_or_accountant),
    session: AsyncSession = Depends(get_db),
) -> AccountOut:
    return await create_setup_account(payload, context, session)


@router.post("/journals", response_model=JournalOut, status_code=status.HTTP_201_CREATED)
async def post_manual_journal(
    payload: JournalCreate,
    request: Request,
    context: TenantContext = Depends(admin_or_accountant),
    session: AsyncSession = Depends(get_db),
) -> JournalOut:
    lines = [line.model_dump() for line in payload.lines]
    for line in lines:
        for relation, label in (("contact_id", "contact"), ("cost_center_id", "cost center")):
            if line.get(relation):
                from app.models import Contact, CostCenter
                model = Contact if relation == "contact_id" else CostCenter
                related = await session.scalar(select(model.id).where(
                    model.tenant_id == context.tenant_id,
                    model.id == line[relation],
                    model.is_active.is_(True),
                ))
                if related is None:
                    raise HTTPException(status_code=422, detail=f"Journal line {label} is missing or inactive")
    journal = await create_journal(
        session=session,
        tenant_id=context.tenant_id,
        user_id=context.user_id,
        entry_date=payload.entry_date,
        memo=payload.memo,
        source_type="MANUAL",
        lines=lines,
    )
    await audit(session, context.tenant_id, context.user_id, "MANUAL_JOURNAL_POSTED", "JournalEntry", journal.id, {
        "number": journal.number,
        "lines": len(lines),
    }, remote_address=request.client.host if request.client else None)
    return JournalOut.model_validate(journal)


@router.get("/journals", response_model=list[JournalOut])
async def list_journals(
    context: TenantContext = Depends(get_tenant_context),
    session: AsyncSession = Depends(get_db),
    from_date: date | None = None,
    to_date: date | None = None,
    limit: int = Query(100, ge=1, le=500),
) -> list[JournalOut]:
    statement = select(JournalEntry).where(JournalEntry.tenant_id == context.tenant_id)
    if from_date:
        statement = statement.where(JournalEntry.entry_date >= from_date)
    if to_date:
        statement = statement.where(JournalEntry.entry_date <= to_date)
    rows = (await session.scalars(statement.order_by(JournalEntry.entry_date.desc(), JournalEntry.number.desc()).limit(limit))).all()
    return [JournalOut.model_validate(row) for row in rows]


@router.get("/journals/{journal_id}/lines", response_model=list[JournalLineOut])
async def journal_lines(
    journal_id: UUID,
    context: TenantContext = Depends(get_tenant_context),
    session: AsyncSession = Depends(get_db),
) -> list[JournalLineOut]:
    exists = await session.scalar(select(JournalEntry.id).where(
        JournalEntry.tenant_id == context.tenant_id,
        JournalEntry.id == journal_id,
    ))
    if exists is None:
        raise HTTPException(status_code=404, detail="Journal entry not found")
    lines = (await session.scalars(select(JournalLine).where(
        JournalLine.tenant_id == context.tenant_id,
        JournalLine.entry_id == journal_id,
    ).order_by(JournalLine.created_at, JournalLine.id))).all()
    return [JournalLineOut.model_validate(line) for line in lines]


@router.post("/journals/{journal_id}/reverse", response_model=JournalOut, status_code=status.HTTP_201_CREATED)
async def reverse_posted_journal(
    journal_id: UUID,
    payload: JournalReverseRequest,
    request: Request,
    context: TenantContext = Depends(admin_or_accountant),
    session: AsyncSession = Depends(get_db),
) -> JournalOut:
    reversal = await reverse_journal(
        session=session,
        tenant_id=context.tenant_id,
        user_id=context.user_id,
        journal_id=journal_id,
        reversal_date=payload.reversal_date,
        reason=payload.reason,
        second_approver_email=str(payload.second_approver_email),
        second_approver_password=payload.second_approver_password,
        remote_address=request.client.host if request.client else None,
    )
    return JournalOut.model_validate(reversal)


@router.get("/trial-balance")
async def trial_balance(
    context: TenantContext = Depends(get_tenant_context),
    session: AsyncSession = Depends(get_db),
    from_date: date = Query(default_factory=lambda: date(date.today().year, 1, 1)),
    to_date: date = Query(default_factory=date.today),
) -> dict:
    if from_date > to_date:
        raise HTTPException(status_code=422, detail="Start date cannot be after end date")

    debit_sum = func.coalesce(func.sum(JournalLine.debit), 0)
    credit_sum = func.coalesce(func.sum(JournalLine.credit), 0)
    stmt = (
        select(
            Account.id.label("account_id"),
            Account.code.label("code"),
            Account.name.label("name"),
            Account.account_category.label("account_category"),
            Account.nature.label("nature"),
            debit_sum.label("debit"),
            credit_sum.label("credit"),
        )
        .select_from(Account)
        .outerjoin(
            JournalLine,
            and_(JournalLine.tenant_id == Account.tenant_id, JournalLine.account_id == Account.id),
        )
        .outerjoin(
            JournalEntry,
            and_(
                JournalEntry.tenant_id == JournalLine.tenant_id,
                JournalEntry.id == JournalLine.entry_id,
                JournalEntry.status.in_(("POSTED", "REVERSED")),
                JournalEntry.entry_date >= from_date,
                JournalEntry.entry_date <= to_date,
            ),
        )
        .where(Account.tenant_id == context.tenant_id, Account.is_active.is_(True))
        .group_by(Account.id, Account.code, Account.name, Account.account_category, Account.nature)
        .order_by(Account.code)
    )
    # Restrict the sums to entries matching the reporting period while keeping accounts with no activity.
    stmt = stmt.with_only_columns(
        Account.id.label("account_id"),
        Account.code.label("code"),
        Account.name.label("name"),
        Account.account_category.label("account_category"),
        Account.nature.label("nature"),
        func.coalesce(func.sum(case((JournalEntry.id.is_not(None), JournalLine.debit), else_=0)), 0).label("debit"),
        func.coalesce(func.sum(case((JournalEntry.id.is_not(None), JournalLine.credit), else_=0)), 0).label("credit"),
    )
    rows = (await session.execute(stmt)).mappings().all()
    entries = []
    total_debit = Decimal("0.00")
    total_credit = Decimal("0.00")
    for row in rows:
        debit = money(row["debit"])
        credit = money(row["credit"])
        total_debit += debit
        total_credit += credit
        entries.append({
            "account_id": str(row["account_id"]),
            "code": row["code"],
            "name": row["name"],
            "account_category": row["account_category"],
            "nature": row["nature"],
            "debit": str(debit),
            "credit": str(credit),
            "net_debit": str(money(debit - credit)),
        })
    return {
        "from_date": from_date.isoformat(),
        "to_date": to_date.isoformat(),
        "accounts": entries,
        "total_debit": str(money(total_debit)),
        "total_credit": str(money(total_credit)),
        "balanced": money(total_debit) == money(total_credit),
    }
