import asyncio
from datetime import date
from decimal import Decimal
from uuid import UUID, uuid4

import pytest
from fastapi import HTTPException
from sqlalchemy import select, update
from sqlalchemy.exc import DBAPIError

from app.core.security import hash_password
from app.database import SessionFactory, set_tenant_context
from app.models import (
    Account,
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
    next_child_code,
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


def test_account_auto_coding_supports_nested_segments() -> None:
    assert next_child_code("1", ["1", "11", "12"]) == "13"
    assert next_child_code("11", ["11", "1101", "1102"]) == "1103"
    assert next_child_code("1101", ["110101", "110102"]) == "110103"
    with pytest.raises(HTTPException) as captured:
        next_child_code("1", ["1", *[f"1{digit}" for digit in range(1, 10)]])
    assert captured.value.status_code == 409


async def create_test_tenant() -> tuple[UUID, str]:
    slug = "test-" + uuid4().hex
    async with SessionFactory() as session:
        tenant = Tenant(name="شركة اختبار", slug=slug, country_code="SA", currency_code="SAR")
        session.add(tenant)
        await session.commit()
        return tenant.id, slug


@pytest.mark.asyncio(loop_scope="session")
async def test_sequence_is_unique_under_concurrency_and_resets_each_year() -> None:
    tenant_id, _ = await create_test_tenant()

    async def allocate(year: int) -> str:
        async with SessionFactory() as session:
            await set_tenant_context(session, str(tenant_id))
            number = await next_number(session, tenant_id, "JOURNAL", "JV", date(year, 1, 1))
            await session.commit()
            return number

    values = await asyncio.gather(*(allocate(2026) for _ in range(32)))
    assert len(values) == len(set(values)) == 32
    assert sorted(values) == [f"JV-2026-{number:06d}" for number in range(1, 33)]
    assert await allocate(2027) == "JV-2027-000001"


@pytest.mark.asyncio(loop_scope="session")
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
        close_event = next(event for event in events if event.action == "FISCAL_PERIOD_CLOSED")
        assert close_event.before_data["status"] == "OPEN"
        assert close_event.after_data["status"] == "CLOSED"


@pytest.mark.asyncio(loop_scope="session")
async def test_journal_requires_an_open_period_covering_the_posting_date() -> None:
    tenant_id, _ = await create_test_tenant()
    async with SessionFactory() as session:
        await set_tenant_context(session, str(tenant_id))
        account_ids = await seed_chart_of_accounts(session, tenant_id)
        with pytest.raises(HTTPException) as captured:
            await create_journal(
                session, tenant_id, None, date(2026, 5, 1), "قيد بلا فترة محاسبية", "MANUAL",
                [
                    {"account_id": account_ids["1101"], "debit": Decimal("10.00"), "credit": Decimal("0.00")},
                    {"account_id": account_ids["3101"], "debit": Decimal("0.00"), "credit": Decimal("10.00")},
                ],
            )
        assert captured.value.status_code == 422
        assert captured.value.detail["posting_date"] == "2026-05-01"


@pytest.mark.asyncio(loop_scope="session")
async def test_journal_rejects_inactive_account() -> None:
    tenant_id, _ = await create_test_tenant()
    async with SessionFactory() as session:
        await set_tenant_context(session, str(tenant_id))
        account_ids = await seed_chart_of_accounts(session, tenant_id)
        session.add(FiscalPeriod(
            tenant_id=tenant_id,
            code="FY2026",
            name="السنة المالية 2026",
            start_date=date(2026, 1, 1),
            end_date=date(2026, 12, 31),
            status="OPEN",
        ))
        await session.flush()
        cash = await session.scalar(select(Account).where(Account.tenant_id == tenant_id, Account.id == account_ids["1101"]))
        assert cash is not None
        cash.is_active = False
        await session.flush()
        with pytest.raises(HTTPException) as captured:
            await create_journal(
                session, tenant_id, None, date(2026, 5, 1), "قيد لحساب موقوف", "MANUAL",
                [
                    {"account_id": account_ids["1101"], "debit": Decimal("10.00"), "credit": Decimal("0.00")},
                    {"account_id": account_ids["3101"], "debit": Decimal("0.00"), "credit": Decimal("10.00")},
                ],
            )
        assert captured.value.status_code == 422

@pytest.mark.asyncio(loop_scope="session")
async def test_postgres_rls_blocks_cross_tenant_reads_without_application_filter() -> None:
    tenant_a, _ = await create_test_tenant()
    tenant_b, _ = await create_test_tenant()

    async with SessionFactory() as session:
        visible_without_context = await session.scalar(select(Account.id).limit(1))
        assert visible_without_context is None

    async with SessionFactory() as session:
        await set_tenant_context(session, str(tenant_b))
        await seed_chart_of_accounts(session, tenant_b)
        await session.commit()

    async with SessionFactory() as session:
        await set_tenant_context(session, str(tenant_a))
        leaked_account = await session.scalar(select(Account).where(Account.code == "1101").limit(1))
        assert leaked_account is None

    async with SessionFactory() as session:
        await set_tenant_context(session, str(tenant_b))
        visible_account = await session.scalar(select(Account).where(Account.code == "1101").limit(1))
        assert visible_account is not None
        assert visible_account.tenant_id == tenant_b

@pytest.mark.asyncio(loop_scope="session")
async def test_database_blocks_mutation_of_posted_journal() -> None:
    tenant_id, _ = await create_test_tenant()
    async with SessionFactory() as session:
        await set_tenant_context(session, str(tenant_id))
        account_ids = await seed_chart_of_accounts(session, tenant_id)
        session.add(FiscalPeriod(
            tenant_id=tenant_id,
            code="FY2026",
            name="السنة المالية 2026",
            start_date=date(2026, 1, 1),
            end_date=date(2026, 12, 31),
            status="OPEN",
        ))
        await session.flush()
        journal = await create_journal(
            session, tenant_id, None, date(2026, 7, 1), "قيد غير قابل للتعديل", "MANUAL",
            [
                {"account_id": account_ids["1101"], "debit": Decimal("75.00"), "credit": Decimal("0.00")},
                {"account_id": account_ids["3101"], "debit": Decimal("0.00"), "credit": Decimal("75.00")},
            ],
        )
        await session.commit()

        await set_tenant_context(session, str(tenant_id))
        with pytest.raises(DBAPIError):
            await session.execute(
                update(JournalEntry)
                .where(JournalEntry.tenant_id == tenant_id, JournalEntry.id == journal.id)
                .values(memo="تعديل غير مسموح")
            )
        await session.rollback()


@pytest.mark.asyncio(loop_scope="session")
async def test_database_rejects_unbalanced_posted_journal_at_commit() -> None:
    tenant_id, _ = await create_test_tenant()
    async with SessionFactory() as session:
        await set_tenant_context(session, str(tenant_id))
        account_ids = await seed_chart_of_accounts(session, tenant_id)
        journal = JournalEntry(
            tenant_id=tenant_id,
            number="BAD-" + uuid4().hex[:12],
            entry_date=date(2026, 8, 1),
            memo="اختبار تجاوز محرك الترحيل",
            status="POSTED",
            source_type="MANUAL",
        )
        session.add(journal)
        await session.flush()
        session.add_all([
            JournalLine(
                tenant_id=tenant_id, entry_id=journal.id, account_id=account_ids["1101"],
                debit=Decimal("100.00"), credit=Decimal("0.00"), currency_code="SAR",
            ),
            JournalLine(
                tenant_id=tenant_id, entry_id=journal.id, account_id=account_ids["3101"],
                debit=Decimal("0.00"), credit=Decimal("99.00"), currency_code="SAR",
            ),
        ])
        with pytest.raises(DBAPIError):
            await session.commit()
        await session.rollback()


@pytest.mark.asyncio(loop_scope="session")
async def test_audit_logs_are_append_only_in_postgresql() -> None:
    tenant_id, _ = await create_test_tenant()
    async with SessionFactory() as session:
        await set_tenant_context(session, str(tenant_id))
        event = AuditLog(
            tenant_id=tenant_id,
            actor_user_id=None,
            action="TEST_EVENT",
            entity_type="Test",
            details={"original": True},
            before_data={"status": "DRAFT"},
            after_data={"status": "POSTED", "amount": "125.00"},
            user_agent="pytest-agent",
            correlation_id="test-correlation-123",
        )
        session.add(event)
        await session.commit()

        saved_event = await session.scalar(
            select(AuditLog).where(AuditLog.tenant_id == tenant_id, AuditLog.id == event.id)
        )
        assert saved_event is not None
        assert saved_event.before_data == {"status": "DRAFT"}
        assert saved_event.after_data == {"status": "POSTED", "amount": "125.00"}
        assert saved_event.user_agent == "pytest-agent"
        assert saved_event.correlation_id == "test-correlation-123"

        await set_tenant_context(session, str(tenant_id))
        with pytest.raises(DBAPIError):
            await session.execute(
                update(AuditLog)
                .where(AuditLog.tenant_id == tenant_id, AuditLog.id == event.id)
                .values(details={"tampered": True})
            )
        await session.rollback()

