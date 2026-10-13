from decimal import Decimal
from uuid import UUID

from fastapi import APIRouter, Depends, HTTPException, status
from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from app.database import get_db
from app.dependencies import TenantContext, admin_or_accountant
from app.models import Product, StockBalance, StockMove
from app.schemas import StockTransferRequest
from app.services.accounting import audit
from app.services.inventory import get_or_create_balance, get_product, get_warehouse

router = APIRouter(prefix="/inventory", tags=["Inventory"])


@router.post("/transfers", status_code=status.HTTP_201_CREATED)
async def transfer_stock(
    payload: StockTransferRequest,
    context: TenantContext = Depends(admin_or_accountant),
    session: AsyncSession = Depends(get_db),
) -> dict:
    if payload.source_warehouse_id == payload.destination_warehouse_id:
        raise HTTPException(status_code=422, detail="Source and destination warehouses must be different")
    product = await get_product(session, context.tenant_id, payload.product_id, lock=True)
    if product.item_type == "SERVICE":
        raise HTTPException(status_code=422, detail="Service products do not have stock transfers")
    source = await get_warehouse(session, context.tenant_id, payload.source_warehouse_id, lock=True)
    destination = await get_warehouse(session, context.tenant_id, payload.destination_warehouse_id, lock=True)

    # Lock balance rows in a deterministic order to reduce deadlock probability.
    warehouse_ids = sorted((source.id, destination.id), key=str)
    balances: dict[UUID, StockBalance] = {}
    for warehouse_id in warehouse_ids:
        balances[warehouse_id] = await get_or_create_balance(session, context.tenant_id, warehouse_id, product.id, lock=True)
    source_balance = balances[source.id]
    destination_balance = balances[destination.id]

    quantity = Decimal(payload.quantity)
    if Decimal(source_balance.quantity) < quantity:
        raise HTTPException(status_code=422, detail={
            "message": "Insufficient stock for transfer",
            "available": str(source_balance.quantity),
            "requested": str(quantity),
        })

    moving_average_cost = Decimal(source_balance.average_cost)
    source_balance.quantity = Decimal(source_balance.quantity) - quantity
    destination_quantity = Decimal(destination_balance.quantity)
    new_destination_quantity = destination_quantity + quantity
    if new_destination_quantity > 0:
        destination_balance.average_cost = (
            destination_quantity * Decimal(destination_balance.average_cost) + quantity * moving_average_cost
        ) / new_destination_quantity
    destination_balance.quantity = new_destination_quantity

    move = StockMove(
        tenant_id=context.tenant_id,
        product_id=product.id,
        source_warehouse_id=source.id,
        destination_warehouse_id=destination.id,
        move_type="TRANSFER",
        quantity=quantity,
        unit_cost=moving_average_cost,
        source_document_type="WAREHOUSE_TRANSFER",
        reference=payload.reference,
        memo=payload.memo or f"تحويل من {source.code} إلى {destination.code}",
    )
    session.add(move)
    await session.flush()
    await audit(session, context.tenant_id, context.user_id, "STOCK_TRANSFER", "StockMove", move.id, {
        "product_id": str(product.id),
        "source_warehouse_id": str(source.id),
        "destination_warehouse_id": str(destination.id),
        "quantity": str(quantity),
        "unit_cost": str(moving_average_cost),
    })
    return {
        "id": str(move.id),
        "product_id": str(product.id),
        "source_warehouse_id": str(source.id),
        "destination_warehouse_id": str(destination.id),
        "quantity": str(quantity),
        "unit_cost": str(moving_average_cost),
        "status": "POSTED",
    }
