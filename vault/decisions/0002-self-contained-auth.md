# ADR 0002 — Self-contained authentication

**Status:** accepted (Phase 0).

## Decisions
1. **The app issues and validates its own JWTs** (HS256). No call to any external Aurigo identity service.
2. **Claims:** `sub` (user id), `email`, `tenant_id`, `jti`, and one `role` claim per role. `MapInboundClaims = false` so names stay exactly as issued. Roles: `ProjectManager`, `TitleExaminer`, `Appraiser`, `Negotiator`, `Finance`, `Admin` (`RoleNames`), used as `[Authorize(Roles = RoleNames.X)]`.
3. **Signing key:** from configuration `Jwt:SigningKey` (env var `Jwt__SigningKey`). A fixed dev-only fallback exists in **Development only**; any other environment without a key refuses to start (checked at startup, not on first request). Minimum 32 bytes always.
4. **`GET /api/v1/auth/dev-token`** issues a token for chosen roles with no password. It returns **404 outside Development**, indistinguishable from a missing route.
5. **Account flow:** admin-provisioned users have no password until they activate. `login`, `activate`, `forgot-password`, `reset-password`, `me`. Activation/reset tokens are single-use, 256-bit, stored as SHA-256 hashes, expire (invite 7 days, reset 1 hour), and using one commits atomically with the password change.
6. **Passwords:** PBKDF2-SHA256, 100,000 iterations, per-hash salt, constant-time compare, 8 to 128 characters (the upper bound stops PBKDF2 being used as a CPU sink).
7. **Anti-enumeration:** unknown email and wrong password return the same error and cost the same PBKDF2 work (a dummy hash is verified); `forgot-password` always answers identically. Account-state errors (locked, tenant inactive) are only revealed after the password verifies. The one deliberate leak is `activation_required` for a never-activated account, which the UI needs.
8. **Email:** `IAccountMailer` + `IEmailSender`. The only sender is `ConsoleEmailSender`, which logs the link in Development and only recipient/subject elsewhere, so live links never reach production logs. No SendGrid wiring.
9. **Rate limits:** the anonymous auth endpoints are limited per IP (default 10/min); all writes (POST/PUT/PATCH/DELETE) are limited per user (default 120/min); reads are unlimited. Configurable under `RateLimiting`.

## Not included (v2, vault Diagram 11)
SSO/OIDC, MFA, lockout after failed attempts, refresh tokens, token revocation, per-role page permissions.

## Consequences
- Behind a reverse proxy the per-IP limit needs forwarded-headers configuration, or all clients share one bucket.
- Tokens cannot be revoked before they expire (8 hours by default).
