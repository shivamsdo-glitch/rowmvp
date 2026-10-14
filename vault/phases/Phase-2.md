# Phase 2 — Title & Survey Package

**Source diagram:** Diagram 2. **Primary roles:** TitleExaminer (+ ProjectManager read).

## Scope
**Title:** chain of title / historical ownership (023), defect & encumbrance tracking with alerts (024), title commitment details (025), link/search title documents — deeds, plats, surveys, reports (026; upload via `IFileStorage`, metadata search only), title versioning + update flags across lifecycle (027), confidentiality by role (028; field-level hide for non-title roles), land-type classification (029), title report templates/front sheet (032; simple templated HTML/PDF), title order tracking PO/order/received/QC dates (242), title company invoice approval + payment hand-off flag (243), existing ROW verification (244).

**Survey:** survey request tracking (035), survey data & CAD files (034; file attachments + metadata only, no CAD parsing), survey metadata & monumentation records (036), restricted editing of survey plans by role (039), ROW plan request tracking & workload (030), external title examiner contract tracking (031 — contract record only, no vendor portal).

**Package:** GIS document QC checklist for legals/sketches/boundary survey (245), legal & sketch review loop with initials (246), **appraisal package assembly** (247) — the hand-off gate to Phase 3 (package complete → parcel eligible for appraisal request).

**Frontend:** parcel "Title & Survey" tab group, defect board, order tracker, package checklist, document upload/list with versions.

## Requirements
ROW-023–032, 034–037*, 039, 242–247. *037 (survey software interoperability) deferred — no source evidence; 036 kept as plain records.

## Deferred (in range number-wise, but belong to deferred diagrams)
033 hearing notifications (D12), 038 field-book migration (D11), 040 public inquiry/statewide contracts (D7/D12), 041 survey reports (D13).

## Exit checklist
- [ ] Order title → receive → QC → log defects → clear → package complete, per parcel
- [ ] Non-title roles cannot see confidential title fields (integration test)
- [ ] Only roles allowed can edit survey plans
- [ ] Incomplete package blocks appraisal request (gate test)
