from datetime import date, datetime, timezone
from decimal import Decimal, ROUND_HALF_UP
from typing import Any
from uuid import UUID

from fastapi import HTTPException
from sqlalchemy import select
from sqlalchemy.dialects.postgresql import insert as pg_insert
from sqlalchemy.ext.asyncio import AsyncSession

from app.models import Account, AuditLog, FiscalPeriod, JournalEntry, JournalLine, NumberSequence

CENT = Decimal("0.01")
DEFAULT_ACCOUNTS = {
    "cash": "1101",
    "bank": "1102",
    "receivable": "1103",
    "inventory": "1201",
    "input_vat": "1301",
    "payable": "2101",
    "output_vat": "2201",
    "capital": "3101",
    "retained_earnings": "3201",
    "revenue": "4101",
    "cogs": "5101",
    "expense": "5201",
}


def money(value: Decimal | int | float | str) -> Decimal:
    return Decimal(str(value)).quantize(CENT, rounding=ROUND_HALF_UP)


def validate_balanced_lines(lines: list[dict[str, Any]]) -> tuple[Decimal, Decimal]:
    if len(lines) < 2:
        raise HTTPException(status_code=422, detail="A journal entry requires at least two lines")
    total_debit = Decimal("0.00")
    total_credit = Decimal("0.00")
    for index, line in enumerate(lines, start=1):
        debit = money(line.get("debit", 0))
        credit = money(line.get("credit", 0))
        if debit < 0 or credit < 0:
            raise HTTPException(status_code=422, detail=f"Journal line {index}: debit and credit cannot be negative")
        if (debit > 0) == (credit > 0):
            raise HTTPException(status_code=422, detail=f"Journal line {index}: set exactly one of debit or credit to a positive amount")
        total_debit += debit
        total_credit += credit
    total_debit, total_credit = money(total_debit), money(total_credit)
    if total_debit <= 0 or total_debit != total_credit:
        raise HTTPException(status_code=422, detail={"message": "Journal is not balanced; transaction will be rolled back", "total_debit": str(total_debit), "total_credit": str(total_credit)})
    return total_debit, total_credit


async def get_account_by_code(session: AsyncSession, tenant_id: UUID, code: str) -> Account:
    account = await session.scalar(
        select(Account).where(Account.tenant_id == tenant_id, Account.code == code, Account.is_active.is_(True))
    )
    if account is None:
        raise HTTPException(status_code=500, detail=f"Required system account {code} is missing or inactive")
    return account


async def next_number(session: AsyncSession, tenant_id: UUID, sequence_key: str, prefix: str, on_date: date) -> str:
    # The year is part of the sequence key, so numbering restarts each financial calendar year.
    scoped_key = f"{sequence_key}:{on_date.year}"
    insert_stmt = pg_insert(NumberSequence).values(
        tenant_id=tenant_id,
        sequence_key=scoped_key,
        next_value=1,
    ).on_conflict_do_nothing(
        index_elements=[NumberSequence.tenant_id, NumberSequence.sequence_key]
    )
    await session.execute(insert_stmt)
    sequence = await session.scalar(
        select(NumberSequence)
        .where(NumberSequence.tenant_id == tenant_id, NumberSequence.sequence_key == scoped_key)
        .with_for_update()
    )
    if sequence is None:
        raise RuntimeError("Document sequence could not be locked after initialization")
    value = sequence.next_value
    sequence.next_value += 1
    await session.flush()
    return f"{prefix}-{on_date.year}-{value:06d}"


async def assert_period_open(session: AsyncSession, tenant_id: UUID, posting_date: date) -> None:
    period = await session.scalar(
        select(FiscalPeriod)
        .where(
            FiscalPeriod.tenant_id == tenant_id,
            FiscalPeriod.start_date <= posting_date,
            FiscalPeriod.end_date >= posting_date,
        )
        .order_by(FiscalPeriod.start_date.desc())
        .with_for_update()
    )
    if period is not None and period.status == "CLOSED":
        raise HTTPException(
            status_code=423,
            detail={
                "message": "Posting is prohibited because the fiscal period is closed",
                "period_code": period.code,
                "start_date": period.start_date.isoformat(),
                "end_date": period.end_date.isoformat(),
            },
        )


async def create_journal(
    session: AsyncSession,
    tenant_id: UUID,
    user_id: UUID | None,
    entry_date: date,
    memo: str,
    source_type: str,
    lines: list[dict[str, Any]],
    source_id: UUID | None = None,
    reversal_of_id: UUID | None = None,
) -> JournalEntry:
    if not memo.strip():
        raise HTTPException(status_code=422, detail="Journal memo is required")
    await assert_period_open(session, tenant_id, entry_date)
    validate_balanced_lines(lines)
    for index, line in enumerate(lines, start=1):
        account = await session.scalar(
            select(Account).where(
                Account.tenant_id == tenant_id,
                Account.id == line["account_id"],
                Account.is_active.is_(True),
                Account.allow_posting.is_(True),
            )
        )
        if account is None:
            raise HTTPException(status_code=422, detail=f"Journal line {index}: account is missing, inactive, or cannot receive postings")
    number = await next_number(session, tenant_id, "JOURNAL", "JV", entry_date)
    entry = JournalEntry(
        tenant_id=tenant_id,
        number=number,
        entry_date=entry_date,
        memo=memo.strip(),
        status="POSTED",
        source_type=source_type,
        source_id=source_id,
        reversal_of_id=reversal_of_id,
        posted_by=user_id,
        posted_at=datetime.now(timezone.utc),
    )
    session.add(entry)
    await session.flush()
    for line in lines:
        session.add(JournalLine(
            tenant_id=tenant_id,
            entry_id=entry.id,
            account_id=line["account_id"],
            contact_id=line.get("contact_id"),
            cost_center_id=line.get("cost_center_id"),
            description=line.get("description"),
            debit=money(line.get("debit", 0)),
            credit=money(line.get("credit", 0)),
            currency_code=line.get("currency_code", "SAR"),
        ))
    await session.flush()
    return entry


async def audit(
    session: AsyncSession,
    tenant_id: UUID,
    actor_user_id: UUID | None,
    action: str,
    entity_type: str,
    entity_id: UUID | None,
    details: dict[str, Any] | None = None,
    remote_address: str | None = None,
) -> None:
    session.add(AuditLog(
        tenant_id=tenant_id,
        actor_user_id=actor_user_id,
        action=action,
        entity_type=entity_type,
        entity_id=entity_id,
        details=details or {},
        remote_address=remote_address,
    ))
    await session.flush()


async def seed_chart_of_accounts(session: AsyncSession, tenant_id: UUID) -> dict[str, UUID]:
    if await session.scalar(select(Account.id).where(Account.tenant_id == tenant_id).limit(1)):
        return {
            code: account_id
            for code, account_id in (
                (account.code, account.id)
                for account in (await session.scalars(select(Account).where(Account.tenant_id == tenant_id))).all()
            )
        }

    chart = [
        ("1", "الأصول", None, "BALANCE_SHEET", "DEBIT", "ASSET", False),
        ("11", "الأصول المتداولة", "1", "BALANCE_SHEET", "DEBIT", "ASSET", False),
        ("1101", "الصندوق", "11", "BALANCE_SHEET", "DEBIT", "ASSET", True),
        ("1102", "البنوك", "11", "BALANCE_SHEET", "DEBIT", "ASSET", True),
        ("1103", "ذمم العملاء", "11", "BALANCE_SHEET", "DEBIT", "ASSET", True),
        ("12", "المخزون", "1", "BALANCE_SHEET", "DEBIT", "ASSET", False),
        ("1201", "مخزون البضائع", "12", "BALANCE_SHEET", "DEBIT", "ASSET", True),
        ("13", "ضرائب مدينة", "1", "BALANCE_SHEET", "DEBIT", "ASSET", False),
        ("1301", "ضريبة القيمة المضافة المدخلات", "13", "BALANCE_SHEET", "DEBIT", "ASSET", True),
        ("2", "الالتزامات", None, "BALANCE_SHEET", "CREDIT", "LIABILITY", False),
        ("21", "الالتزامات المتداولة", "2", "BALANCE_SHEET", "CREDIT", "LIABILITY", False),
        ("2101", "ذمم الموردين", "21", "BALANCE_SHEET", "CREDIT", "LIABILITY", True),
        ("22", "الضرائب المستحقة", "2", "BALANCE_SHEET", "CREDIT", "LIABILITY", False),
        ("2201", "ضريبة القيمة المضافة المخرجات", "22", "BALANCE_SHEET", "CREDIT", "LIABILITY", True),
        ("3", "حقوق الملكية", None, "BALANCE_SHEET", "CREDIT", "EQUITY", False),
        ("31", "رأس المال", "3", "BALANCE_SHEET", "CREDIT", "EQUITY", False),
        ("3101", "رأس المال المدفوع", "31", "BALANCE_SHEET", "CREDIT", "EQUITY", True),
        ("32", "الأرباح المبقاة", "3", "BALANCE_SHEET", "CREDIT", "EQUITY", False),
        ("3201", "الأرباح المبقاة", "32", "BALANCE_SHEET", "CREDIT", "EQUITY", True),
        ("4", "الإيرادات", None, "INCOME_STATEMENT", "CREDIT", "REVENUE", False),
        ("41", "إيرادات التشغيل", "4", "INCOME_STATEMENT", "CREDIT", "REVENUE", False),
        ("4101", "إيرادات المبيعات", "41", "INCOME_STATEMENT", "CREDIT", "REVENUE", True),
        ("5", "المصروفات", None, "INCOME_STATEMENT", "DEBIT", "EXPENSE", False),
        ("51", "تكلفة المبيعات", "5", "INCOME_STATEMENT", "DEBIT", "EXPENSE", False),
        ("5101", "تكلفة البضاعة المباعة", "51", "INCOME_STATEMENT", "DEBIT", "EXPENSE", True),
        ("52", "المصروفات التشغيلية", "5", "INCOME_STATEMENT", "DEBIT", "EXPENSE", False),
        ("5201", "مصروفات تشغيلية", "52", "INCOME_STATEMENT", "DEBIT", "EXPENSE", True),
    ]
    by_code: dict[str, Account] = {}
    for code, name, parent_code, statement_type, nature, category, allow_posting in chart:
        parent_id = by_code[parent_code].id if parent_code else None
        account = Account(
            tenant_id=tenant_id,
            code=code,
            name=name,
            parent_id=parent_id,
            statement_type=statement_type,
            nature=nature,
            account_category=category,
            allow_posting=allow_posting,
            is_system=True,
            is_active=True,
        )
        session.add(account)
        await session.flush()
        by_code[code] = account
    return {code: account.id for code, account in by_code.items()}


def next_child_code(parent_code: str, existing_codes: list[str]) -> str:
    width = 1 if len(parent_code) == 1 else 2
    suffixes: list[int] = []
    for code in existing_codes:
        if code.startswith(parent_code) and len(code) == len(parent_code) + width:
            suffix = code[len(parent_code):]
            if suffix.isdigit():
                suffixes.append(int(suffix))
    next_suffix = (max(suffixes) if suffixes else 0) + 1
    limit = 10 ** width
    if next_suffix >= limit:
        raise HTTPException(status_code=409, detail="No automatic child code remains under this account; choose a code explicitly")
    return parent_code + f"{next_suffix:0{width}d}"

async def reverse_journal(
    session: AsyncSession,
    tenant_id: UUID,
    user_id: UUID,
    journal_id: UUID,
    reversal_date: date,
    reason: str,
    second_approver_email: str,
    second_approver_password: str,
    remote_address: str | None = None,
) -> JournalEntry:
    from app.services.approvals import validate_second_approver

    original = await session.scalar(
        select(JournalEntry)
        .where(JournalEntry.tenant_id == tenant_id, JournalEntry.id == journal_id)
        .with_for_update()
    )
    if original is None:
        raise HTTPException(status_code=404, detail="Journal entry not found")
    if original.status != "POSTED":
        raise HTTPException(status_code=409, detail="Only a posted journal can be reversed")
    prior_reversal = await session.scalar(
        select(JournalEntry.id).where(
            JournalEntry.tenant_id == tenant_id,
            JournalEntry.reversal_of_id == original.id,
        ).limit(1)
    )
    if prior_reversal is not None:
        raise HTTPException(status_code=409, detail="This journal already has a reversal entry")

    second_approver = await validate_second_approver(
        session, tenant_id, user_id, second_approver_email, second_approver_password
    )
    original_lines = (await session.scalars(
        select(JournalLine).where(
            JournalLine.tenant_id == tenant_id,
            JournalLine.entry_id == original.id,
        ).order_by(JournalLine.created_at, JournalLine.id)
    )).all()
    reversed_lines = [
        {
            "account_id": line.account_id,
            "contact_id": line.contact_id,
            "cost_center_id": line.cost_center_id,
            "description": f"عكس: {line.description or original.memo}",
            "debit": line.credit,
            "credit": line.debit,
            "currency_code": line.currency_code,
        }
        for line in original_lines
    ]
    reversal = await create_journal(
        session=session,
        tenant_id=tenant_id,
        user_id=user_id,
        entry_date=reversal_date,
        memo=f"عكس القيد {original.number}: {reason.strip()}",
        source_type="REVERSAL",
        lines=reversed_lines,
        source_id=original.id,
        reversal_of_id=original.id,
    )
    original.status = "REVERSED"
    await session.flush()
    await audit(
        session, tenant_id, user_id, "JOURNAL_REVERSED", "JournalEntry", original.id,
        {
            "original_number": original.number,
            "reversal_number": reversal.number,
            "reversal_date": reversal_date.isoformat(),
            "reason": reason.strip(),
            "second_approver_user_id": str(second_approver.id),
        },
        remote_address,
    )
    return reversal

