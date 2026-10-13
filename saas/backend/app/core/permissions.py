from collections.abc import Iterable
from uuid import UUID

from fastapi import HTTPException
from sqlalchemy import select
from sqlalchemy.dialects.postgresql import insert as pg_insert
from sqlalchemy.ext.asyncio import AsyncSession

from app.models import PermissionDefinition, RolePermission

ROLE_CODES = (
    "Owner", "Admin", "CFO", "Accountant", "Cashier", "Sales",
    "Purchasing", "Storekeeper", "HR", "Auditor", "FieldAgent", "ReadOnly",
)

PERMISSION_DEFINITIONS = (
    ("account.read", "accounting", "read", "View the chart of accounts"),
    ("account.create", "accounting", "create", "Create or maintain accounts"),
    ("cost_center.read", "accounting", "read", "View cost centers and analytical dimensions"),
    ("cost_center.create", "accounting", "create", "Create or maintain cost centers"),
    ("journal.read", "accounting", "read", "View journal entries and their lines"),
    ("journal.create", "accounting", "create", "Create and post balanced journal entries"),
    ("journal.reverse", "accounting", "reverse", "Reverse an immutable posted journal"),
    ("period.read", "accounting", "read", "View financial periods"),
    ("period.create", "accounting", "create", "Create financial periods"),
    ("period.close", "accounting", "close", "Close a financial period with dual control"),
    ("period.reopen", "accounting", "reopen", "Reopen a financial period with dual control"),
    ("report.financial.read", "reports", "read", "View general-ledger financial reports"),
    ("report.tax.read", "tax", "read", "View tax reports and tax-period data"),
    ("audit.read", "security", "read", "Read the append-only audit trail"),
    ("contact.read", "contacts", "read", "View customers and suppliers"),
    ("contact.create", "contacts", "create", "Create or maintain customers and suppliers"),
    ("product.read", "inventory", "read", "View products and services"),
    ("product.create", "inventory", "create", "Create or maintain products and services"),
    ("warehouse.read", "inventory", "read", "View warehouses"),
    ("warehouse.manage", "inventory", "manage", "Create or maintain warehouses"),
    ("inventory.read", "inventory", "read", "View stock balances and movements"),
    ("inventory.transfer", "inventory", "transfer", "Transfer stock between warehouses"),
    ("inventory.adjust", "inventory", "adjust", "Adjust quantities after authorization"),
    ("invoice.read", "commerce", "read", "View sales invoices and supplier bills"),
    ("invoice.sale.create", "commerce", "create", "Create and post sales invoices"),
    ("invoice.purchase.create", "commerce", "create", "Create and post supplier bills"),
    ("invoice.ocr.review", "commerce", "review", "Review a machine-extracted supplier invoice draft"),
    ("invoice.ocr.approve", "commerce", "approve", "Approve and post an OCR supplier invoice draft"),
    ("tax.zatca.read", "tax", "read", "View e-invoicing configuration and delivery states"),
    ("tax.zatca.submit", "tax", "submit", "Submit a validated e-invoice to the configured ZATCA environment"),
    ("permissions.read", "security", "read", "View the permission catalog and role matrix"),
    ("permissions.manage", "security", "manage", "Override role permissions for this company"),
    ("branch.read", "organization", "read", "View company branches"),
    ("branch.manage", "organization", "manage", "Manage company branches and branch assignments"),
    ("users.read", "security", "read", "View company memberships"),
    ("users.manage", "security", "manage", "Manage company memberships and roles"),
    ("subscription.read", "subscription", "read", "View company plan and usage"),
    ("subscription.manage", "subscription", "manage", "Manage plan, seats, branches, and paid features"),
    ("company.delete", "organization", "delete", "Delete a company after verified retention and ownership checks"),
)

PERMISSION_CATALOG = {
    code: {"code": code, "module": module, "action": action, "description": description}
    for code, module, action, description in PERMISSION_DEFINITIONS
}
ALL_PERMISSION_CODES = frozenset(PERMISSION_CATALOG)

ROLE_DEFAULT_PERMISSIONS: dict[str, frozenset[str]] = {
    "Owner": ALL_PERMISSION_CODES,
    "Admin": ALL_PERMISSION_CODES - frozenset({"company.delete", "subscription.manage"}),
    "CFO": frozenset({
        "account.read", "account.create", "cost_center.read", "cost_center.create",
        "journal.read", "journal.create", "journal.reverse", "period.read", "period.create",
        "period.close", "period.reopen", "report.financial.read", "report.tax.read", "audit.read",
        "contact.read", "product.read", "warehouse.read", "inventory.read", "invoice.read",
        "invoice.purchase.create", "invoice.ocr.review", "invoice.ocr.approve", "tax.zatca.read",
        "tax.zatca.submit", "branch.read", "users.read", "permissions.read", "subscription.read",
    }),
    "Accountant": frozenset({
        "account.read", "account.create", "cost_center.read", "cost_center.create",
        "journal.read", "journal.create", "journal.reverse", "period.read", "report.financial.read",
        "report.tax.read", "contact.read", "contact.create", "product.read", "product.create",
        "warehouse.read", "inventory.read", "invoice.read", "invoice.sale.create",
        "invoice.purchase.create", "invoice.ocr.review", "invoice.ocr.approve", "tax.zatca.read",
        "tax.zatca.submit", "branch.read",
    }),
    "Cashier": frozenset({
        "contact.read", "contact.create", "product.read", "warehouse.read", "invoice.read",
        "invoice.sale.create",
    }),
    "Sales": frozenset({
        "contact.read", "contact.create", "product.read", "warehouse.read", "invoice.read",
        "invoice.sale.create",
    }),
    "Purchasing": frozenset({
        "contact.read", "contact.create", "product.read", "warehouse.read", "invoice.read",
        "invoice.purchase.create", "invoice.ocr.review",
    }),
    "Storekeeper": frozenset({
        "product.read", "warehouse.read", "inventory.read", "inventory.transfer", "inventory.adjust",
        "branch.read",
    }),
    "HR": frozenset(),
    "Auditor": frozenset({
        "account.read", "cost_center.read", "journal.read", "period.read", "report.financial.read",
        "report.tax.read", "audit.read", "contact.read", "product.read", "warehouse.read",
        "inventory.read", "invoice.read", "tax.zatca.read", "branch.read", "users.read",
    }),
    "FieldAgent": frozenset({
        "contact.read", "contact.create", "product.read", "warehouse.read", "invoice.read",
        "invoice.sale.create",
    }),
    "ReadOnly": frozenset({
        "account.read", "cost_center.read", "journal.read", "period.read", "report.financial.read",
        "contact.read", "product.read", "warehouse.read", "inventory.read", "invoice.read",
        "branch.read",
    }),
}


async def seed_permission_catalog(session: AsyncSession) -> None:
    rows = [dict(item) for item in PERMISSION_DEFINITIONS]
    statement = pg_insert(PermissionDefinition).values(rows).on_conflict_do_nothing(
        index_elements=[PermissionDefinition.code]
    )
    await session.execute(statement)


async def has_permission(
    session: AsyncSession,
    tenant_id: UUID,
    role_code: str,
    permission_code: str,
) -> bool:
    if permission_code not in ALL_PERMISSION_CODES:
        raise HTTPException(status_code=500, detail="An endpoint references an unknown permission code")

    override = await session.scalar(
        select(RolePermission.allowed).where(
            RolePermission.tenant_id == tenant_id,
            RolePermission.role_code == role_code,
            RolePermission.permission_code == permission_code,
        )
    )
    if override is not None:
        return bool(override)
    return permission_code in ROLE_DEFAULT_PERMISSIONS.get(role_code, frozenset())


async def list_effective_permissions(
    session: AsyncSession,
    tenant_id: UUID,
    role_code: str,
) -> list[dict]:
    overrides = (await session.execute(
        select(RolePermission.permission_code, RolePermission.allowed).where(
            RolePermission.tenant_id == tenant_id,
            RolePermission.role_code == role_code,
        )
    )).all()
    override_map = {code: bool(allowed) for code, allowed in overrides}
    defaults = ROLE_DEFAULT_PERMISSIONS.get(role_code, frozenset())
    return [
        {
            **definition,
            "default_allowed": code in defaults,
            "effective_allowed": override_map.get(code, code in defaults),
            "overridden": code in override_map,
        }
        for code, definition in sorted(PERMISSION_CATALOG.items())
    ]


def validate_role_code(role_code: str) -> None:
    if role_code not in ROLE_CODES:
        raise HTTPException(status_code=422, detail="Unsupported role code")
