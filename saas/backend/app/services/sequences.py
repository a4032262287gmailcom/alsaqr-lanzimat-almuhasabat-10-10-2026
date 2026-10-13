from datetime import date
from uuid import UUID

from sqlalchemy.dialects.postgresql import insert
from sqlalchemy.ext.asyncio import AsyncSession
from sqlalchemy import func

from app.models.accounting import SequenceCounter


class SequenceService:
    @staticmethod
    async def next_number(
        session: AsyncSession,
        *,
        tenant_id: UUID,
        document_type: str,
        prefix: str,
        document_date: date,
        scope_key: str = "GLOBAL",
        width: int = 6,
    ) -> str:
        if not document_type or len(document_type) > 50:
            raise ValueError("Document type must be between 1 and 50 characters.")
        if not prefix or len(prefix) > 20:
            raise ValueError("Sequence prefix must be between 1 and 20 characters.")
        if width < 1 or width > 18:
            raise ValueError("Sequence width must be between 1 and 18.")
        if not scope_key or len(scope_key) > 80:
            raise ValueError("Sequence scope must be between 1 and 80 characters.")

        # A single upsert serializes concurrent increments on PostgreSQL's unique key.
        statement = insert(SequenceCounter).values(
            tenant_id=tenant_id,
            scope_key=scope_key,
            document_type=document_type,
            fiscal_year=document_date.year,
            prefix=prefix,
            next_value=2,
            width=width,
            created_at=func.now(),
            updated_at=func.now(),
        ).on_conflict_do_update(
            constraint="uq_sequence_scope_type_year",
            set_={"next_value": SequenceCounter.next_value + 1, "updated_at": func.now()},
        ).returning(SequenceCounter.next_value, SequenceCounter.prefix, SequenceCounter.width)

        allocated_next, stored_prefix, stored_width = (await session.execute(statement)).one()
        number = int(allocated_next) - 1
        branch_fragment = "" if scope_key == "GLOBAL" else f"-{scope_key}"
        return f"{stored_prefix}{branch_fragment}-{document_date.year}-{number:0{int(stored_width)}d}"
