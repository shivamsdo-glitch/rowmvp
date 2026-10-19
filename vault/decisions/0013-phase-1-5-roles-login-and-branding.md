# ADR 0013 — Phase 1.5: eleven roles, username sign-in, Masterworks branding

**Status:** accepted. Decisions marked **U** were made by the user on 2026-10-19 from "Next requirements.docx" (all recommended defaults accepted, permission matrix approved as drafted in `vault/phases/Phase-1.5-permission-matrix-DRAFT.md`).

## Roles (U)
The six locked roles became eleven: `ProjectManager`, `Designer`, `DesignApprover`, `Surveyor`, `TitleResearcher`, `Appraiser`, `AppraisalApprover`, `Negotiator`, `RelocationAgent`, `Finance`, `Admin`. `TitleExaminer` is renamed `TitleResearcher` (the title work); `Surveyor` is new. A migration renames existing `TitleExaminer` rows so edited permissions survive. Designer/DesignApprover and Appraiser/AppraisalApprover are separate roles so approvals are two-person (ADR 0012). Slice 2 turns the fixed list into agency-editable roles with a create/read/update/delete grid; until then the new roles get defaults on the existing page keys.

## Sign-in (U)
- `AppUser.Username` (lower-cased, unique platform-wide, defaults to the email). `POST /api/v1/auth/login` takes `{username, password}`; the account email works as a username too. Wrong password and unknown username are indistinguishable (same 401, same PBKDF2 cost).
- **Demo sign-ins:** eleven named users (`RoleNames.DemoLogins`) with the shared password `qwertyuiop` are seeded by `DemoUsersSeedStep`, which is **Development-only**. Nothing outside Development ever gets a known password. The older not-yet-activated `{role}@row.local` users are retired by the same step.
- The dev role picker stays on the sign-in page in dev builds only (`import.meta.env.DEV`), because the E2E suites and the stable-user-per-role rule (ADR 0002) depend on it. The landing page is the Masterworks Right of Way Management page with Username and Password.

## Branding (U)
`BRAND.productName` and the tab title are "Masterworks Right of Way Management". No logo was supplied; the text wordmark stays.

## Scope notes
- v2 deferrals for condemnation, relocation, property management/disposition and GIS parcel location are lifted for *forms with CRUD, attachments and role access only* (U); their workflow automation stays out.
- Phase 1.5 delivers the screens and tables; Phases 2-4 later add rules (offers, approval chains, just compensation) on the same tables.

## Verified by
Unit (`AuthValidatorTests`, `DefaultRolePermissionsTests`), API (`NamedLoginTests` x11, `AccountFlowTests`, `SchemaAndContractTests` for 11 roles), frontend (`login.test.tsx`, `roles.test.tsx`), Playwright (`login.spec.ts`: AT-X-42..45; every role-matrix spec now runs for eleven roles).
