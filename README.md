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

## Local commands

```bash
dotnet restore
dotnet build
dotnet test
docker compose up -d
```

