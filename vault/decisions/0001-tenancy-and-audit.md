# ADR 0001 — Tenancy and audit

**Status:** accepted (Phase 0). **Adapted from:** the sibling Aurigo.AssetMaintenance pattern, with the changes noted below.

## Decisions
1. **Every aggregate root and child entity derives `EntityBase`** (`Id`, `TenantId`, audit columns). Every `ITenantOwned` type gets an EF global query filter `TenantId == CurrentTenantId`, applied by reflection in `RowDbContext.OnModelCreating`, so a new entity cannot forget it.
2. **`CurrentTenantId` is an instance property on the DbContext**, not a static. EF parameterises instance members per query; a static is constant-folded into cached plans and leaks across tenants.
3. **Fail closed.** The tenant comes from the JWT `tenant_id` claim. No claim means `Guid.Empty`, which matches no row. The interceptor also refuses to save a tenant-owned entity that has no tenant, and refuses to move an entity between tenants.
4. **Only anonymous auth flows call `IgnoreQueryFilters()`** (login, forgot-password, activate, reset). They have no tenant yet; email is globally unique so it identifies the tenant. Anonymous inserts (an activation token) pass `TenantId` explicitly rather than relying on a context.
5. **`Tenant` itself is not tenant-owned** (it is the tenant) and is not filtered.
6. **Auditing is a `SaveChangesInterceptor`**, never handler code. It stamps `TenantId`/`CreatedBy`/`UpdatedAt`, and writes `AuditLog` rows: one per create/delete (JSON snapshot), one per changed field on update. `AuditLog` is tenant-owned but not an `EntityBase`, so it is never audited itself.
7. **Secrets are redacted in audit rows** (`PasswordHash`, `TokenHash`): the change is recorded, the value never is. The "did it change" comparison uses the real values and redacts afterwards.

## Differences from the reference
- Delete is audited (the reference only audited create and update).
- Sync `SaveChanges` is intercepted as well as async.
- Secrets are redacted (the reference serialised every scalar, including password hashes).
- The no-tenant and tenant-move guards are new.
- Columns use snake_case (`EFCore.NamingConventions`), so the column is literally `tenant_id`.

## Consequences
- Handlers contain no audit or tenant code. Tests that need another tenant create a second context with a different `ICurrentTenant`.
- Unique email across the platform means one email cannot belong to two agencies. Revisit if a user must span tenants.
- Concurrent consumption of the same activation token is not atomic (read, mark, save). The token is a 256-bit secret, so this is accepted for the MVP.
