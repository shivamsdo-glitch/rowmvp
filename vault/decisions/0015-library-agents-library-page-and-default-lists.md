# ADR 0015 — Library: its own permission, the Agents list, the library-driven flag and default lists

**Status:** accepted (Phase 1.5 slice 3). Decisions marked **U** came from "Next requirements.docx" and the answers of 2026-10-19.

## The Library has its own page permission (U)
`PageKeys.Library` ("library"). The approved matrix gives **every role Read** on the Library (forms need its dropdown values) and **Admin full access**; before this slice it hung off the Administration page, so only administrators could read it. `Settings` and `Configuration` stay on Administration. Existing agencies get the new rows add-only from `TenantProvisioner` (read for every default role, full for Admin; custom roles start with none).

## Library-driven flag (U)
When a field is added the form asks **"Library-driven (people choose from a list defined here)"**, ticked by default. Ticked = a List field (a dropdown filled from the Library); unticked = a free-input field of the chosen type (Text, Number, Date, Yes/No). The flag is `IsLibraryDriven` in the API, derived from the type (List = library-driven), so there is one source of truth and no way for the two to disagree.

## Agents (U)
`LibraryAgent` per agency: name, **agent type** (a value of the `agent_type` list, stored as text so the record stays readable if the value is later switched off), company, phone, email, active. Rules: the type must be an active value of the Agent type list when set or changed; the same name + type + company (ignoring case and spacing) is entered once (unique index, race-safe); agents are **switched off, never deleted**; email is stored lower-case. `GET /api/v1/library/agents` (`agentType`, `activeOnly`) feeds the parcel Agents grid in slice 5. Contacts (people attached to a parcel with an address and an "is primary" flag) stay on the parcel and are not Library records.

## Default lists
Added add-only for new agencies (an existing agency keeps the lists it has; the provisioner adds missing *fields*, never values to an existing field):
- Values from the reference screens: **Owner type** (CAD Owner, Commercial Sign Owner, Fee Owner, Prior Owner, Vested Owner), **Relocation type** (Residential, Non-residential, Personal Property only), **Encumbrance type** (the 17 on the screen, without the test row), and **Agent type** now Row Designer, Surveyor, Title Researcher, Appraiser, Negotiator, Broker, Relocation, Land Planner, Community Engagement, Site Work.
- **Empty lists** for the agency to fill (U: "empty, except where the screens show values"): appraisal type, appraisal amount type, review type, review recommendation, mode of contact, easement type, offer type, settlement type, counteroffer response, JAT response, demo status, disposition type/method/status, displacee status/interest, relocation eligibility type, conveyance type, fund type, title company, address type.

## Test infrastructure
Integration fixtures start their host on the thread pool (`PgApi.InitializeAsync`): xUnit runs fixture set-up on a sync context with one slot per core, and blocking set-up in more fixtures than cores deadlocked the suite. The SecurityTests class has its own factory with a high write limit because it sends every write endpoint anonymously many times a minute.

## Verified by
API `LibraryAgentsApiTests` (every role, tenancy, race, validation), `LibraryApiTests`; frontend `agents.test.tsx`, `library.test.tsx`; Playwright `library-agents.spec.ts` and `library.spec.ts` (AT-AD-38, AT-X-37).
