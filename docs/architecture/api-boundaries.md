# API Boundaries

Engine endpoint files are HTTP adapters. They should validate route/query/body input, call a service or repository, and return the existing response shape.

## Rules

- New endpoint files must not inject or use `IDatabaseConnection` directly.
- New endpoint files must not contain SQL statements or command construction.
- SQL belongs in Storage repositories or focused API read services when the projection is API-specific.
- Repository and read-service data access should use `IDatabaseConnection.CreateConnection()` and dispose the connection with `using`.
- Web/Dashboard code must not reference `MediaEngine.Storage` implementation types and must not contain SQL.
- Domain remains independent of API, Web, Storage, Providers, Ingestion, Processors, and AI.

## Adding A New Endpoint

1. Define or reuse the DTO/contract.
2. Put persistence in a repository or read service.
3. Inject that service into the endpoint handler.
4. Preserve cancellation token flow.
5. Add endpoint behavior tests and service/repository tests for non-trivial queries.

The current direct database endpoint allowlist is legacy debt. It should shrink over time and must not be treated as a pattern for new code.
