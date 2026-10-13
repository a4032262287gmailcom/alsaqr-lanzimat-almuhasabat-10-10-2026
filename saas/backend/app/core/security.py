from datetime import datetime, timedelta, timezone
from uuid import UUID

import jwt
from pwdlib import PasswordHash

from app.core.config import get_settings

password_hash = PasswordHash.recommended()
ALGORITHM = "HS256"


def hash_password(password: str) -> str:
    if len(password) < 12:
        raise ValueError("Password must be at least 12 characters")
    return password_hash.hash(password)


def verify_password(password: str, encoded_hash: str) -> bool:
    try:
        return password_hash.verify(password, encoded_hash)
    except Exception:
        return False


def create_access_token(user_id: UUID) -> str:
    settings = get_settings()
    now = datetime.now(timezone.utc)
    payload = {
        "sub": str(user_id),
        "iat": now,
        "exp": now + timedelta(minutes=settings.jwt_access_token_minutes),
        "iss": settings.app_name,
        "aud": "alsaqr-cloud-api",
    }
    return jwt.encode(payload, settings.jwt_secret_key, algorithm=ALGORITHM)


def decode_access_token(token: str) -> UUID:
    settings = get_settings()
    payload = jwt.decode(
        token,
        settings.jwt_secret_key,
        algorithms=[ALGORITHM],
        audience="alsaqr-cloud-api",
        issuer=settings.app_name,
        options={"require": ["sub", "iat", "exp", "iss", "aud"]},
    )
    return UUID(payload["sub"])
