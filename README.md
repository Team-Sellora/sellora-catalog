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

In the Staging environment, the service seeds five active products for Organization's `SELLORA-DEMO` company after applying migrations. The seed is idempotent, and it never runs in Development, Testing, or Production.

The service applies pending EF Core migrations at startup before serving requests, matching Organization. The `Testing` environment skips this startup step because the PostgreSQL fixtures apply migrations themselves. Migration `20260905180000_ScopeBatchCodesToProduct` replaces the company-wide batch-code index without deleting data. Rolling back requires resolving any batch codes reused across products before restoring the old unique index.

Apply `20260906121212_AddProductPriceHistory` and
`20260906124412_AddCatalogOutbox` before deploying the price-change workflow.

API and database tests use Testcontainers.PostgreSql 4.14.0 with `postgres:16`, matching Organization's PostgreSQL fixture. Start Docker Desktop in Linux-container mode before running tests. Testcontainers downloads the image when needed, applies real migrations to temporary databases, and removes its containers afterward. Tests cover search, tenant isolation, price history and outbox persistence, database constraints, and migrations; no SQLite fallback is used.

The API applies pending migrations at startup, except in `Testing`, where fixtures apply them. Local configuration matches Docker Compose (`catalog_db` on port 5434). Start the development database before the API. Hosted environments must override `ConnectionStrings__Default`. Existing data from a different local database is not transferred automatically.

Run tests with coverage using `dotnet test -c Release --collect "XPlat Code Coverage"`.

## Local commands

```bash
dotnet restore
dotnet build
dotnet test
docker compose up -d
```
