from datetime import datetime, timezone
from uuid import UUID

from fastapi import HTTPException
from sqlalchemy import func, select, text
from sqlalchemy.ext.asyncio import AsyncSession

from app.models import FiscalPeriod, JournalEntry, JournalLine
from app.services.accounting import audit
from app.services.approvals import validate_second_approver
from app.schemas import FiscalPeriodCreate


async def create_period(
    session: AsyncSession,
    tenant_id: UUID,
    actor_user_id: UUID,
    payload: FiscalPeriodCreate,
    remote_address: str | None = None,
) -> FiscalPeriod:
    if payload.start_date > payload.end_date:
        raise HTTPException(status_code=422, detail="Period start date must be on or before the end date")

    # Serialize period creation per tenant; a query-only overlap check is otherwise racy under concurrent requests.
    await session.execute(
        text("SELECT pg_advisory_xact_lock(hashtextextended(:lock_key, 0))"),
        {"lock_key": f"fiscal-period:{tenant_id}"},
    )
    overlap_id = await session.scalar(
        select(FiscalPeriod.id).where(
            FiscalPeriod.tenant_id == tenant_id,
            FiscalPeriod.start_date <= payload.end_date,
            FiscalPeriod.end_date >= payload.start_date,
        ).limit(1)
    )
    if overlap_id is not None:
        raise HTTPException(status_code=409, detail="Fiscal periods for a company cannot overlap")

    period = FiscalPeriod(
        tenant_id=tenant_id,
        code=payload.code.strip(),
        name=payload.name.strip(),
        start_date=payload.start_date,
        end_date=payload.end_date,
        status="OPEN",
    )
    session.add(period)
    await session.flush()
    await audit(
        session, tenant_id, actor_user_id, "FISCAL_PERIOD_CREATED",
        "FiscalPeriod", period.id,
        {"code": period.code, "start_date": period.start_date.isoformat(), "end_date": period.end_date.isoformat()},
        remote_address,
    )
    return period


async def close_period(
    session: AsyncSession,
    tenant_id: UUID,
    actor_user_id: UUID,
    period_id: UUID,
    reason: str,
    second_approver_email: str,
    second_approver_password: str,
    remote_address: str | None = None,
) -> FiscalPeriod:
    period = await session.scalar(
        select(FiscalPeriod).where(
            FiscalPeriod.tenant_id == tenant_id,
            FiscalPeriod.id == period_id,
        ).with_for_update()
    )
    if period is None:
        raise HTTPException(status_code=404, detail="Fiscal period not found")
    if period.status != "OPEN":
        raise HTTPException(status_code=409, detail="Fiscal period is already closed")

    second_approver = await validate_second_approver(
        session, tenant_id, actor_user_id, second_approver_email, second_approver_password
    )

    draft_count = await session.scalar(
        select(func.count()).select_from(JournalEntry).where(
            JournalEntry.tenant_id == tenant_id,
            JournalEntry.entry_date >= period.start_date,
            JournalEntry.entry_date <= period.end_date,
            JournalEntry.status == "DRAFT",
        )
    )
    if draft_count:
        raise HTTPException(status_code=409, detail="All draft journal entries in the period must be posted or removed before closing")

    unbalanced = await session.scalar(
        select(JournalEntry.id)
        .outerjoin(
            JournalLine,
            (JournalLine.tenant_id == JournalEntry.tenant_id) & (JournalLine.entry_id == JournalEntry.id),
        )
        .where(
            JournalEntry.tenant_id == tenant_id,
            JournalEntry.entry_date >= period.start_date,
            JournalEntry.entry_date <= period.end_date,
            JournalEntry.status.in_(("POSTED", "REVERSED")),
        )
        .group_by(JournalEntry.id)
        .having(
            (func.count(JournalLine.id) < 2)
            | (func.coalesce(func.sum(JournalLine.debit), 0) != func.coalesce(func.sum(JournalLine.credit), 0))
        )
        .limit(1)
    )
    if unbalanced is not None:
        raise HTTPException(status_code=409, detail="The period contains a journal that is missing lines or is not balanced")

    period.status = "CLOSED"
    period.closed_by = actor_user_id
    period.closed_at = datetime.now(timezone.utc)
    period.close_reason = reason.strip()
    period.reopened_by = None
    period.reopened_at = None
    period.reopen_reason = None
    await session.flush()
    await audit(
        session, tenant_id, actor_user_id, "FISCAL_PERIOD_CLOSED", "FiscalPeriod", period.id,
        {"code": period.code, "reason": reason.strip(), "second_approver_user_id": str(second_approver.id)},
        remote_address,
    )
    return period


async def reopen_period(
    session: AsyncSession,
    tenant_id: UUID,
    actor_user_id: UUID,
    period_id: UUID,
    reason: str,
    second_approver_email: str,
    second_approver_password: str,
    remote_address: str | None = None,
) -> FiscalPeriod:
    period = await session.scalar(
        select(FiscalPeriod).where(
            FiscalPeriod.tenant_id == tenant_id,
            FiscalPeriod.id == period_id,
        ).with_for_update()
    )
    if period is None:
        raise HTTPException(status_code=404, detail="Fiscal period not found")
    if period.status != "CLOSED":
        raise HTTPException(status_code=409, detail="Only a closed fiscal period can be reopened")

    second_approver = await validate_second_approver(
        session, tenant_id, actor_user_id, second_approver_email, second_approver_password
    )

    period.status = "OPEN"
    period.reopened_by = actor_user_id
    period.reopened_at = datetime.now(timezone.utc)
    period.reopen_reason = reason.strip()
    await session.flush()
    await audit(
        session, tenant_id, actor_user_id, "FISCAL_PERIOD_REOPENED", "FiscalPeriod", period.id,
        {"code": period.code, "reason": reason.strip(), "second_approver_user_id": str(second_approver.id)},
        remote_address,
    )
    return period
