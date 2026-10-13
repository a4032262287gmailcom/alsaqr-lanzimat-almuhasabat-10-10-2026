import asyncio
from datetime import date
from decimal import Decimal
from uuid import uuid4

import pytest
from fastapi import HTTPException
from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from app.core.security import hash_password
from app.database import SessionFactory, set_tenant_context
from app.models import (
    AuditLog,
    FiscalPeriod,
    JournalEntry,
    JournalLine,
    Membership,
    Tenant,
    User,
)
from app.schemas import FiscalPeriodCreate
from app.services.accounting import (
    create_journal,
    next_number,
    reverse_journal,
    seed_chart_of_accounts,
    validate_balanced_lines,
)
from app.services.periods import close_period, create_period, reopen_period


def test_journal_rejects_unbalanced_amounts() -> None:
    with pytest.raises(HTTPException) as captured:
        validate_balanced_lines([
            {"debit": Decimal("100.00"), "credit": Decimal("0.00")},
            {"debit": Decimal("0.00"), "credit": Decimal("99.99")},
        ])
    assert captured.value.status_code == 422
    assert captured.value.detail["total_debit"] == "100.00"
    assert captured.value.detail["total_credit"] == "99.99"


def test_journal_requires_two_single_sided_lines() -> None:
    invalid_sets = [
        [],
        [{"debit": Decimal("10.00"), "credit": Decimal("0.00")}],
        [
            {"debit": Decimal("10.00"), "credit": Decimal("10.00")},
            {"debit": Decimal("0.00"), "credit": Decimal("0.00")},
        ],
        [
            {"debit": Decimal("-1.00"), "credit": Decimal("0.00")},
            {"debit": Decimal("0.00"), "credit": Decimal("1.00")},
        ],
    ]
    for lines in invalid_sets:
        with pytest.raises(HTTPException):
            validate_balanced_lines(lines)


def test_journal_rounds_to_currency_precision() -> None:
    debit, credit = validate_balanced_lines([
        {"debit": Decimal("10.004"), "credit": Decimal("0")},
        {"debit": Decimal("0"), "credit": Decimal("10.00")},
    ])
    assert debit == Decimal("10.00")
    assert credit == Decimal("10.00")


async def create_test_tenant() -> tuple[UUID, str]:
    slug = "test-" + uuid4().hex
    async with SessionFactory() as session:
        tenant = Tenant(name="شركة اختبار", slug=slug, country_code="SA", currency_code="SAR")
        session.add(tenant)
        await session.commit()
        return tenant.id, slug


@pytest.mark.asyncio
async def test_sequence_is_unique_under_concurrency_and_resets_each_year() -> None:
    tenant_id, _ = await create_test_tenant()

    async def allocate(year: int) -> str:
        async with SessionFactory() as session:
            await set_tenant_context(session, str(tenant_id))
            async with session.begin():
                return await next_number(session, tenant_id, "JOURNAL", "JV", date(year, 1, 1))

    values = await asyncio.gather(*(allocate(2026) for _ in range(32)))
    assert len(values) == len(set(values)) == 32
    assert sorted(values) == [f"JV-2026-{number:06d}" for number in range(1, 33)]
    assert await allocate(2027) == "JV-2027-000001"


@pytest.mark.asyncio
async def test_period_dual_control_lock_audit_and_journal_reversal() -> None:
    tenant_id, _ = await create_test_tenant()
    async with SessionFactory() as session:
        await set_tenant_context(session, str(tenant_id))
        account_ids = await seed_chart_of_accounts(session, tenant_id)

        owner = User(
            email=f"owner-{uuid4().hex}@example.test",
            full_name="مالك الاختبار",
            password_hash=hash_password("Owner-Passphrase-2026!"),
            is_active=True,
        )
        cfo = User(
            email=f"cfo-{uuid4().hex}@example.test",
            full_name="مدير مالي الاختبار",
            password_hash=hash_password("CFO-Passphrase-2026!"),
            is_active=True,
        )
        session.add_all([owner, cfo])
        await session.flush()
        session.add_all([
            Membership(tenant_id=tenant_id, user_id=owner.id, role="Owner", is_active=True),
            Membership(tenant_id=tenant_id, user_id=cfo.id, role="CFO", is_active=True),
        ])
        await session.flush()

        period = await create_period(
            session, tenant_id, owner.id,
            FiscalPeriodCreate(
                code="FY2026-APR",
                name="الفترة التجريبية",
                start_date=date(2026, 4, 1),
                end_date=date(2026, 4, 30),
            ),
            "203.0.113.10",
        )

        with pytest.raises(HTTPException) as overlap:
            await create_period(
                session, tenant_id, owner.id,
                FiscalPeriodCreate(
                    code="FY2026-OVERLAP",
                    name="فترة متداخلة",
                    start_date=date(2026, 4, 20),
                    end_date=date(2026, 5, 20),
                ),
            )
        assert overlap.value.status_code == 409

        await close_period(
            session, tenant_id, owner.id, period.id,
            "إقفال نهاية الشهر",
            cfo.email, "CFO-Passphrase-2026!",
            "203.0.113.10",
        )

        with pytest.raises(HTTPException) as locked:
            await create_journal(
                session, tenant_id, owner.id, date(2026, 4, 15), "قيد داخل فترة مقفلة", "MANUAL",
                [
                    {"account_id": account_ids["1101"], "debit": Decimal("25.00"), "credit": Decimal("0.00")},
                    {"account_id": account_ids["3101"], "debit": Decimal("0.00"), "credit": Decimal("25.00")},
                ],
            )
        assert locked.value.status_code == 423

        await reopen_period(
            session, tenant_id, owner.id, period.id,
            "تصحيح بعد موافقة المدير المالي",
            cfo.email, "CFO-Passphrase-2026!",
            "203.0.113.10",
        )

        original = await create_journal(
            session, tenant_id, owner.id, date(2026, 4, 15), "قيد تجريبي صحيح", "MANUAL",
            [
                {"account_id": account_ids["1101"], "debit": Decimal("115.00"), "credit": Decimal("0.00")},
                {"account_id": account_ids["3101"], "debit": Decimal("0.00"), "credit": Decimal("115.00")},
            ],
        )

        with pytest.raises(HTTPException) as same_person:
            await reverse_journal(
                session, tenant_id, owner.id, original.id, date(2026, 4, 22),
                "سبب عكس القيد", owner.email, "Owner-Passphrase-2026!",
            )
        assert same_person.value.status_code == 422

        reversal = await reverse_journal(
            session, tenant_id, owner.id, original.id, date(2026, 4, 22),
            "عكس قيد غير مطلوب",
            cfo.email, "CFO-Passphrase-2026!",
            "203.0.113.10",
        )
        await session.flush()

        original_after = await session.scalar(
            select(JournalEntry).where(JournalEntry.tenant_id == tenant_id, JournalEntry.id == original.id)
        )
        assert original_after is not None and original_after.status == "REVERSED"
        assert reversal.reversal_of_id == original.id
        reversal_lines = (await session.scalars(
            select(JournalLine).where(JournalLine.tenant_id == tenant_id, JournalLine.entry_id == reversal.id)
        )).all()
        assert sum((line.debit for line in reversal_lines), Decimal("0")) == Decimal("115.00")
        assert sum((line.credit for line in reversal_lines), Decimal("0")) == Decimal("115.00")

        events = (await session.scalars(
            select(AuditLog).where(
                AuditLog.tenant_id == tenant_id,
                AuditLog.remote_address == "203.0.113.10",
            )
        )).all()
        actions = {event.action for event in events}
        assert {"FISCAL_PERIOD_CREATED", "FISCAL_PERIOD_CLOSED", "FISCAL_PERIOD_REOPENED", "JOURNAL_REVERSED"} <= actions


@pytest.mark.asyncio
async def test_journal_rejects_inactive_account() -> None:
    tenant_id, _ = await create_test_tenant()
    async with SessionFactory() as session:
        await set_tenant_context(session, str(tenant_id))
        account_ids = await seed_chart_of_accounts(session, tenant_id)
        from app.models import Account
        cash = await session.scalar(select(Account).where(Account.tenant_id == tenant_id, Account.id == account_ids["1101"]))
        assert cash is not None
        cash.is_active = False
        with pytest.raises(HTTPException) as captured:
            await create_journal(
                session, tenant_id, None, date(2026, 5, 1), "قيد لحساب موقوف", "MANUAL",
                [
                    {"account_id": account_ids["1101"], "debit": Decimal("10.00"), "credit": Decimal("0.00")},
                    {"account_id": account_ids["3101"], "debit": Decimal("0.00"), "credit": Decimal("10.00")},
                ],
            )
        assert captured.value.status_code == 422
