# Sellora Product & Catalog Service

The Product & Catalog microservice is the authoritative, tenant-scoped source of product, batch, and price information for Sellora.

## Technology

- .NET 8 / ASP.NET Core
- PostgreSQL (`catalog_db`)
- Entity Framework Core
- WSO2 Identity Server and API Manager
- Kafka transactional outbox (US-E2-2)
- Internal gRPC and Redis cache (US-E2-3)

## Architecture

```text
Domain          Business entities and rules
Application     Use cases and contracts
Infrastructure  Persistence and external integrations
Api             REST/gRPC endpoints, authentication and middleware
Tests           Unit and integration tests
```

## Story order

1. US-E2-1 — Product lifecycle scoped to the company
2. US-E2-2 — Price change with audit trail and event
3. US-E2-3 — Product and price read API for Order
4. US-E2-4 — Product categories (deferrable)

The service owns its database. `companyId` is an opaque identifier obtained from the authenticated JWT; the Catalog service must never query the Organization database.

## Catalogue behavior

- `GET /api/products` defaults to active products. Use `status=Active`, `status=Inactive`, or `status=All` (case-insensitive); filtering happens before counts and pagination. Other status values return 400.
- Deactivated products remain retrievable by ID for historical records.
- Product reads and writes require a valid `companyId` claim; missing or malformed claims return 401.
- Initial prices must fit `numeric(18,2)`: 0.01 through 9999999999999999.99, with at most two decimal places. Invalid prices return 400 rather than being rounded by the database.
- SKUs are unique per company. Batch codes are unique per company and product, allowing different products to share a batch code.

Apply migration `20260905180000_ScopeBatchCodesToProduct` before deploying this version to an existing database. It replaces the company-wide batch-code index without deleting data. Rolling back requires resolving any batch codes reused across products before restoring the old unique index.

The tests exercise HTTP responses, role restrictions, tenant isolation, product lifecycle, price validation, and batch-code migration/constraints using SQLite. They also verify the PostgreSQL migration script and model snapshot; live PostgreSQL execution is a separate deployment check.

## Local commands

```bash
dotnet restore
dotnet build
dotnet test
docker compose up -d
```
