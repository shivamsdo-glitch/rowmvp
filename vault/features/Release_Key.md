# Release Key

ROW BUILD ROADMAP – RELEASE KEY

Release and Build sequence are on the 'ROW Features' sheet (columns Z-AC). They are judgment assignments, not formulas. Each release is a thin, usable slice; sequence is unique (1-323).

| Release | Theme | # features | First seq | Last seq | Release in version 2 file | Why it sits here (basis) |
| R01 | Platform foundation & UI standards | 13 | 1 | 13 | R01 | Everything else is built on hosting, environments, the shared data model, role/field security, audit and the UI standards (accessibility, browsers, usability) that every later form must follow. |
| R02 | Project & parcel core | 11 | 14 | 24 | R02 | First business slice: a ROW project and its parcels (System Design 3a: Project and Parcel Management comes first), plus the document repository, workflow engine and tasks every later module uses. Parent-tract and tax-parcel link (ROW-233) attaches to the parcel record. |
| R03 | Owners, stakeholders & parcel team | 9 | 25 | 33 | R03 | Owners, interests, contacts and team roles hang off the parcel (System Design: M1 -> M2). Demographics and tax data are captured here so payments and Title VI reporting have the data later. |
| R04 | Project funding, parcel depth, acquisition plan & map viewer | 12 | 34 | 45 | R04 | Deepens the parcel and project records (funding, estimates, budgets, acquisition plan, interests by type, grouping, split/combine, batch updates) and adds the map viewer that appraisal, utilities and inventory reuse. |
| R05 | Templates, correspondence & e-signature | 4 | 46 | 49 | R05 | Templates, mail merge and e-signature are needed by hearing notices and title reports (R07), notices to owners (R06), offers (R11) and relocation notices (R18), so they come first. |
| R06 | Acquisition rules, authorization, compliance & alerts | 16 | 50 | 65 | R06 | System Design: the Acquisition Process and Compliance Engine governs Acquisition and Negotiation. ROW authorization, site visit, Notice to Owner and the plan-review gate belong to the pre-acquisition steps in Diagram 1. Alerts are built here because deadline and next-action reminders depend on them. |
| R07 | Title & abstraction | 15 | 66 | 80 | R07 | Title and survey both feed appraisal (System Design: M3 and M4 -> M5). Title ordering is built before title research. Uses document versioning, role-based confidential access (R01) and mail merge (R05). |
| R08 | Survey, plans & appraisal package | 9 | 81 | 89 | R08 | Survey data and CAD files feed appraisal (System Design: M4 -> M5). GIS quality check, legal/sketch review and appraisal package assembly close out Diagram 2 and hand the parcel to appraisal. |
| R09 | Appraisal core  |  Utilities core (parallel track) | 20 | 90 | 109 | R09 | Appraisal intake needs the assembled package (R08). Utilities run in parallel with acquisition per your direction; the utilities core needs only project, parcel, tasks and documents. |
| R10 | Appraisal depth  |  Utilities agreements & reimbursement (parallel track) | 20 | 110 | 129 | R10 | Appraiser management, report writing, specialty reports and comparable-sales data build on the appraisal core and the R04 map viewer. On the utilities track: prior rights, cost authorization, relocation notices, estimate review, Buy America and milestones build on the utilities core and its agreements. |
| R11 | Pre-offer review, offers & negotiation | 13 | 130 | 142 | R11 | Offers start only after just compensation is approved (R09). Management review, funding check and approval thresholds come before the first offer. Impasse data created here feeds condemnation (R21). |
| R12 | Agreements, easements & alternative acquisition paths | 9 | 143 | 151 | R12 | Agreement approval, subordination letters, easements, key turnover and environmental site assessments follow the settlement (R11). Donation, developer-conveyance and early-acquisition paths reuse the configurable acquisition workflow (ROW-071). |
| R13 | Payments & compensation (tracked in-system) | 14 | 152 | 165 | R13 | Payments follow agreements and settlements (System Design: M6 -> M9). Per your direction, payments are tracked in the system first; finance-system integration is R33. Request for Funds, fund reservation, title-company invoices and possession/incentive payments sit on the payment engine. |
| R14 | Closing, recording & parcel closure  |  Utilities delivery (parallel track) | 6 | 166 | 171 | new | Closing claims, closing package, recording and the Parcel Closed status finish the standard acquisition. On the utilities track, notice to proceed, change orders and invoicing need the payment engine (R13); utility clearance runs in parallel. |
| R15 | Search, dashboards & core reports | 9 | 172 | 180 | R13 | Core operational reporting over the data built in R02-R14: search, dashboards, ad-hoc reports, exports, status and acquisition reports. |
| R16 | Data import & legacy migration | 5 | 181 | 185 | R14 | Per your direction, migration and import tools are sequenced before the first customer go-live. They need the project, parcel and owner entities (R02-R03) to map into. |
| R17 | Production readiness & go-live support | 8 | 186 | 193 | R15 | Security controls, backup/DR, monitoring, performance, UAT/project delivery, training and support. Earliest sensible customer go-live point (standard acquisition from project through closing); see Assumptions. |
| R18 | Relocation case, planning & notices | 10 | 194 | 203 | R16 | Relocation follows the signed agreement (System Design: M6 -> M7). Planning study, General Information Notice, advisory interview, case, questionnaires, claims and notices. |
| R19 | Replacement housing, eligibility & payment calculations | 8 | 204 | 211 | R16 / R17 | Replacement-housing study, comparable-dwelling notice, decent-safe-sanitary inspection, eligibility exceptions and the payment calculations. |
| R20 | Relocation payments, appeals & reports | 5 | 212 | 216 | R17 | Last-resort housing, advance payments and payment approval need the R13 payment engine and the R19 calculations. Appeals and relocation reports close the slice. |
| R21 | Condemnation case & approvals | 7 | 217 | 223 | R18 | Triggered by impasse (System Design: M6 -> M8). Case management, approval chain, order-of-taking checklist and the document set. |
| R22 | Condemnation possession, deposits & court payments | 7 | 224 | 230 | R18 | Order of possession, court deposit, owner withdrawal, settlement or stipulated judgment, trial report and court payments (need the R13 payment engine). |
| R23 | Site clearance & demolition | 7 | 231 | 237 | new | After acquisition, relocation and (where needed) condemnation, the site is cleared: vacancy verification, hazardous-materials assessment, sale of improvements or demolition, clearance contract, environmental commitments and construction-ready documentation (Diagram 10). |
| R24 | ROW certification & release to construction | 6 | 238 | 243 | new | Certification needs parcel closure (R14), utility clearance (R14) and site clearance (R23): status matrix, per-parcel checklist, certification document, internal and federal approval, then release to construction (Diagram 10). |
| R25 | Property inventory & handoff | 9 | 244 | 252 | R19 | Property management begins after acquisition (System Design: ACQ -> PROP). Inventory is the base for leases, inspections and disposal; activation follows the handoff. |
| R26 | Leases, occupancy & use agreements | 13 | 253 | 265 | R20 | Depends on inventory (R25) and payments (R13). Rent-back (ROW-292) overlaps lease-back (ROW-083), so both are delivered together. Property-management valuations are placed here because they relate to leased property. |
| R27 | Inspections (web) & mobile field access (parallel) | 5 | 266 | 270 | R21 | Per your direction, web inspection and mobile/field access are built in parallel. Photo storage is included because inspections need it. Improvement inventory and vacant-property security follow. |
| R28 | Surplus declaration & disposal approval | 6 | 271 | 276 | R22 | Review committee, declaration, legal description and sketch, minimum-bid rules, environmental clearance and board approval precede any disposal. |
| R29 | Disposal & sales | 6 | 277 | 282 | R22 | Disposal methods and sales (ROW-301 and ROW-140 overlap and are delivered together), then compliance, financials and inventory reports. |
| R30 | Regulatory reporting & compliance certification | 7 | 283 | 289 | R23 | FHWA, Title VI and IRS 1099 reports need data from payments, relocation and owners. Federal reimbursement billing needs authorized amount and federal share (ROW-238) and expenditure tracking (ROW-113). Per your direction, certifications are not a go-live gate. |
| R31 | Advanced analytics & report portal | 5 | 290 | 294 | R24 | KPIs, forecasting, charts, report portal and ODBC access, built on top of the R15 reporting layer. |
| R32 | External portals, public access & records | 8 | 295 | 302 | R25 | Per your direction, external portals come after the internal workflows they expose. Retention and public-records requests are included. |
| R33 | Finance & enterprise integration | 7 | 303 | 309 | R26 | Per your direction, in-system payment tracking (R13) comes first. The open API is built first, then finance and enterprise connections. ROW-111 and ROW-211 overlap and are delivered together. |
| R34 | Document, mail, map & survey integrations | 9 | 310 | 318 | R27 | External services (document system, Outlook, USPS, external map providers, survey software, relocation calculators, market rent) are connected after the internal features they extend. |
| R35 | Rail & transit (NH-specific) | 5 | 319 | 323 | R28 | Per your direction, NH-specific rail items go last. NH has not defined several of its named systems (see Open Questions #5-6). |
|  | Total | 323 |
Rules applied

1. Dependency first: a feature is never built before something it needs. Every 'Depends on' ID has a lower build sequence than the feature itself.

2. Within the same dependency level, existing features are ordered by Consensus tier (sources rating Must, then number of sources). The 91 new features (ROW-233 to ROW-323) have no tier, so they follow existing features at the same level and are then ordered by ID.

3. Nothing is treated as already built: Aurigo coverage columns were NOT used for ordering (your direction: fresh start, existing items are to-do).

4. Thin slices: each release is a coherent set a user group can use. Process order follows the System Design arrows and the numbered process diagrams (project/parcel -> owners/title/survey -> appraisal -> acquisition -> payments -> relocation / condemnation -> clearance -> certification; property management after acquisition).

5. Parallel tracks (your direction): Utilities run alongside appraisal and payments (R09, R10, R14); web inspection and mobile/field access are delivered together (R27).

6. Last (your direction): external portals (R32), integrations (R33-R34) and NH-specific rail items (R35). Payments are tracked in-system first (R13); finance integration is R33.

7. Overlapping features are delivered in the same release, not merged or flagged: ROW-111 and ROW-211 (R33); ROW-058, ROW-107, ROW-108 and ROW-243 (R13); ROW-083 and ROW-292 (R26); ROW-140 and ROW-301 (R29); ROW-139 and ROW-296 (R28).

8. Not used for ordering (your direction): deadlines and security-certification gating. Certifications (ROW-217) sit in R30.

Assumptions to confirm

A. First customer go-live candidate = after R17 (standard acquisition from project through closing and parcel closure, with data migration and production readiness). Relocation, condemnation, clearance, certification and property management follow. You did not specify a go-live point; move it if needed.

B. System Design shows title and survey feeding appraisal, so appraisal (R09) comes after R07-R08 even though that delays the first end-to-end slice.

C. ROW-030 (ROW plan requests) is a Title & Abstraction item but opens R07, matching Diagram 2.

D. Roles marked 'inferred' in the process diagrams are unconfirmed (your direction: not blocking).

E. ROW-310 (railroad project type and crossing funding, from NDOT) is placed with utilities in R10; ROW-145 (NH railroad crossing agreements) stays in R35. They overlap only partly, so they were not placed together.

F. ROW Certification (R24) is placed before property management (R25) because certification is a pre-construction step in the sources (Lifecycle Stage 9), while property management handles acquired property afterwards (System Design: ACQ -> PROP). Move it if your lifecycle differs.

G. Release numbers changed from version 2 because new features needed new slices. Column F shows the version 2 release for each current release.

