# Diagram 7 – Payments, Closing and Financial Control

Paste the block under **Diagram** into https://mermaid.live. It renders without errors (mermaid-cli 11.12). Size: about 4274 characters.

**Legend:** blue = ROW staff and approvers, orange = external party, green = system or finance. **Yellow = a new requirement (ROW-233 onward; see New_Requirements.md).** **Dashed purple outline = agency-specific item.** Evidence codes: **V** stated in a project source, **A** shown in your agency SOPs or workflows, **O** carried from your original diagram or feature list, **D** my design decision, **U** your decision.

Source short names: NDOT (Nevada DOT ROW Manual 2025), HDOT (Hawaii DOT HWY-R Manual 2025), ATP (Austin Transit Partnership manuals), TH (Training Handbook), TBW (Tampa Bay Water guideline), REM, GP, EF, PA SOP, ED SOP, Donation, Developer, Early, Settlement and RFF SOPs (Orange County, Florida), P1, P2 and Conveyance workflows, PUA matrix (City of Virginia Beach), IRWIN manuals, lifecycle document (Researcher Copilot).

## What changed in this final set

| Change | Reason |
|---|---|
| Fund reservation moved before approval; encumbrance adjusted at expenditure | RFF SOP; PA SOP |
| Added Request for Funds form content and court-ordered payments | RFF SOP packet |
| Added closing documents approved as to form, incidental expenses, recording and distribution | HDOT 2.8; REM; PA SOP |
| Added federal reimbursement verification and billing | HDOT objectives (limited evidence) |
| Moved reports (ROW-113 FHWA expenditure reports, ROW-114) to Diagram 13 | Reporting capability |

## Diagram

```mermaid
flowchart TB
  subgraph FA["Contract Administrator – funds"]
    direction LR
    FA_a["Check funding availability; reserve funds or arrange a budget transfer; log in the fund tracking sheet<br/>(ROW-116, ROW-284)"]
  end
  subgraph L["Legal Counsel / County Attorney"]
    direction LR
    L_a["Send court-ordered payment memo (order of taking, stipulated order, final judgment) with allocation by parcel and deadline<br/>(ROW-286)"]
    L_b["Approve conveyance documents as to form<br/>(ROW-288)"]
  end
  subgraph A["ROW Agent / Negotiator"]
    direction LR
    A_a["Record offers, settlements and awards per parcel (outgoing and incoming)<br/>(ROW-107)"]
    A_b["Prepare Request for Funds: transaction type, attachments (contract, executed instruments, certificate of value, settlement analysis) and payees<br/>(ROW-108, ROW-285)"]
    A_c["Create payment request: owner, title company, displacee, court or attorney; split among payees by percentage not above 100 percent<br/>(ROW-108)"]
    A_a --> A_b
    A_b --> A_c
  end
  subgraph AP["Approving Official – configurable chain"]
    direction LR
    AP_a{"Amount above threshold?"}
    AP_b["Second-level approval by threshold (agent, manager, chief engineer, attorney, fiscal)<br/>(ROW-108)"]
    AP_c["Approve payment<br/>(ROW-108)"]
    AP_a -->|"Yes"| AP_b
    AP_a -->|"No"| AP_c
  end
  subgraph F1["Finance / Accounts Payable"]
    direction LR
    F1_a["Add vendor; auto-populate vendor code<br/>(ROW-112)"]
    F1_b["Show project fund balance; alert if below threshold<br/>(ROW-116)"]
    F1_c["Process or verify payment in finance system<br/>(ROW-111)"]
    F1_d["Issue warrant, check or EFT; record numbers and dates<br/>(ROW-109)"]
    F1_e["Confirm payment or deposit precedes any required surrender of possession (Diagrams 4 and 6)<br/>(ROW-110)"]
    F1_a --> F1_b
    F1_b --> F1_c
    F1_c --> F1_d
    F1_d --> F1_e
  end
  subgraph TC["Title / Escrow Company (external)"]
    direction LR
    TC_a["Receive payment or notice of deposit; schedule closing<br/>(ROW-110)"]
    TC_b["Complete closing; record instruments; return recorded documents<br/>(ROW-110)"]
    TC_a --> TC_b
  end
  subgraph A2["ROW Agent / Negotiator – closing record"]
    direction LR
    A2_a["Document scheduled closing date and completion<br/>(ROW-110)"]
    A2_b["Pay owner incidental expenses and prorated taxes on claim<br/>(ROW-287)"]
    A2_c["File recorded documents; send copies to owner, project file and tax authority<br/>(ROW-289)"]
    A2_a --> A2_b
    A2_b --> A2_c
  end
  subgraph F2["Finance – expenditure tracking and federal billing"]
    direction LR
    F2_a["Record expenditure by project, phase and funding source; adjust encumbrance<br/>(ROW-116)"]
    F2_b["Categorise: acquisition, relocation, taxes, incidentals, protective rent, federally ineligible items<br/>(ROW-113)"]
    F2_c["Verify federal eligibility and prepare reimbursement billing<br/>(ROW-290)"]
    F2_a --> F2_b
    F2_b --> F2_c
  end
  subgraph P["ROW Project Lead / Project Manager"]
    direction LR
    P_a["Manage consultants and contractors: data, work completed, payments, emergency litigation purchase orders, statewide contracts<br/>(ROW-115, ROW-040)"]
  end
  subgraph FH["FHWA / Federal Oversight (external)"]
    direction LR
    FH_a["Review federal reimbursement and audit; documentation and audit trail available<br/>(ROW-079)"]
  end
  L -->|"Court-ordered memo"| FA
  FA -->|"Funds reserved"| A
  A -->|"Payment request"| AP
  AP -->|"Approved"| F1
  AP -->|"Rejected: revise"| A
  F1 -->|"Funds sent"| TC
  L -->|"Documents approved as to form"| A2
  TC -->|"Closing complete"| A2
  A2 -->|"Closing posted"| F2
  F2 -->|"Consultant costs"| P
  F2 -->|"Reimbursement billing"| FH
  classDef new fill:#fff8d6,stroke:#b8960c
  classDef agency stroke-dasharray:5 4,stroke:#7a4f9a
  class FA_a,L_a,L_b,A_b,A2_b,A2_c,F2_c new
  style FA fill:#e8f1fb,stroke:#4a78b5
  style L fill:#e8f1fb,stroke:#4a78b5
  style A fill:#e8f1fb,stroke:#4a78b5
  style AP fill:#e8f1fb,stroke:#4a78b5
  style F1 fill:#eaf6ea,stroke:#4a8f4a
  style TC fill:#fdf0e0,stroke:#c07a1f
  style A2 fill:#e8f1fb,stroke:#4a78b5
  style F2 fill:#eaf6ea,stroke:#4a8f4a
  style P fill:#e8f1fb,stroke:#4a78b5
  style FH fill:#fdf0e0,stroke:#c07a1f
```

## Source verification matrix

| Node | Evidence | Source and detail |
|---|---|---|
| FA_a Fund reservation | V, A | RFF SOP (Contract Administrator reserves funds or arranges a budget transfer, logs the fund tracking sheet, then approvals); PA SOP (funding checked before the first offer). ROW-116, **ROW-284** |
| L_a Court-ordered payment memo | A | RFF packet: County Attorney memos for orders of taking with allocation by parcel (compensation, attorney fees, expert fees) and a deposit deadline. **ROW-286** |
| L_b Approve as to form | V | HDOT 2.8 (Legal Counsel approves conveyance documents as to form). **ROW-288** |
| A_a Record offers and awards | O | ROW-107 |
| A_b Request for Funds | A | RFF SOP form: BCC or ordinance approval, transaction type (pre- or post-condemnation, at, below or above approved appraisal, advance payment), attachments (contract, executed instruments, certificate of value, settlement analysis), payees including attorney trust accounts and recording fees. ROW-108, **ROW-285** |
| A_c Payment request, payees | O | ROW-108 |
| AP_a to AP_c Approvals | O, A | ROW-108. RFF SOP: agent recommends; REM manager, chief engineer, engineering manager and fiscal approve; county attorney above thresholds. Configurable |
| F1_a to F1_d Vendor, balance, process, issue | O | ROW-112, 116, 111, 109 |
| F1_e Payment before possession | V | NDOT 7.417, 7.514 (24.102(j)); HDOT 2.2-14. ROW-110 |
| TC_a, TC_b Closing | O, V | ROW-110. NDOT 7.424 (all acquisitions through escrow unless directed otherwise), 7.435 (final escrow billing, owner's policy of title, final settlement statement) |
| A2_a Closing record | O | ROW-110 |
| A2_b Incidental expenses | V | HDOT 2.8 (recording fees, mortgage prepayment penalty, prorated taxes on claim); NDOT 7.512. **ROW-287** |
| A2_c Recorded documents | V, A | HDOT 2.8 (recordation, distribution to project file, owner and county tax division); PA SOP (deed recording, recorded copy). **ROW-289** |
| F2_a Expenditure, encumbrance | O | ROW-116 |
| F2_b Categorise | O, V | ROW-113. HDOT: items compensable under state law but ineligible for federal participation are listed; interest on awards is not federally eligible |
| F2_c Federal billing | V (limited) | HDOT objectives (internal control and verification of federal reimbursement, 23 CFR 710.203). No source describes the billing mechanics. **ROW-290** |
| P_a Consultants | O, A | ROW-115, ROW-040 (statewide-contract half). RFF packet: emergency purchase orders for litigation |
| FH_a Federal review | O | ROW-079 |

## Moved out of this diagram

| Feature | Now in |
|---|---|
| ROW-113 FHWA expenditure reports; ROW-114 appraised versus paid | Diagram 13 |

## Open items

1. Federal billing mechanics (progress vouchers, eligibility checks) have no source in the files; please supply the agency practice.
