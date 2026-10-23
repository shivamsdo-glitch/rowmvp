# 0029 — Related parcel tabs share one tab as sections (21 tabs → 11)

**Status:** accepted (user decision, 2026-10-22). **Context:** the parcel strip had 21 tabs and wrapped onto three rows.

**Decision.** Related tabs become stacked *sections* of one tab. Every section keeps its own page key, ADR and acceptance IDs; a role sees a section only if it can read that page, and a tab shows if any of its sections shows. No backend, page-key or permission-matrix change.

| Tab | Sections |
|---|---|
| Summary | Summary, Team, Parcel location, Notes |
| Ownership interests | Ownership interests |
| Negotiation | Negotiation, Diary, Contact log |
| Reviews & site visits | Reviews & reports, Site visits & notice |
| Interests & easements | Interests, Easements |
| Title | Title encumbrances, Title and closing |
| Appraisal | Appraisal |
| Workflow & milestones | Workflow, Milestones & deadlines |
| Relocation | Relocation, Questionnaire |
| Condemnation | Condemnation |
| Property management & disposition | Property management & disposition |

**Consequences.**
- Old links still work: `?tab=easements` opens *Interests & easements* and scrolls to the section; `&record=` opens that record on its own page (ADR 0028) in its section. Work-screen *Open* links are unchanged.
- Each section is `<section data-section="<old key>">`; tests check presence with that attribute.
- The Diary's add form is now named "Log a diary contact" so it differs from the Contact log's "Log a contact" on the same tab.
- Diary and Contact log now sit side by side and overlap in purpose; merging their data is not decided.
