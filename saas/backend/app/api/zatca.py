from fastapi import APIRouter, Depends
from sqlalchemy.ext.asyncio import AsyncSession

from app.database import get_db
from app.dependencies import TenantContext, get_tenant_context
from app.models import Tenant

router = APIRouter(prefix="/zatca", tags=["ZATCA e-invoicing"])


@router.get("/capabilities")
async def capabilities(
    context: TenantContext = Depends(get_tenant_context),
    session: AsyncSession = Depends(get_db),
) -> dict:
    tenant = await session.get(Tenant, context.tenant_id)
    has_tin = bool(tenant and tenant.tax_number)
    return {
        "phase_1_tlv_qr_generator": True,
        "tenant_tax_number_configured": has_tin,
        "phase_2_crypto_signing_configured": False,
        "phase_2_clearance_or_reporting_configured": False,
        "zatca_api_credentials_configured": False,
        "production_compliance_claim": False,
        "message": (
            "The code can create the five-field TLV QR payload. Phase 2 requires separately implemented and tested "
            "certificate onboarding, cryptographic signing, invoice hash/chain, XML validation and ZATCA clearance or reporting."
        ),
    }
