# Phase 5 — Dashboard, Seed Data & Demo Script

**Goal:** a demo-ready MVP. Reporting platform (Diagram 13) stays deferred; this is a single purpose-built dashboard.

## Scope
- **Dashboard** (home route): project selector; parcels by status (bar/donut); pipeline funnel Title → Appraisal → Negotiation → Closed; budget vs. committed vs. paid; overdue milestones/statutory deadlines (from Phase 1 calcs); pending approvals for the current role; recent activity from audit log. Drill-through to filtered parcel lists. (Lightweight subset of ROW-188/199 ideas; read-only queries in `Application/Dashboard/`.)
- **Seed data** (`Infrastructure/Seed`, idempotent, Development only): 1 tenant; 6 users (one per role, dev-token ready); 2 projects (one federal-aid, one local); ~30 parcels spread across every pipeline stage incl. a partial take, a remnant, an impasse, a donation; owners/tenants/contacts; title defects; appraisals with a rejected→revised case; offers at different approval-threshold bands; one fully closed parcel with payment. Config: approval chain thresholds, statutory deadline rules.
- **Demo script** `vault/demo/Demo-Script.md`: 15–20 min walkthrough by role (PM creates project/parcel → Title Examiner package → Appraiser/approver JC → Negotiator offer to closing → Finance payment → PM dashboard), with expected screens and "gotcha" moments (threshold escalation, step-gate block, confidential title field hidden).
- Polish: empty/loading/error states, role-based nav, print view for parcel synopsis (009), seed reset command, README quick start (`docker compose up`, `dotnet run`, `npm run dev`).
- Quality gate: full test pass, one Playwright-free smoke checklist in the demo doc, traceability table (ROW-ID → phase → test) at `vault/phases/Traceability.md`.

## Exit checklist
- [ ] Fresh clone → documented 3 commands → seeded demo running
- [ ] Demo script executed end to end without manual DB edits
- [ ] Traceability table complete; deferred IDs listed with reason
