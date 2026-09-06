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

The service applies pending EF Core migrations at startup before serving requests, matching Organization. The `Testing` environment skips this startup step because the PostgreSQL fixtures apply migrations themselves. Migration `20260905180000_ScopeBatchCodesToProduct` replaces the company-wide batch-code index without deleting data. Rolling back requires resolving any batch codes reused across products before restoring the old unique index.

The tests use `Testcontainers.PostgreSql` 4.14.0 and the `postgres:16` Docker image, matching Organization's PostgreSQL constraint-test fixture. API and database tests run against isolated PostgreSQL containers with the real EF Core migrations applied. They cover case-insensitive name/SKU search, HTTP responses, role restrictions, tenant isolation, product lifecycle, price validation, database rounding/check constraints, and batch-code migration/uniqueness. SQLite and `EnsureCreated()` are not used.

### Running database tests

Start Docker Desktop (Linux containers) or another compatible Docker daemon before running `dotnet test`. Testcontainers pulls `postgres:16` if needed, starts temporary containers on automatically assigned ports, applies migrations, and removes the containers after the tests. The first run needs network access to pull the image. Tests fail if Docker is unavailable; they do not silently fall back to SQLite.

The development database remains separate: `docker compose up -d` starts `catalog_db` on port 5434 with a persistent volume, while Organization uses port 5433. Tests do not use or modify either development database.

The default local connection string matches Docker Compose: localhost port 5434, database `catalog_db`, and the Compose development account. Start the database before starting the API; the API applies migrations automatically. For hosted environments, supply `ConnectionStrings__Default` through environment configuration with the appropriate database credentials.

## Local commands

```bash
dotnet restore
dotnet build
dotnet test
docker compose up -d
```
