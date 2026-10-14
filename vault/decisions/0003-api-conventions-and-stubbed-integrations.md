# ADR 0003 — API conventions and stub-first integrations

**Status:** accepted (Phase 0).

## API conventions
- **Routes are versioned: `/api/v1/...`.** Behind the Aurigo gateway the same routes appear under a gateway-added prefix (`/api/row-management/v1/...`); the app itself always serves `/api/v1`. Swagger documents `v1` with a Bearer scheme; it is on in Development and behind `Swagger:Enabled` elsewhere.
- **Errors are RFC 7807 ProblemDetails** from one `ExceptionHandlingMiddleware`: `NotFound` 404, `Forbidden` 403, `AuthenticationFailed` 401, `Conflict` 409, other `DomainException` 400, FluentValidation 400 with field errors, concurrency 409, rate limit 429, anything else a generic 500 whose cause is only in the log. `title` is a stable machine code (`invalid_credentials`, `validation_failed`, ...); every response carries a `traceId`.
- **Validation only at the pipeline boundary** (FluentValidation via MediatR `ValidationBehavior`); the domain enforces invariants, not input shape.
- **DTOs are never entities**; mapping lives in `Application/Mappers`.
- **Logging:** Serilog to console, enriched with `TenantId`, `UserId`, `RequestId`. Response compression: Brotli then Gzip.
- **Startup:** migrate the database (fail fast); in Development only, seed the demo tenant, the six roles and one inactive user per role.

## External integrations: stub-first
Anything ROW will one day call (document management, notifications, and later a finance system) is an interface in `Application/ExternalServices/` with a `Stub*Client` in `Infrastructure/ExternalClients/` registered in `DependencyInjection.cs`. **No real HTTP client to any outside system exists in the MVP.** Adding one means adding a client class and swapping one registration; callers do not change.

Deliberately not carried over from the reference project: ArcGIS, Aurigo Plan/Build, Primavera, the AI agent client and the Essentials public API client (they need credentials this project does not have).

## Consequences
- The stubs keep realistic behaviour (`StubDocMgmtClient` really stores and returns bytes for the life of the process), so demos work; nothing survives a restart.
