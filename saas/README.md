# AlSaqr Cloud ERP — SaaS foundation

This is an independent cloud-SaaS workstream. It does not replace or modify the Windows Forms desktop accounting app.

## Stack
- Python 3.12, FastAPI, Pydantic 2
- SQLAlchemy 2 async ORM and PostgreSQL
- Alembic schema bootstrap plus PostgreSQL Row-Level Security (RLS)
- JWT access tokens, Argon2 password hashing, tenant memberships and role checks
- React + Vite frontend
- Docker Compose for a local development environment

## Run locally
```powershell
cd saas
Copy-Item .env.example .env
docker compose up --build
```

Then open `http://localhost:5173`; API docs are at `http://localhost:8000/docs`.

The values in `.env.example` are for local development only. Replace database credentials and `JWT_SECRET_KEY`, disable debug mode, use HTTPS, configure backups/monitoring, and run the API with a least-privilege PostgreSQL role before any production deployment.

## API basics
- `POST /api/v1/auth/register` — creates a user, company (tenant), default chart of accounts and main warehouse.
- `POST /api/v1/auth/login` — authenticates a user and returns a short-lived JWT plus company memberships.
- `GET /api/v1/auth/me` — current identity.
- `GET /api/v1/companies/mine` — companies available to the signed-in user.
- Tenant-scoped operations require `Authorization: Bearer <token>` and `X-Company-ID: <company UUID>`.
- `GET/POST /api/v1/accounting/accounts`, `GET/POST /api/v1/accounting/journals`.
- `GET/POST /api/v1/setup/parties`, `GET/POST /api/v1/setup/products`, `GET/POST /api/v1/setup/warehouses`.
- `POST /api/v1/commerce/sales` and `POST /api/v1/commerce/purchases` perform the document, journal, VAT, stock balance/movement, and audit writes within one database transaction.
- `GET /api/v1/zatca/capabilities` explicitly reports whether ZATCA credentials and submission integration are configured; the base project does not pretend to be certified or to have submitted e-invoices.

## Accounting and isolation invariants
- Money is stored as fixed precision decimals; posted journals must have at least two lines, positive single-sided lines, and equal debit/credit totals rounded to two decimals.
- Tenant-owned tables have RLS policies driven by the transaction-local PostgreSQL setting `app.current_tenant_id`. API dependencies verify membership before tenant endpoints execute.
- In production, the application database role must not be a PostgreSQL superuser or have `BYPASSRLS`; those roles can bypass row-level security.
- Posted documents are not edited silently. Corrections should use reversal/credit-note workflows that are implemented and audited explicitly.

## Scope honesty
This is a concrete starter platform, not yet a market-ready/certified ERP. The accounting core and CRUD/posting APIs are being laid down; IFRS presentation, year-end closing, multi-currency revaluation, payment/bank reconciliation, payroll, restaurant/POS, construction job costing, audit requirements, ZATCA Phase 2 onboarding/signing/clearance/reporting, penetration testing, restore drills and commercial subscription billing each require dedicated implementation and domain review. Do not claim ZATCA compliance merely because an invoice table or endpoint exists.
