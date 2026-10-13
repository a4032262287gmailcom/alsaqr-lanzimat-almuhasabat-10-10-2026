from uuid import UUID

from fastapi import HTTPException
from sqlalchemy import func, select
from sqlalchemy.ext.asyncio import AsyncSession

from app.core.security import verify_password
from app.models import Membership, User

CONTROL_ROLES = ("Owner", "Admin", "CFO")


async def validate_second_approver(
    session: AsyncSession,
    tenant_id: UUID,
    actor_user_id: UUID,
    approver_email: str,
    approver_password: str,
) -> User:
    normalized_email = approver_email.strip().lower()
    approver = await session.scalar(
        select(User).where(
            func.lower(User.email) == normalized_email,
            User.is_active.is_(True),
        )
    )
    if approver is None:
        raise HTTPException(status_code=403, detail="Second approver credentials are invalid")
    if approver.id == actor_user_id:
        raise HTTPException(status_code=422, detail="A different user must confirm this sensitive operation")
    membership = await session.scalar(
        select(Membership).where(
            Membership.tenant_id == tenant_id,
            Membership.user_id == approver.id,
            Membership.is_active.is_(True),
            Membership.role.in_(CONTROL_ROLES),
        )
    )
    if membership is None or not verify_password(approver_password, approver.password_hash):
        raise HTTPException(status_code=403, detail="Second approver must be an active Owner, Admin, or CFO with valid credentials")
    return approver
