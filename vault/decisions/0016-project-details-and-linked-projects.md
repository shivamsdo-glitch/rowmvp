# ADR 0016 — ROW project details, the link to Planned/Build projects, and Add parcel on a project

**Status:** accepted (Phase 1.5 slice 4). Decisions marked **U** came from "Next requirements.docx" and the answers of 2026-10-19.

## The project record gains the fields of the ROW project details screen (U)
`Phase` (free text; **required when a project is created**, the form starts at "Phase 1"; a project made before the field existed can be edited without one), `Location`, `ProjectManagerUserId` + `ProjectManagerName`, `DocumentFolderStructure`, `DocumentProperties`, `ConstructionProjectManager`, `RightOfWayClearDate`. All are optional in the API; the existing fields keep their names (Letting date, Construction start/end are the screen's "Projected construction start/completion").
- **Project manager** is an active person of the agency, chosen from `GET /api/v1/projects/managers` (needs Read on Projects). The name is kept as it was when chosen, so the project stays readable if the person is later switched off. Choosing someone who is not active in this agency is a 400 `unknown_project_manager`.
- **Document folder structure** and **Document properties** are Library lists (empty by default, ADR 0015); there is no document platform behind them (CLAUDE.md scope), so they are just values.
- **Funding information** is read-only on the overview: **Board approved funds** = the annual budgets added up; **Cost estimate** = the latest estimate version; **Actual cost** = 0 until payments exist. "Funding amount" and "Design estimate" stay as the editable agency figures.
- **Right of way clear date** is only a date here: clearance and certification (Diagram 10) are v2.

## Link to a Planned or Build project (U)
The Planned Projects and Build Projects modules belong to the wider platform. CLAUDE.md forbids a real client without a decision, so `IProjectCatalogClient` has only `StubProjectCatalogClient` (seven sample records). The project stores `LinkedSource` ("Planned" or "Build"), `LinkedProjectNumber` and `LinkedProjectName` (as they were when linked). `GET /api/v1/projects/catalog?source=&search=` feeds the picker. The link is validated against the catalog (400 `unknown_linked_project`); source and number come together or not at all. Clearing the source clears the link.

## Add parcel on a project (U)
The project page header has **Add parcel** (shown when the role may create parcels), opening the parcel form for that project; saving opens the new parcel. The form is the existing parcel form; slice 5 replaces its fields with the full parcel form.

## Compatibility
The new members of the create/update commands and `ProjectDto` are trailing optional parameters, so the import, the earlier tests and older clients keep working; an update that omits them clears them (the form always sends them all).

## Verified by
API `ProjectDetailsApiTests` (every role on the two lookups, validation, tenancy of managers, funding block), unit `ProjectDetailsTests`, frontend `project-details.test.tsx`, Playwright `project-details.spec.ts` (AT-PM-40..42).
