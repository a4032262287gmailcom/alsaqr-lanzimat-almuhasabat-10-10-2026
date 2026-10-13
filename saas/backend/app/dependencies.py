from dataclasses import dataclass
from typing import Callable

from fastapi import Request
from uuid import UUID

from fastapi import Depends, Header, HTTPException, status
from fastapi.security import HTTPAuthorizationCredentials, HTTPBearer
from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from app.database import get_db, set_tenant_context
from app.models import Membership, Tenant, User
from app.core.permissions import has_permission
from app.core.security import decode_access_token

bearer_scheme = HTTPBearer(auto_error=False)


@dataclass(frozen=True)
class TenantContext:
    tenant_id: UUID
    user_id: UUID
    role: str
    user: User


async def get_current_user(
    credentials: HTTPAuthorizationCredentials | None = Depends(bearer_scheme),
    session: AsyncSession = Depends(get_db),
) -> User:
    if credentials is None or credentials.scheme.lower() != "bearer":
        raise HTTPException(status_code=status.HTTP_401_UNAUTHORIZED, detail="Authentication required", headers={"WWW-Authenticate": "Bearer"})
    try:
        user_id = decode_access_token(credentials.credentials)
    except Exception:
        raise HTTPException(status_code=status.HTTP_401_UNAUTHORIZED, detail="Invalid or expired access token", headers={"WWW-Authenticate": "Bearer"})
    user = await session.scalar(select(User).where(User.id == user_id, User.is_active.is_(True)))
    if user is None:
        raise HTTPException(status_code=status.HTTP_401_UNAUTHORIZED, detail="User is inactive or no longer exists")
    return user


async def get_tenant_context(
    request: Request,
    x_company_id: UUID = Header(..., alias="X-Company-ID"),
    user: User = Depends(get_current_user),
    session: AsyncSession = Depends(get_db),
) -> TenantContext:
    membership = await session.scalar(
        select(Membership).where(
            Membership.tenant_id == x_company_id,
            Membership.user_id == user.id,
            Membership.is_active.is_(True),
        )
    )
    if membership is None:
        raise HTTPException(status_code=status.HTTP_403_FORBIDDEN, detail="You do not have an active membership in this company")
    tenant = await session.scalar(select(Tenant).where(Tenant.id == x_company_id, Tenant.is_active.is_(True)))
    if tenant is None:
        raise HTTPException(status_code=status.HTTP_404_NOT_FOUND, detail="Company is inactive or does not exist")
    await set_tenant_context(session, str(tenant.id))
    session.info["request_audit_context"] = {
        "remote_address": request.client.host if request.client else None,
        "user_agent": request.headers.get("user-agent", "")[:500] or None,
        "correlation_id": getattr(request.state, "correlation_id", None),
    }
    return TenantContext(tenant_id=tenant.id, user_id=user.id, role=membership.role, user=user)


def require_permission(permission_code: str) -> Callable:
    async def dependency(
        context: TenantContext = Depends(get_tenant_context),
        session: AsyncSession = Depends(get_db),
    ) -> TenantContext:
        allowed = await has_permission(
            session, context.tenant_id, context.role, permission_code
        )
        if not allowed:
            raise HTTPException(
                status_code=status.HTTP_403_FORBIDDEN,
                detail={
                    "message": "Your company role does not grant this permission",
                    "permission": permission_code,
                },
            )
        return context
    return dependency


def require_roles(*allowed_roles: str) -> Callable:
    async def dependency(context: TenantContext = Depends(get_tenant_context)) -> TenantContext:
        if context.role not in allowed_roles:
            raise HTTPException(status_code=status.HTTP_403_FORBIDDEN, detail="Your role cannot perform this operation")
        return context
    return dependency


admin_or_accountant = require_roles("Owner", "Admin", "CFO", "Accountant")
admin_only = require_roles("Owner", "Admin")
can_sell = require_roles("Owner", "Admin", "CFO", "Accountant", "Cashier", "Sales", "FieldAgent")
