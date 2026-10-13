import re
from datetime import datetime, timezone
from uuid import UUID

from fastapi import APIRouter, Depends, HTTPException, status
from sqlalchemy import func, select
from sqlalchemy.exc import IntegrityError
from sqlalchemy.ext.asyncio import AsyncSession

from app.core.config import get_settings
from app.core.security import create_access_token, hash_password, verify_password
from app.database import get_db, set_tenant_context
from app.dependencies import get_current_user
from app.models import Membership, NumberSequence, Tenant, User, Warehouse
from app.schemas import AuthResponse, LoginRequest, MembershipOut, RegisterRequest, UserOut
from app.services.accounting import seed_chart_of_accounts

router = APIRouter(prefix="/auth", tags=["Authentication"])


def make_slug(value: str) -> str:
    slug = re.sub(r"[^a-z0-9]+", "-", value.lower()).strip("-")
    slug = slug[:90].strip("-")
    return slug or "company"


async def membership_payloads(session: AsyncSession, user_id: UUID) -> list[MembershipOut]:
    rows = (await session.execute(
        select(Membership, Tenant)
        .join(Tenant, Tenant.id == Membership.tenant_id)
        .where(
            Membership.user_id == user_id,
            Membership.is_active.is_(True),
            Tenant.is_active.is_(True),
        )
        .order_by(Tenant.name)
    )).all()
    return [
        MembershipOut(
            tenant_id=membership.tenant_id,
            company_name=tenant.name,
            company_slug=tenant.slug,
            role=membership.role,
        )
        for membership, tenant in rows
    ]


def auth_response(user: User, memberships: list[MembershipOut]) -> AuthResponse:
    settings = get_settings()
    return AuthResponse(
        access_token=create_access_token(user.id),
        expires_in_seconds=settings.jwt_access_token_minutes * 60,
        user=UserOut.model_validate(user),
        memberships=memberships,
    )


@router.post("/register", response_model=AuthResponse, status_code=status.HTTP_201_CREATED)
async def register(payload: RegisterRequest, session: AsyncSession = Depends(get_db)) -> AuthResponse:
    normalized_email = str(payload.email).lower()
    exists = await session.scalar(select(User.id).where(func.lower(User.email) == normalized_email))
    if exists is not None:
        raise HTTPException(status_code=409, detail="An account with this email already exists")

    requested_slug = payload.company_slug or make_slug(payload.company_name)
    slug = requested_slug
    suffix = 2
    while await session.scalar(select(Tenant.id).where(Tenant.slug == slug)) is not None:
        if payload.company_slug:
            raise HTTPException(status_code=409, detail="Company slug is already taken")
        slug = f"{requested_slug[:88]}-{suffix}"
        suffix += 1
        if suffix > 100:
            raise HTTPException(status_code=409, detail="Could not allocate a unique company slug")

    user = User(
        email=normalized_email,
        full_name=payload.full_name.strip(),
        password_hash=hash_password(payload.password),
        is_active=True,
    )
    tenant = Tenant(
        name=payload.company_name.strip(),
        slug=slug,
        legal_name=payload.legal_name,
        tax_number=payload.tax_number,
        country_code="SA",
        currency_code="SAR",
        timezone="Asia/Riyadh",
        is_active=True,
    )
    session.add_all([user, tenant])
    try:
        await session.flush()
    except IntegrityError as exc:
        raise HTTPException(status_code=409, detail="Email or company slug is already in use") from exc

    membership = Membership(tenant_id=tenant.id, user_id=user.id, role="Owner", is_active=True)
    session.add(membership)
    await session.flush()
    await set_tenant_context(session, str(tenant.id))

    await seed_chart_of_accounts(session, tenant.id)
    session.add(Warehouse(tenant_id=tenant.id, code="MAIN", name="المستودع الرئيسي", is_default=True, is_active=True))
    for key in ("JOURNAL", "SALE", "PURCHASE"):
        session.add(NumberSequence(tenant_id=tenant.id, sequence_key=key, next_value=1))
    await session.flush()
    response = auth_response(user, [
        MembershipOut(tenant_id=tenant.id, company_name=tenant.name, company_slug=tenant.slug, role="Owner")
    ])
    return response


@router.post("/login", response_model=AuthResponse)
async def login(payload: LoginRequest, session: AsyncSession = Depends(get_db)) -> AuthResponse:
    normalized_email = str(payload.email).lower()
    user = await session.scalar(select(User).where(func.lower(User.email) == normalized_email))
    if user is None or not user.is_active or not verify_password(payload.password, user.password_hash):
        raise HTTPException(status_code=401, detail="Invalid email or password", headers={"WWW-Authenticate": "Bearer"})
    user.last_login_at = datetime.now(timezone.utc)
    await session.flush()
    memberships = await membership_payloads(session, user.id)
    if not memberships:
        raise HTTPException(status_code=403, detail="No active company memberships are available for this user")
    return auth_response(user, memberships)


@router.get("/me", response_model=UserOut)
async def me(user: User = Depends(get_current_user)) -> UserOut:
    return UserOut.model_validate(user)


@router.get("/companies/mine", response_model=list[MembershipOut])
async def my_companies(
    user: User = Depends(get_current_user),
    session: AsyncSession = Depends(get_db),
) -> list[MembershipOut]:
    return await membership_payloads(session, user.id)
