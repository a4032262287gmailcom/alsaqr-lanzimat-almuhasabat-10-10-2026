# Changelog

All notable changes to the AlSaqr SaaS workstream are recorded here.

## 2026-10-13 — Phase 1 accounting core

- Documented the existing PostgreSQL and SQLAlchemy 2 multi-tenant model, including companies, users, memberships, branch access, permissions, chart of accounts, cost centers, journals, journal lines, periods, sequences, inventory references, invoices and audit events.
- Required every journal posting date to belong to an explicitly configured open fiscal period.
- Extended audit events to preserve before/after snapshots and request metadata, including correlation IDs.
- Added a transaction-correlated request ID middleware.
- Added tests for nested account-code allocation, missing posting periods, audit snapshots and tenant-scoped RLS.
- Added a second Alembic revision that safely adds audit snapshot columns whether the bootstrap schema has already included them or not.
- Added a CI run using PostgreSQL 16, a separate migration role, an application role, Ruff, and the PostgreSQL-backed test suite.
- Verified on CI run 38433569500: migrations passed, Ruff passed, 12 tests passed.

## Known limitations

This is the first deliverable stage, not a declaration that the whole ERP is production-certified. Full IFRS/SOCPA reporting, valuation method transitions with historical layers, foreign-currency revaluation, statutory ZATCA Phase 2 commissioning, Saudi payroll/GOSI/WPS calculations, PDPL operational controls, penetration testing, backups/restore drills and SLA monitoring remain subject to separate engineering, regulatory and business acceptance.
