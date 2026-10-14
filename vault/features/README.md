# Feature workbook (ROW_Module_Features v3)

Source: `ROW_Module_Features_version_3.xlsx` (copied here unchanged as `ROW_Module_Features_v3.xlsx`). CSV/MD dumps are generated from it with formulas evaluated (cached values), so they are greppable. If the workbook changes, regenerate; don't hand-edit the dumps.

| File | Content |
|---|---|
| `ROW_Features.csv` | 323 features (ROW-001..323): domain/category, description, **consolidated priority**, #sources, consensus tier, per-source codes (NH, ID, NM, WA, ATX, OSC), regulations, named systems, Aurigo coverage, notes, **Release (R01–R35), Build sequence (1–323), Depends on** |
| `Release_Key.md` | Build roadmap: release themes, ordering rules, assumptions (go-live candidate = after R17) |
| `Traceability.csv` | ~960 verbatim source extracts behind each feature (source doc, location, source's own priority) |
| `Sources.csv` | The source documents and how priority was read |
| `City_County_Features.csv` | CTY-xxx requirements for *use of* public ROW (permits etc.). **Out of scope for the ROW module; reference only** |
| `Workbook_README.md` | How to read priority, tiers, coverage columns |

Diagram previews (PNG) are in `../previews/`.

## Reading notes
- Priority = *highest* rating any source gave ("at least one source requires it"). NH and Idaho rate nearly everything Must; use Consensus tier for weight.
- ROW-233..323 have no RFP source columns (priority "Stated – no priority", Tier 6) — they come from SOPs/manuals; evidence is in the diagram matrices.
- Aurigo coverage was judged only from provided Aurigo docs; "Not evidenced" ≠ "product lacks it".

## How this relates to the MVP phase plan
The MVP IDs (001–021, 023–048, 063–081, 107–110, 233–268) fall almost entirely in releases **R02–R14**: R02 project/parcel core, R03 owners, R04 parcel depth, R06 acquisition rules, R07–R08 title/survey, R09–R10 appraisal, R11–R12 offers/agreements, R13 payments, R14 closing.
Not in the MVP but sequenced *before* it in the workbook: **R01 platform foundation** (we cover only the slice we need in Phase 0) and **R05 templates/mail-merge/e-signature** (we cover only a minimal "signed" record).
IDs inside MVP ranges that the workbook places in later releases (deferred in our plan): ROW-031 (R32 vendor portal), ROW-037 (R34), ROW-038 (R16).
Use the `Depends on` column to check a feature's prerequisites before building it.
