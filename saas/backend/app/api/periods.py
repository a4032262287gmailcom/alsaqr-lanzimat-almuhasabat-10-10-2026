from datetime import date
from uuid import UUID

from fastapi import APIRouter, Depends, Query, Request, status
from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from app.database import get_db
from app.dependencies import TenantContext, get_tenant_context, require_roles
from app.models import FiscalPeriod
from app.schemas import FiscalPeriodCreate, FiscalPeriodOut, PeriodActionRequest
from app.services.periods import close_period, create_period, reopen_period

router = APIRouter(prefix="/periods", tags=["Fiscal period control"])
period_controller = require_roles("Owner", "Admin", "CFO")


@router.get("", response_model=list[FiscalPeriodOut])
async def list_periods(
    context: TenantContext = Depends(get_tenant_context),
    session: AsyncSession = Depends(get_db),
    from_date: date | None = None,
    to_date: date | None = None,
    limit: int = Query(100, ge=1, le=500),
) -> list[FiscalPeriodOut]:
    statement = select(FiscalPeriod).where(FiscalPeriod.tenant_id == context.tenant_id)
    if from_date is not None:
        statement = statement.where(FiscalPeriod.end_date >= from_date)
    if to_date is not None:
        statement = statement.where(FiscalPeriod.start_date <= to_date)
    rows = (await session.scalars(
        statement.order_by(FiscalPeriod.start_date.desc(), FiscalPeriod.code).limit(limit)
    )).all()
    return [FiscalPeriodOut.model_validate(row) for row in rows]


@router.post("", response_model=FiscalPeriodOut, status_code=status.HTTP_201_CREATED)
async def add_period(
    payload: FiscalPeriodCreate,
    request: Request,
    context: TenantContext = Depends(period_controller),
    session: AsyncSession = Depends(get_db),
) -> FiscalPeriodOut:
    period = await create_period(
        session, context.tenant_id, context.user_id, payload,
        request.client.host if request.client else None,
    )
    return FiscalPeriodOut.model_validate(period)


@router.post("/{period_id}/close", response_model=FiscalPeriodOut)
async def close_fiscal_period(
    period_id: UUID,
    payload: PeriodActionRequest,
    request: Request,
    context: TenantContext = Depends(period_controller),
    session: AsyncSession = Depends(get_db),
) -> FiscalPeriodOut:
    period = await close_period(
        session, context.tenant_id, context.user_id, period_id,
        payload.reason, str(payload.second_approver_email), payload.second_approver_password,
        request.client.host if request.client else None,
    )
    return FiscalPeriodOut.model_validate(period)


@router.post("/{period_id}/reopen", response_model=FiscalPeriodOut)
async def reopen_fiscal_period(
    period_id: UUID,
    payload: PeriodActionRequest,
    request: Request,
    context: TenantContext = Depends(period_controller),
    session: AsyncSession = Depends(get_db),
) -> FiscalPeriodOut:
    period = await reopen_period(
        session, context.tenant_id, context.user_id, period_id,
        payload.reason, str(payload.second_approver_email), payload.second_approver_password,
        request.client.host if request.client else None,
    )
    return FiscalPeriodOut.model_validate(period)
