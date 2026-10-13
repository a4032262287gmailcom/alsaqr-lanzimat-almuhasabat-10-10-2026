from datetime import datetime, timedelta, timezone
from uuid import UUID

import jwt
from pwdlib import PasswordHash

from app.core.config import settings

_password_hash = PasswordHash.recommended()
_ALGORITHM = "HS256"


def hash_password(password: str) -> str:
    if len(password) < 12:
        raise ValueError("Password must be at least 12 characters.")
    return _password_hash.hash(password)


def verify_password(password: str, password_hash: str) -> bool:
    try:
        return _password_hash.verify(password, password_hash)
    except (ValueError, TypeError):
        return False


def create_access_token(user_id: UUID) -> str:
    now = datetime.now(timezone.utc)
    claims = {
        "sub": str(user_id),
        "iat": now,
        "exp": now + timedelta(minutes=settings.jwt_access_token_minutes),
        "type": "access",
    }
    return jwt.encode(claims, settings.jwt_secret_key, algorithm=_ALGORITHM)


def decode_access_token(token: str) -> UUID:
    try:
        claims = jwt.decode(
            token,
            settings.jwt_secret_key,
            algorithms=[_ALGORITHM],
            options={"require": ["sub", "iat", "exp", "type"]},
        )
        if claims.get("type") != "access":
            raise jwt.InvalidTokenError("Wrong token type.")
        return UUID(claims["sub"])
    except (jwt.PyJWTError, KeyError, ValueError) as exc:
        raise ValueError("Invalid or expired access token.") from exc
