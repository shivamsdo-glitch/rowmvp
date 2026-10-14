# ADR 0004 — Page RBAC, seed runner, document attachments, saved filters

**Status:** accepted (Phase 0).

## Page-level RBAC
- `RolePagePermission` (role, page key, Create/Read/Update/Delete, template label Basic/Standard/Pro/Custom) per tenant role. Basic = read, Standard = create/read/update, Pro = full CRUD; edited flags are labelled Custom.
- Page keys are a fixed list in `PageKeys` (13 pages). Defaults live in `DefaultRolePermissions`; `TenantProvisioner` adds missing roles/rows and **never overwrites** rows an admin changed, so new pages back-fill safely.
- Effective permission = union across the caller's roles. Role names come from the JWT; permission rows are read from the database per request (no stale token claims).
- Controllers still use role `[Authorize]` where a whole endpoint is role-gated; handlers use `IPagePermissionService` for page-scoped actions.

## Seed runner
`SeedRunner` runs ordered `ISeedStep`s after migrations and fails fast. Steps: demo tenant (10), default RBAC (20, every environment, all active tenants), demo users (30), demo data (40, scaffold only until Phase 1 entities exist). Anything flagged development-only is skipped outside Development.

## Document attachments
- One generic flow for every module: `Document` metadata row (`EntityType`, `EntityId`) + bytes in the external store (stub today).
- `AttachmentTargets` maps entity type → owning page; permissions: list/download = Read, attach = Update, delete = Delete on that page.
- Safety: extension allowlist, content type chosen by the server, file name sanitised, SHA-256 recorded, size measured on the actual bytes, downloads are `attachment` + `nosniff`.
- Delete is soft (row and bytes kept for audit). Existence of the target entity is not verified until Phase 1+ entities exist.

## Saved filters
`SavedFilterProfile` (user, page key, name, filter JSON object ≤16 KB). Strictly per user; another user's id returns 404; names unique per user/page, case-insensitive.

## Consequences
- Documents are buffered in memory (25 MB cap); revisit with streaming when a real store is added.
- Notifications UI, tenant GIS layers and any map work remain deferred to v2.
