from decimal import Decimal
from uuid import UUID

from fastapi import HTTPException
from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from app.models import Product, StockBalance, StockMove, Warehouse
from app.services.accounting import money


async def get_product(session: AsyncSession, tenant_id: UUID, product_id: UUID, lock: bool = False) -> Product:
    statement = select(Product).where(
        Product.tenant_id == tenant_id,
        Product.id == product_id,
        Product.is_active.is_(True),
    )
    if lock:
        statement = statement.with_for_update()
    product = await session.scalar(statement)
    if product is None:
        raise HTTPException(status_code=422, detail=f"Product {product_id} is missing or inactive")
    return product


async def get_warehouse(session: AsyncSession, tenant_id: UUID, warehouse_id: UUID, lock: bool = False) -> Warehouse:
    statement = select(Warehouse).where(
        Warehouse.tenant_id == tenant_id,
        Warehouse.id == warehouse_id,
        Warehouse.is_active.is_(True),
    )
    if lock:
        statement = statement.with_for_update()
    warehouse = await session.scalar(statement)
    if warehouse is None:
        raise HTTPException(status_code=422, detail=f"Warehouse {warehouse_id} is missing or inactive")
    return warehouse


async def get_or_create_balance(
    session: AsyncSession,
    tenant_id: UUID,
    warehouse_id: UUID,
    product_id: UUID,
    lock: bool = True,
) -> StockBalance:
    statement = select(StockBalance).where(
        StockBalance.tenant_id == tenant_id,
        StockBalance.warehouse_id == warehouse_id,
        StockBalance.product_id == product_id,
    )
    if lock:
        statement = statement.with_for_update()
    balance = await session.scalar(statement)
    if balance is not None:
        return balance
    balance = StockBalance(
        tenant_id=tenant_id,
        warehouse_id=warehouse_id,
        product_id=product_id,
        quantity=Decimal("0"),
        average_cost=Decimal("0"),
    )
    session.add(balance)
    try:
        await session.flush()
    except Exception:
        raise HTTPException(status_code=409, detail="Concurrent stock initialization; retry this operation")
    return balance


async def receive_stock(
    session: AsyncSession,
    tenant_id: UUID,
    product: Product,
    warehouse_id: UUID,
    quantity: Decimal,
    unit_cost: Decimal,
    document_type: str,
    document_id: UUID,
    reference: str,
) -> None:
    if product.item_type == "SERVICE":
        return
    balance = await get_or_create_balance(session, tenant_id, warehouse_id, product.id)
    old_qty = Decimal(balance.quantity)
    incoming_qty = Decimal(quantity)
    old_value = old_qty * Decimal(balance.average_cost)
    incoming_value = incoming_qty * Decimal(unit_cost)
    new_qty = old_qty + incoming_qty
    if new_qty <= 0:
        raise HTTPException(status_code=422, detail="Received stock quantity must be positive")
    balance.average_cost = (old_value + incoming_value) / new_qty
    balance.quantity = new_qty
    product.standard_cost = balance.average_cost
    session.add(StockMove(
        tenant_id=tenant_id,
        product_id=product.id,
        source_warehouse_id=None,
        destination_warehouse_id=warehouse_id,
        move_type="RECEIPT",
        quantity=incoming_qty,
        unit_cost=unit_cost,
        source_document_type=document_type,
        source_document_id=document_id,
        reference=reference,
        memo="استلام مشتريات",
    ))
    await session.flush()


async def issue_stock(
    session: AsyncSession,
    tenant_id: UUID,
    product: Product,
    warehouse_id: UUID,
    quantity: Decimal,
    document_type: str,
    document_id: UUID,
    reference: str,
) -> tuple[Decimal, Decimal]:
    if product.item_type == "SERVICE":
        return Decimal("0.00"), Decimal("0.0000")
    balance = await get_or_create_balance(session, tenant_id, warehouse_id, product.id)
    if Decimal(balance.quantity) < Decimal(quantity):
        raise HTTPException(status_code=422, detail={
            "message": "Insufficient stock",
            "product": product.name,
            "available": str(balance.quantity),
            "requested": str(quantity),
            "warehouse_id": str(warehouse_id),
        })
    unit_cost = Decimal(balance.average_cost)
    total_cost = money(unit_cost * Decimal(quantity))
    balance.quantity = Decimal(balance.quantity) - Decimal(quantity)
    session.add(StockMove(
        tenant_id=tenant_id,
        product_id=product.id,
        source_warehouse_id=warehouse_id,
        destination_warehouse_id=None,
        move_type="ISSUE",
        quantity=Decimal(quantity),
        unit_cost=unit_cost,
        source_document_type=document_type,
        source_document_id=document_id,
        reference=reference,
        memo="صرف مبيعات",
    ))
    await session.flush()
    return total_cost, unit_cost
