# Diagram 5 – Relocation Assistance

Paste the block under **Diagram** into https://mermaid.live. It renders without errors (mermaid-cli 11.12). Size: about 5922 characters.

**Legend:** blue = ROW staff and approvers, orange = external party, green = system or finance. **Yellow = a new requirement (ROW-233 onward; see New_Requirements.md).** **Dashed purple outline = agency-specific item.** Evidence codes: **V** stated in a project source, **A** shown in your agency SOPs or workflows, **O** carried from your original diagram or feature list, **D** my design decision, **U** your decision.

Source short names: NDOT (Nevada DOT ROW Manual 2025), HDOT (Hawaii DOT HWY-R Manual 2025), ATP (Austin Transit Partnership manuals), TH (Training Handbook), TBW (Tampa Bay Water guideline), REM, GP, EF, PA SOP, ED SOP, Donation, Developer, Early, Settlement and RFF SOPs (Orange County, Florida), P1, P2 and Conveyance workflows, PUA matrix (City of Virginia Beach), IRWIN manuals, lifecycle document (Researcher Copilot).

## What changed in this final set

| Change | Reason |
|---|---|
| Added relocation planning at project initiation, General Information Notice and personal advisory interview | ATP 6.1a, 6.3; NDOT 8.403, 8.408 (24.205(a), 24.203(a)) |
| Split payment determination by displacee type instead of a sequential chain | ATP 7, 8; NDOT 8.414-8.425 |
| Added comparable-dwelling notice, decent-safe-sanitary inspection, housing of last resort, eligibility exceptions, advance payments | ATP 6.2-6.8, 8.4 |
| 30-day notice moved from the displacee lane to the agency's notices | ATP 6.1c; NDOT 8.410 |
| Added appeal path | ATP 12; NDOT 8.411 (24.10) |
| ROW-022 (Title VI information) moved in from Diagram 1; ROW-098 (demolition) moved to Diagram 10; ROW-099 reports moved to Diagram 13 | Your decisions; IRWIN relocation screens; Practitioner Handbook Phase 6 |

## Diagram

```mermaid
flowchart TB
  subgraph RP["Relocation Agent / Project Lead – planning at project initiation"]
    direction LR
    RP_a["Prepare relocation planning study: displacees by type, replacement housing and business sites, special needs, last-resort need<br/>(ROW-269)"]
    RP_b["Identify displacement parcels; create relocation case with unique ID and type (residential or non-residential)<br/>(ROW-085)"]
    RP_a --> RP_b
  end
  subgraph R1["Relocation Agent – notices and advisory"]
    direction LR
    R1_a["Send General Information Notice with relocation brochure as soon as feasible; record to whom and date<br/>(ROW-094, ROW-270)"]
    R1_b["Hold personal advisory interview with each displacee; record needs; record ethnicity, gender and minority status for Title VI<br/>(ROW-022, ROW-271)"]
    R1_c["Pre-populate worksheets and questionnaires from parcel and owner data<br/>(ROW-086)"]
    R1_d["Issue Notice of Relocation Eligibility when eligibility begins: earliest of notice of intent, initiation of negotiations, binding agreement or acquisition<br/>(ROW-094)"]
    R1_e["Record displacee contact and new address<br/>(ROW-096)"]
    R1_a --> R1_b
    R1_b --> R1_c
    R1_c --> R1_d
    R1_d --> R1_e
  end
  subgraph O1["Displacee (external) – information"]
    direction LR
    O1_a["Complete residential or non-residential relocation questionnaire<br/>(ROW-086)"]
    O1_b["Provide statement of rent and income<br/>(ROW-093)"]
    O1_a --> O1_b
  end
  subgraph R2["Relocation Agent – housing and eligibility"]
    direction LR
    R2_a["Prepare replacement-housing study; record available comparable housing; give written notice of the comparable and the price or rent used for the payment limit<br/>(ROW-092, ROW-272)"]
    R2_b["Inspect replacement dwelling for decent, safe and sanitary standard; offer transportation to inspect housing<br/>(ROW-273)"]
    R2_c["Where comparable housing is not available, provide housing of last resort<br/>(ROW-276)"]
    R2_d["Compute eligibility from occupancy and displacement dates; check exceptions: eviction for cause, alien not lawfully present, duplication<br/>(ROW-095, ROW-277)"]
    R2_e["Issue 90-day notice, then a 30-day notice with the move date; no move before a comparable dwelling is available (urgent-need exception documented)<br/>(ROW-094)"]
    R2_a --> R2_b
    R2_b --> R2_c
    R2_c --> R2_d
    R2_d --> R2_e
  end
  subgraph R3["Relocation Agent – payment calculation"]
    direction LR
    R3_a["Residential: moving costs (actual or fixed); owner-occupants: purchase supplement with mortgage interest and price differentials; tenants: rental assistance up to 42 months or down payment<br/>(ROW-087, ROW-088, ROW-089, ROW-090)"]
    R3_b["Non-residential: actual moving and related expenses, reestablishment, or fixed payment<br/>(ROW-087)"]
    R3_c["Use external calculators or agency worksheets; attach to the case<br/>(ROW-091)"]
    R3_a --> R3_b
    R3_b --> R3_c
  end
  subgraph O2["Displacee (external) – claim and move"]
    direction LR
    O2_a["Select replacement dwelling or site"]
    O2_b["Submit claim with supporting documents within 18 months<br/>(ROW-093)"]
    O2_c["Move by the notified date"]
    O2_a --> O2_b
    O2_b --> O2_c
  end
  subgraph R4["Relocation Agent – claims and payment request"]
    direction LR
    R4_a["Generate claim forms and pre-populated statement of rent and income; help the displacee complete the claim<br/>(ROW-093)"]
    R4_b["Alert before the 18-month claim period ends; send letter; waive for good cause<br/>(ROW-095)"]
    R4_c["Issue advance payment where hardship is shown<br/>(ROW-275)"]
    R4_d["Create relocation payment request; note taxable or non-taxable type for 1099<br/>(ROW-097)"]
    R4_e["If a claim is denied in whole or part, send written notice with the basis and the appeal procedure<br/>(ROW-097)"]
    R4_a --> R4_b
    R4_b --> R4_c
    R4_c --> R4_d
    R4_d ~~~ R4_e
  end
  subgraph AP["Approving Official"]
    direction LR
    AP_a["Approve relocation payment through rules-based workflow<br/>(ROW-097)"]
  end
  subgraph F["Finance / Accounts Payable"]
    direction LR
    F_a["Process relocation payment by warrant or EFT through the shared payment workflow (Diagram 7)<br/>(ROW-097)"]
  end
  subgraph AL1["Displacee (external) – appeal"]
    direction LR
    AL1_a["File appeal within the agency's time limit (60 days in the Austin procedure) after written notice of the decision"]
  end
  subgraph AL2["Appeals Official"]
    direction LR
    AL2_a["Review the file and decide the appeal; notify the displacee in writing; track outcome<br/>(ROW-274)"]
  end
  subgraph H["Relocation Agent – hand-off"]
    direction LR
    H_a["Record vacated date; notify Property Management that occupants moved or rent-back is approved (Diagram 8); release the parcel to clearance (Diagram 10)"]
  end
  RP -->|"Planning study and case"| R1
  R1 -->|"Questionnaire and notices"| O1
  O1 -->|"Information returned"| R2
  R2 -->|"Eligibility and housing notices"| R3
  R3 -->|"Payment determination"| O2
  O2 -->|"Claim submitted"| R4
  R4 -->|"Payment request"| AP
  AP -->|"Approved"| F
  AP -->|"Rejected: correct claim"| R4
  R4 -->|"Claim denied"| AL1
  AL1 -->|"Appeal filed"| AL2
  AL2 -->|"Decision"| R4
  F -->|"Displacee vacated"| H
  classDef new fill:#fff8d6,stroke:#b8960c
  classDef agency stroke-dasharray:5 4,stroke:#7a4f9a
  class RP_a,R1_a,R1_b,R2_a,R2_b,R2_c,R2_d,R4_c,AL2_a new
  style RP fill:#e8f1fb,stroke:#4a78b5
  style R1 fill:#e8f1fb,stroke:#4a78b5
  style O1 fill:#fdf0e0,stroke:#c07a1f
  style R2 fill:#e8f1fb,stroke:#4a78b5
  style R3 fill:#e8f1fb,stroke:#4a78b5
  style O2 fill:#fdf0e0,stroke:#c07a1f
  style R4 fill:#e8f1fb,stroke:#4a78b5
  style AP fill:#e8f1fb,stroke:#4a78b5
  style F fill:#eaf6ea,stroke:#4a8f4a
  style AL1 fill:#fdf0e0,stroke:#c07a1f
  style AL2 fill:#e8f1fb,stroke:#4a78b5
  style H fill:#e8f1fb,stroke:#4a78b5
```

## Source verification matrix

| Node | Evidence | Source and detail |
|---|---|---|
| RP_a Relocation planning | V | ATP 6.3a; NDOT 8.403 (49 CFR 24.205(a)): planning in early project development, before any displacing action; may include a relocation survey or study. **ROW-269** |
| RP_b Displacement parcels, case | O, V | ROW-085. TH Phase 2: identify displacement parcels and tag for relocation planning |
| R1_a General Information Notice | V | ATP 6.1a; NDOT 8.408 (usually the first personal contact); HDOT text of 49 CFR 24.203(a) ("as soon as feasible"). The FHWA relocation brochure (2005, dated) is the standard brochure; no dollar limits are quoted here. ROW-094, **ROW-270** |
| R1_b Advisory interview, Title VI | V, O, U | ATP 6.3b (personal interview with each residential and business displacee; needs and preferences). ROW-022 moved here by your decision; IRWIN keeps Ethnicity, Gender and Minority on displacee and owner records inside its relocation screens. **ROW-271** |
| R1_c Pre-populate worksheets | O | ROW-086 |
| R1_d Notice of Relocation Eligibility | V | ATP 6.1b; NDOT 8.409 (generally the same date as initiation of negotiations and the initial offer; eligibility begins at the earliest of notice of intent, initiation of negotiations, binding agreement or acquisition; 24.203(b)). ROW-094 |
| R1_e Contact and new address | O | ROW-096 |
| O1_a, O1_b Questionnaire, rent and income | O | ROW-086, ROW-093 |
| R2_a Replacement-housing study, written comparable notice | V, O | ATP 6.2 and 6.3b(2)(b) (at least one comparable, preferably three; written notice of the comparable and the price or rent used for the payment limit); NDOT 8.412. ROW-092, **ROW-272** |
| R2_b Decent, safe and sanitary inspection | V | ATP 6.3b(2)(c), (e): inspect comparable housing where feasible; offer transportation to inspect housing. **ROW-273** |
| R2_c Housing of last resort | V | ATP 8.4; NDOT 8.425. **ROW-276** |
| R2_d Eligibility and exceptions | V, O | ATP 6.1b, 6.4 (eviction for cause), 6.6 (aliens not lawfully present), 6.8 (no duplication). ROW-095, **ROW-277** |
| R2_e 90-day and 30-day notices | V | ATP 6.1c; NDOT 8.410: 90-day notice may be issued 90 days or earlier before displacement; a later notice gives the move date at least 30 days ahead; if issued before a comparable is available, no move earlier than 90 days after; urgent-need exception with a determination in the file. These are agency actions. ROW-094 |
| R3_a Residential payments | V, O | ATP 7.1, 7.2, 8.1, 8.2, 8.3; NDOT 8.414-8.424: payment types depend on displacee type and tenure (90-day occupancy rule, not 180); purchase supplement with mortgage interest and price differentials; rental assistance up to 42 months. ROW-087 to ROW-090. The original drew these as sequential steps; they are alternatives by tenure |
| R3_b Non-residential payments | V, O | ATP 7.1, 7.3-7.5; NDOT 8.417-8.421. ROW-087 |
| R3_c External calculators | O | ROW-091 |
| O2_a to O2_c Select, claim, move | O, V | ROW-093; ATP 6.5d (claims within 18 months of displacement or, for owners, the later of displacement or final acquisition payment) |
| R4_a Claim forms and help | V, O | ATP 6.5a (documentation and reasonable help completing the claim); NDOT 8.413 (24.207). ROW-093 |
| R4_b Claim-expiry alerts | V, O | ATP 6.5d (18 months; waived for good cause). ROW-095 |
| R4_c Advance payment | V | ATP 6.5c. **ROW-275** |
| R4_d Payment request | O | ROW-097 |
| R4_e Denial notice | V | ATP 6.5e: written notice of the basis and the appeal procedure |
| AP_a, F_a Approve and pay | O, D | ROW-097. Relocation payments use the shared payment workflow of Diagram 7 (design decision) |
| AL1_a, AL2_a Appeals | V | ATP 12.4 (60 days after written notification), 12.5-12.9; NDOT 8.411 (appeal form to the Director; relocation appeal concerns only relocation, dissatisfaction with acquisition goes to the negotiator; 24.10). **ROW-274** |
| H_a Hand-off | V | NDOT 13.602 (Property Management takes over after notice that occupants moved or rent-back is approved). Clearance in Diagram 10 |

## Moved out of this diagram

| Feature | Now in |
|---|---|
| ROW-098 demolition and securing | Diagram 10 |
| ROW-099 relocation reports | Diagram 13 |

## Open items

1. The appeal time limit and form are agency-specific (60 days in the Austin procedure). I draw them as the agency's limit.
2. Dollar limits for replacement housing payments are not shown because the only brochure in the files (2005) is dated.
