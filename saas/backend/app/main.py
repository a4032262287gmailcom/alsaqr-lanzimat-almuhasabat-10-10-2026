from contextlib import asynccontextmanager
from uuid import uuid4

from fastapi import FastAPI, Request
from fastapi.middleware.cors import CORSMiddleware
from fastapi.responses import JSONResponse
from sqlalchemy import text
from sqlalchemy.exc import IntegrityError

from app.api import accounting, ai, auth, commerce, inventory, periods, setup, zatca
from app.core.config import get_settings
from app.database import engine
from app.schemas import HealthOut

settings = get_settings()


@asynccontextmanager
async def lifespan(_: FastAPI):
    yield
    await engine.dispose()


app = FastAPI(
    title=settings.app_name,
    version="0.1.0",
    description=(
        "Multi-tenant accounting and inventory API foundation. "
        "Accounting postings are transactional; production IFRS and ZATCA deployment requires separate validation."
    ),
    docs_url="/docs" if settings.app_env.lower() not in {"production", "prod"} else None,
    redoc_url="/redoc" if settings.app_env.lower() not in {"production", "prod"} else None,
    openapi_url="/openapi.json" if settings.app_env.lower() not in {"production", "prod"} else None,
    lifespan=lifespan,
)

app.add_middleware(
    CORSMiddleware,
    allow_origins=settings.cors_origin_list,
    allow_credentials=False,
    allow_methods=["GET", "POST", "PUT", "PATCH", "DELETE", "OPTIONS"],
    allow_headers=["Authorization", "Content-Type", "X-Company-ID", "Idempotency-Key"],
    max_age=600,
)


@app.middleware("http")
async def security_headers(request: Request, call_next):
    supplied_request_id = request.headers.get("X-Request-ID", "").strip()
    request_id = supplied_request_id[:80] if supplied_request_id and len(supplied_request_id) <= 80 else uuid4().hex
    request.state.correlation_id = request_id
    response = await call_next(request)
    response.headers.setdefault("X-Request-ID", request_id)
    response.headers.setdefault("X-Content-Type-Options", "nosniff")
    response.headers.setdefault("X-Frame-Options", "DENY")
    response.headers.setdefault("Referrer-Policy", "strict-origin-when-cross-origin")
    response.headers.setdefault("Cache-Control", "no-store" if request.url.path.startswith("/api/") else "no-cache")
    if settings.app_env.lower() in {"production", "prod"}:
        response.headers.setdefault("Strict-Transport-Security", "max-age=31536000; includeSubDomains")
    return response


@app.exception_handler(IntegrityError)
async def integrity_error_handler(_: Request, exc: IntegrityError):
    message = "The operation conflicts with existing company data or violates a database constraint."
    detail = str(getattr(exc, "orig", "")).lower()
    if "uq_" in detail or "unique constraint" in detail or "duplicate key" in detail:
        message = "A record with the same company-specific code or reference already exists."
    return JSONResponse(status_code=409, content={"detail": message})


@app.get("/healthz", response_model=HealthOut, tags=["Operations"])
async def health() -> HealthOut:
    return HealthOut(status="ok", service=settings.app_name)


@app.get("/readyz", tags=["Operations"])
async def readiness() -> dict:
    async with engine.connect() as connection:
        await connection.execute(text("SELECT 1"))
    return {"status": "ready", "database": "reachable"}


api_prefix = "/api/v1"
app.include_router(auth.router, prefix=api_prefix)
app.include_router(setup.router, prefix=api_prefix)
app.include_router(accounting.router, prefix=api_prefix)
app.include_router(periods.router, prefix=api_prefix)
app.include_router(commerce.router, prefix=api_prefix)
app.include_router(inventory.router, prefix=api_prefix)
app.include_router(ai.router, prefix=api_prefix)
app.include_router(zatca.router, prefix=api_prefix)
