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

## Phase 1 — accounting data and posting foundation

The following checks are currently implemented in the PostgreSQL-backed core:
- Tenant-owned rows use PostgreSQL RLS with a transaction-local company context, and relationships between tenant-owned records use tenant-aware composite foreign keys.
- The general ledger supports a hierarchical chart of accounts, statement type/nature, active/postable controls, and automatic child-code generation.
- Journal lines use NUMERIC values and optional cost centers; posting validates two or more single-sided lines and balances debit and credit to two decimal places.
- Posting requires an open fiscal period covering the journal date, rejects inactive or non-postable accounts, and allocates sequence numbers safely across concurrent requests.
- Posted journal entries are immutable; correction is done through a reversal journal with a second approver.
- Period closing/reopening is audited, requires a second approver, and refuses closing when draft or unbalanced posted journals remain.
- Audit events retain before/after JSON snapshots, actor, request address, user-agent and correlation ID. PostgreSQL prevents audit rows from being updated or deleted.
- The Alembic bootstrap installs row-level isolation policies and database triggers that independently enforce journal balance/immutability and append-only audit behavior.

### Verified CI run
The SaaS CI run 38433569500 completed successfully. It applied the PostgreSQL migrations, passed Ruff static analysis, and passed **12 tests** covering balance validation, rounding, account-code generation, concurrent numbering and year reset, period overlap/dual-control, posting without a period, reversals, inactive accounts, RLS cross-tenant reads, database-enforced journal immutability and balance, and append-only audit snapshots.
## Scope honesty
This is a concrete starter platform, not yet a market-ready/certified ERP. The accounting core and CRUD/posting APIs are being laid down; IFRS presentation, year-end closing, multi-currency revaluation, payment/bank reconciliation, payroll, restaurant/POS, construction job costing, audit requirements, ZATCA Phase 2 onboarding/signing/clearance/reporting, penetration testing, restore drills and commercial subscription billing each require dedicated implementation and domain review. Do not claim ZATCA compliance merely because an invoice table or endpoint exists.
