# ROW process diagrams – final set

Fourteen diagrams (Master and Diagrams 1 to 13), each with a source matrix, plus a feature index, a new-requirements list and a reconciliation log. Everything was validated against all sources first, reconciled across diagrams, and redrawn together after your decisions.

## Files
| File | What it is |
|---|---|
| Diagram00_Final.md | Master: ten-stage lifecycle |
| Diagram01_Final.md to Diagram09_Final.md | Project and parcel set-up; title and survey; appraisal; negotiation; relocation; condemnation; payments and closing; property management and disposal; utility and railroad coordination |
| Diagram10_Final.md | **New:** Clearance and Certification |
| Diagram11_Final.md to Diagram13_Final.md | Platform set-up (with the implementation sequence as a real flow), everyday services and reporting (capability maps, no sequence arrows) |
| New_Requirements.md | ROW-233 to ROW-323: process steps the sources show that the 232-feature list lacks |
| Feature_Index_Final.md | Where every one of the 323 requirements now appears |
| Reconciliation_Log.md | Everything moved between diagrams, and what is still open |
| *_preview.png | Rendered images |
| _superseded/ | Earlier versions, kept for reference only |

## Your decisions, as applied
Ten-stage lifecycle for the Master; certification types left configurable and undefined; platform diagrams redrawn as capability maps; new Diagram 10 with platform diagrams shifted to 11 to 13; agency-specific items kept with a dashed outline; railroad crossing agreements moved to the utility diagram, NH-specific rail data stays in Property Management; demolition and securing in Diagram 10; configurable parcel-creator lane; parcel status and agreement or interest status as manually set lists at both levels; ROW-022 in Diagram 5; ROW-013 as project budget; parent tract as the tract of record linked to tax parcel numbers.

## Defaults I applied without asking
New requirements numbered from ROW-233 in diagram order; the Master keeps user swimlanes with stage numbers in brackets; approvals are drawn as configurable chains because thresholds differ by agency (Orange County, Nevada, Hawaii and Austin all differ); capability maps list each feature once.

## Checks run
All 232 original features and all 91 new requirements appear in at least one diagram, and no unknown IDs appear. All 14 diagrams render without errors in mermaid-cli 11.12, including the code embedded in each document.

## Yours to verify
1. **Notice to Owner timing on federal-aid projects.** Drawn before the acquisition gate (REM, HDOT and your Phase II flow agree), but HDOT and NDOT say a notice of intent to acquire establishes relocation eligibility.
2. **CFR citation.** The Training Handbook cites 23 CFR 710.201 for NEPA before acquisition; NDOT cites 710.203; HDOT lists 710.201 as State Responsibilities. I could not check the eCFR (network off).
3. **Certification types.** The Training Handbook and the FHWA text quoted in the Hawaii manual define Types 2 and 3 differently; the handbook may need correcting.
4. **Federal billing mechanics** (ROW-290): no source describes them.

## Features with no source evidence (kept, not removed)
ROW-028, 029, 034 data types, 036, 037 (Trimble and Carlson), 060, 070, 121, 132, 134, 137, 138. "Advice of Rights" and "Acquisition Review Report" appear in no source and were removed from node text.

## Source coverage
Read in depth: all agency SOPs and workflows, the diagram file, the Austin manuals, NDOT chapters 1, 2, 5 to 9 and 13, HDOT acquisition, closing, condemnation and utility sections, the IRWIN acquisition manual, lifecycle document stages 1 and 9, Training Handbook phases 1 to 5, the RFF SOP packet (OCR'd), the PUA matrix and the product demo's parcel and agreement discussion. Searched or sampled only: the rest of the Training and Practitioner Handbooks, lifecycle stages 2 to 8 and 10, IRWIN Property Management, Billboard, Permitting and PST manuals, the other demo recordings, the York RFP, NDOT chapters 3, 4, 10, 11 and 12, HDOT chapters outside those listed, and the 2005 FHWA brochure. The Orange County, Florida identification comes from the County Attorney memos in the RFF SOP packet.
