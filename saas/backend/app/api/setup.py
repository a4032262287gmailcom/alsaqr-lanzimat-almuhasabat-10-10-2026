from decimal import Decimal
from uuid import UUID

from fastapi import APIRouter, Depends, HTTPException, Query, status
from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from app.database import get_db
from app.dependencies import TenantContext, admin_only, admin_or_accountant, get_tenant_context
from app.models import Account, Contact, CostCenter, Product, StockBalance, Warehouse
from app.schemas import (
    AccountCreate, AccountOut, ContactCreate, ContactOut, CostCenterCreate,
    CostCenterOut, ProductCreate, ProductOut, StockBalanceOut, WarehouseCreate, WarehouseOut,
)
from app.services.accounting import audit, get_account_by_code, money, next_child_code
from app.services.inventory import get_product, get_warehouse, receive_stock

router = APIRouter(prefix="/setup", tags=["Company setup"])


@router.get("/accounts", response_model=list[AccountOut])
async def list_accounts(
    context: TenantContext = Depends(get_tenant_context),
    session: AsyncSession = Depends(get_db),
    include_inactive: bool = Query(False),
) -> list[AccountOut]:
    statement = select(Account).where(Account.tenant_id == context.tenant_id)
    if not include_inactive:
        statement = statement.where(Account.is_active.is_(True))
    statement = statement.order_by(Account.code)
    return [AccountOut.model_validate(row) for row in (await session.scalars(statement)).all()]


@router.post("/accounts", response_model=AccountOut, status_code=status.HTTP_201_CREATED)
async def create_account(
    payload: AccountCreate,
    context: TenantContext = Depends(admin_or_accountant),
    session: AsyncSession = Depends(get_db),
) -> AccountOut:
    parent = None
    if payload.parent_id:
        parent = await session.scalar(select(Account).where(
            Account.tenant_id == context.tenant_id,
            Account.id == payload.parent_id,
            Account.is_active.is_(True),
        ))
        if parent is None:
            raise HTTPException(status_code=422, detail="Parent account is missing or inactive")
        if parent.allow_posting:
            raise HTTPException(status_code=422, detail="A posting account cannot have child accounts")
    existing = (await session.scalars(select(Account.code).where(Account.tenant_id == context.tenant_id))).all()
    code = payload.code
    if code is None:
        if parent is None:
            roots = [value for value in existing if len(value) == 1 and value.isdigit()]
            next_root = max([int(value) for value in roots], default=0) + 1
            if next_root > 9:
                raise HTTPException(status_code=409, detail="Top-level automatic codes 1-9 are exhausted")
            code = str(next_root)
        else:
            code = next_child_code(parent.code, existing)
    if code in existing:
        raise HTTPException(status_code=409, detail="Account code is already in use in this company")
    account = Account(
        tenant_id=context.tenant_id,
        code=code,
        name=payload.name,
        parent_id=parent.id if parent else None,
        statement_type=payload.statement_type,
        nature=payload.nature,
        account_category=payload.account_category,
        allow_posting=payload.allow_posting,
        is_system=False,
        is_active=True,
    )
    session.add(account)
    await session.flush()
    await audit(session, context.tenant_id, context.user_id, "ACCOUNT_CREATED", "Account", account.id, {"code": code, "name": account.name})
    return AccountOut.model_validate(account)


@router.get("/cost-centers", response_model=list[CostCenterOut])
async def list_cost_centers(
    context: TenantContext = Depends(get_tenant_context),
    session: AsyncSession = Depends(get_db),
) -> list[CostCenterOut]:
    rows = (await session.scalars(
        select(CostCenter).where(CostCenter.tenant_id == context.tenant_id, CostCenter.is_active.is_(True)).order_by(CostCenter.code)
    )).all()
    return [CostCenterOut.model_validate(row) for row in rows]


@router.post("/cost-centers", response_model=CostCenterOut, status_code=status.HTTP_201_CREATED)
async def create_cost_center(
    payload: CostCenterCreate,
    context: TenantContext = Depends(admin_or_accountant),
    session: AsyncSession = Depends(get_db),
) -> CostCenterOut:
    exists = await session.scalar(select(CostCenter.id).where(CostCenter.tenant_id == context.tenant_id, CostCenter.code == payload.code))
    if exists:
        raise HTTPException(status_code=409, detail="Cost center code already exists")
    if payload.parent_id and not await session.scalar(select(CostCenter.id).where(
        CostCenter.tenant_id == context.tenant_id, CostCenter.id == payload.parent_id, CostCenter.is_active.is_(True)
    )):
        raise HTTPException(status_code=422, detail="Parent cost center is missing or inactive")
    center = CostCenter(tenant_id=context.tenant_id, code=payload.code, name=payload.name, parent_id=payload.parent_id, is_active=True)
    session.add(center)
    await session.flush()
    await audit(session, context.tenant_id, context.user_id, "COST_CENTER_CREATED", "CostCenter", center.id, {"code": center.code})
    return CostCenterOut.model_validate(center)


@router.get("/contacts", response_model=list[ContactOut])
async def list_contacts(
    context: TenantContext = Depends(get_tenant_context),
    session: AsyncSession = Depends(get_db),
    contact_type: str | None = Query(default=None, pattern="^(CUSTOMER|SUPPLIER|BOTH)$"),
    search: str | None = Query(default=None, max_length=120),
) -> list[ContactOut]:
    statement = select(Contact).where(Contact.tenant_id == context.tenant_id, Contact.is_active.is_(True))
    if contact_type:
        statement = statement.where(Contact.contact_type.in_([contact_type, "BOTH"]))
    if search:
        statement = statement.where(Contact.name.ilike(f"%{search.replace('%', r'\%').replace('_', r'\_')}%"))
    rows = (await session.scalars(statement.order_by(Contact.name).limit(500))).all()
    return [ContactOut.model_validate(row) for row in rows]


@router.post("/contacts", response_model=ContactOut, status_code=status.HTTP_201_CREATED)
async def create_contact(
    payload: ContactCreate,
    context: TenantContext = Depends(admin_or_accountant),
    session: AsyncSession = Depends(get_db),
) -> ContactOut:
    if payload.tax_number:
        existing = await session.scalar(select(Contact.id).where(
            Contact.tenant_id == context.tenant_id,
            Contact.tax_number == payload.tax_number,
        ))
        if existing:
            raise HTTPException(status_code=409, detail="A contact with this tax number already exists")
    contact = Contact(
        tenant_id=context.tenant_id,
        name=payload.name,
        contact_type=payload.contact_type,
        email=str(payload.email).lower() if payload.email else None,
        phone=payload.phone,
        tax_number=payload.tax_number,
        address=payload.address,
        is_active=True,
    )
    session.add(contact)
    await session.flush()
    await audit(session, context.tenant_id, context.user_id, "CONTACT_CREATED", "Contact", contact.id, {"name": contact.name, "type": contact.contact_type})
    return ContactOut.model_validate(contact)


@router.get("/warehouses", response_model=list[WarehouseOut])
async def list_warehouses(
    context: TenantContext = Depends(get_tenant_context),
    session: AsyncSession = Depends(get_db),
) -> list[WarehouseOut]:
    rows = (await session.scalars(
        select(Warehouse).where(Warehouse.tenant_id == context.tenant_id, Warehouse.is_active.is_(True)).order_by(Warehouse.is_default.desc(), Warehouse.code)
    )).all()
    return [WarehouseOut.model_validate(row) for row in rows]


@router.post("/warehouses", response_model=WarehouseOut, status_code=status.HTTP_201_CREATED)
async def create_warehouse(
    payload: WarehouseCreate,
    context: TenantContext = Depends(admin_only),
    session: AsyncSession = Depends(get_db),
) -> WarehouseOut:
    if await session.scalar(select(Warehouse.id).where(Warehouse.tenant_id == context.tenant_id, Warehouse.code == payload.code)):
        raise HTTPException(status_code=409, detail="Warehouse code already exists")
    if payload.is_default:
        await session.execute(
            select(Warehouse).where(Warehouse.tenant_id == context.tenant_id, Warehouse.is_default.is_(True)).with_for_update()
        )
        previous = (await session.scalars(select(Warehouse).where(Warehouse.tenant_id == context.tenant_id, Warehouse.is_default.is_(True)))).all()
        for warehouse in previous:
            warehouse.is_default = False
    warehouse = Warehouse(tenant_id=context.tenant_id, code=payload.code, name=payload.name, address=payload.address, is_default=payload.is_default, is_active=True)
    session.add(warehouse)
    await session.flush()
    await audit(session, context.tenant_id, context.user_id, "WAREHOUSE_CREATED", "Warehouse", warehouse.id, {"code": warehouse.code})
    return WarehouseOut.model_validate(warehouse)


@router.get("/products", response_model=list[ProductOut])
async def list_products(
    context: TenantContext = Depends(get_tenant_context),
    session: AsyncSession = Depends(get_db),
    search: str | None = Query(default=None, max_length=120),
    barcode: str | None = Query(default=None, max_length=128),
) -> list[ProductOut]:
    statement = select(Product).where(Product.tenant_id == context.tenant_id, Product.is_active.is_(True))
    if barcode:
        statement = statement.where(Product.barcode == barcode)
    if search:
        escaped = search.replace("%", r"\%").replace("_", r"\_")
        statement = statement.where(Product.name.ilike(f"%{escaped}%") | Product.sku.ilike(f"%{escaped}%") | Product.barcode.ilike(f"%{escaped}%"))
    rows = (await session.scalars(statement.order_by(Product.name).limit(500))).all()
    return [ProductOut.model_validate(row) for row in rows]


@router.post("/products", response_model=ProductOut, status_code=status.HTTP_201_CREATED)
async def create_product(
    payload: ProductCreate,
    context: TenantContext = Depends(admin_or_accountant),
    session: AsyncSession = Depends(get_db),
) -> ProductOut:
    exists = await session.scalar(select(Product.id).where(Product.tenant_id == context.tenant_id, Product.sku == payload.sku))
    if exists:
        raise HTTPException(status_code=409, detail="SKU already exists in this company")
    if payload.barcode and await session.scalar(select(Product.id).where(Product.tenant_id == context.tenant_id, Product.barcode == payload.barcode)):
        raise HTTPException(status_code=409, detail="Barcode already exists in this company")
    accounts = {code: await get_account_by_code(session, context.tenant_id, code) for code in ("1201", "4101", "5201")}
    product = Product(
        tenant_id=context.tenant_id,
        sku=payload.sku,
        name=payload.name,
        description=payload.description,
        item_type=payload.item_type,
        barcode=payload.barcode,
        unit_of_measure=payload.unit_of_measure,
        standard_cost=payload.standard_cost,
        unit_price=payload.unit_price,
        vat_rate=payload.vat_rate,
        inventory_account_id=accounts["1201"].id,
        revenue_account_id=accounts["4101"].id,
        expense_account_id=accounts["5201"].id,
        is_active=True,
    )
    session.add(product)
    await session.flush()
    await audit(session, context.tenant_id, context.user_id, "PRODUCT_CREATED", "Product", product.id, {"sku": product.sku, "item_type": product.item_type})
    return ProductOut.model_validate(product)


@router.get("/stock-balances", response_model=list[StockBalanceOut])
async def list_stock_balances(
    context: TenantContext = Depends(get_tenant_context),
    session: AsyncSession = Depends(get_db),
    warehouse_id: UUID | None = None,
) -> list[StockBalanceOut]:
    statement = select(StockBalance).where(StockBalance.tenant_id == context.tenant_id)
    if warehouse_id:
        statement = statement.where(StockBalance.warehouse_id == warehouse_id)
    rows = (await session.scalars(statement.order_by(StockBalance.warehouse_id, StockBalance.product_id))).all()
    return [StockBalanceOut.model_validate(row) for row in rows]
