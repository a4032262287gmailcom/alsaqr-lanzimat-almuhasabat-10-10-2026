import json
from typing import Any
from uuid import UUID

from sqlalchemy.ext.asyncio import AsyncSession

from app.models.accounting import AuditLog


class AuditService:
    @staticmethod
    def record(
        session: AsyncSession,
        *,
        tenant_id: UUID,
        actor_user_id: UUID | None,
        actor_name: str,
        action: str,
        entity_type: str,
        entity_id: str,
        before_data: dict[str, Any] | None = None,
        after_data: dict[str, Any] | None = None,
        ip_address: str | None = None,
        user_agent: str | None = None,
        correlation_id: str | None = None,
    ) -> AuditLog:
        row = AuditLog(
            tenant_id=tenant_id,
            actor_user_id=actor_user_id,
            actor_name_snapshot=actor_name,
            action=action,
            entity_type=entity_type,
            entity_id=str(entity_id),
            before_data=AuditService._json_safe(before_data),
            after_data=AuditService._json_safe(after_data),
            ip_address=ip_address,
            user_agent=user_agent,
            correlation_id=correlation_id,
        )
        session.add(row)
        return row

    @staticmethod
    def _json_safe(value: dict[str, Any] | None) -> dict[str, Any] | None:
        if value is None:
            return None
        return json.loads(json.dumps(value, ensure_ascii=False, default=str))
