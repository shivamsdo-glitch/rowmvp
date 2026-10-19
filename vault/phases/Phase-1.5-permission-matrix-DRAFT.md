# Phase 1.5 — Default permission matrix (DRAFT for approval)

Source: "Next requirements.docx" (role task lists and flow diagrams). Letters are the Create / Read / Update / Delete boxes ticked by default; `-` is no access. An administrator can edit every cell in Settings > Permissions; re-seeding only adds missing rows and never overwrites edits. Admin is locked to full access.

Rules applied: a role that does the work gets CRUD (delete only where it owns the record); a role that reviews gets R+U; everyone else gets R on the things they need to see; nobody gets access that the document does not imply.

| Module | PM | Designer | Design Approver | Surveyor | Title Researcher | Appraiser | Appraisal Approver | Negotiator | Relocation Agent | Finance | Admin |
|---|---|---|---|---|---|---|---|---|---|---|---|
| Dashboard | R | R | R | R | R | R | R | R | R | R | CRUD |
| Projects | CRUD | R | R | R | R | R | R | R | R | R | CRUD |
| Parcels (core form, agents, contacts, interests) | CRUD | CRUD | RU | R | R | R | R | R | R | R | CRUD |
| Ownership Details | R | CRUD | R | R | RU | R | R | CRU | R | R | CRUD |
| Parcel Location | R | CRUD | R | CRU | R | R | R | R | R | R | CRUD |
| Contact Log | R | CRU | R | CRU | CRU | CRU | R | CRUD | CRUD | R | CRUD |
| Easements | R | CRU | R | CRUD | R | R | R | R | R | R | CRUD |
| Title Encumbrances | R | R | R | R | CRUD | R | R | R | R | R | CRUD |
| Title and Closing | R | R | R | R | CRU | R | R | CRU | R | R | CRUD |
| Appraisal | R | R | R | R | R | CRUD | RU | R | R | R | CRUD |
| Negotiation | R | R | R | R | R | R | R | CRUD | R | R | CRUD |
| Questionnaire | R | R | R | R | R | R | R | R | CRUD | R | CRUD |
| Relocation | R | R | R | R | R | R | R | R | CRUD | R | CRUD |
| Condemnation | R | R | R | R | R | R | R | CRU | R | R | CRUD |
| Property Mgmt & Disposition | CRUD | R | R | R | R | R | R | R | R | R | CRUD |
| Payments | R | - | - | - | - | - | - | R | R | CRUD | CRUD |
| Library (values and people) | R | R | R | R | R | R | R | R | R | R | CRUD |
| Settings (Users & Roles, Permissions) | - | - | - | - | - | - | - | - | - | - | CRUD |

## Judgement calls to confirm
1. **Design Approver** gets `RU` on Parcels so it can set the "Approved for acquisition" flag; it cannot create or delete parcels. Only this role (and Admin) may change that flag.
2. **Appraisal Approver** gets `RU` on Appraisal (review fields, Approved Just Compensation); the Appraiser cannot edit those fields. This is enforced as field-level rules in the Appraisal handler, on top of the grid.
3. **Condemnation** has no owner in your document. I gave it to the Negotiator (`CRU`) because the diagram hands off from a declined offer.
4. **Property Management & Disposition** also has no owner. I gave it to the Project Manager.
5. **Finance** only touches Payments; it reads everything else.
6. **Delete** is limited to each tab's owning role and Admin. A deleted record is soft-deleted (audited).
7. **Library read** is open to everyone because forms need the dropdown values; only Admin edits.
8. The Project Manager cannot edit Title, Appraisal, Negotiation or Relocation records, only view them ("view every project and parcel and every subsequent detail").
