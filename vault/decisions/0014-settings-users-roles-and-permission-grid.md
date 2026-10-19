# ADR 0014 — Settings: users, roles and the permission grid

**Status:** accepted (Phase 1.5 slice 2). Decisions marked **U** came from "Next requirements.docx" and the answers of 2026-10-19.

## What it is
A **Settings** item in the sidebar (Administration access only) with two tabs: **Users & Roles** and **Permissions** (U). Library stays its own item.

## Roles
- `Role.IsSystem` marks the eleven defaults (ADR 0013). A system role keeps its name (the product refers to roles by name) and cannot be deleted; its description and its grid can be edited. **Admin is locked**: no edits, always full access (U), so access can always be repaired.
- Agencies create **custom roles** (name unique ignoring case, 60 characters, letters/digits/space/-&/). A new role starts with **no access** (U). A custom role can be renamed, and deleted only when nobody is in it.
- Access is checked from the database by role name (`IPagePermissionService`), so custom roles work everywhere without code changes. The grid's columns are the page keys (`PageKeys`): "modules". Each later slice adds its sub-tab pages to `PageKeys` and the defaults; the grid grows by itself.
- **Create, update and delete require read** (enforced by the API and the grid). The older Basic/Standard/Pro labels are kept only as a derived label on each row.

## Users
- Settings creates a user with a username, name, email, **one role** (U) and a password set by the administrator. Username and email are unique across all agencies (sign-in needs no agency hint). Editing changes name, email, role, active flag; a password can be reset. A role change reaches the person's token at their next sign-in.
- You cannot deactivate yourself; the **last active administrator cannot be demoted or switched off**.
- Passwords are hashed (PBKDF2); neither a password nor its hash is written to the audit trail.

## Not in this slice
Email invitations for new users (the existing invite flow stays API-only), self-service password change, MFA.

## Verified by
API `SettingsApiTests` (real Postgres: every role, tenancy, locks, validation, audit), frontend `settings.test.tsx`, Playwright `settings.spec.ts` (AT-AD-30..37).
