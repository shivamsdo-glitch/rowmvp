# Diagram 0 – Master Diagram – End-to-End ROW Process (ten-stage lifecycle)

Paste the block under **Diagram** into https://mermaid.live. It renders without errors (mermaid-cli 11.12). Size: about 4438 characters.

**Legend:** blue = ROW staff and approvers, orange = external party, green = system or finance. **Yellow = a new requirement (ROW-233 onward; see New_Requirements.md).** **Dashed purple outline = agency-specific item.** Evidence codes: **V** stated in a project source, **A** shown in your agency SOPs or workflows, **O** carried from your original diagram or feature list, **D** my design decision, **U** your decision.

Source short names: NDOT (Nevada DOT ROW Manual 2025), HDOT (Hawaii DOT HWY-R Manual 2025), ATP (Austin Transit Partnership manuals), TH (Training Handbook), TBW (Tampa Bay Water guideline), REM, GP, EF, PA SOP, ED SOP, Donation, Developer, Early, Settlement and RFF SOPs (Orange County, Florida), P1, P2 and Conveyance workflows, PUA matrix (City of Virginia Beach), IRWIN manuals, lifecycle document (Researcher Copilot).

## What changed in this final set

| Change | Reason |
|---|---|
| Rebuilt on the ten-stage lifecycle (stage numbers shown in brackets) | Your decision; lifecycle document |
| Added ROW Authorization (stage 1) and ROW Certification with federal concurrence (stage 9); new Clearance and Certification team | Lifecycle Stages 1 and 9; TH 4.4-4.5; HDOT; NDOT 2.411 |
| FHWA / federal oversight now appears at the start, middle and end | HDOT; TH |
| Relocation planning starts from the project, not from the Negotiator | ATP 6.3a; NDOT 8.403 |
| 'Parcel acquired' no longer comes from Finance; acquisition, relocation, utility and possession status all feed certification | PA SOP; lifecycle Stage 9 |
| Utility and railroad coordination runs in parallel from the project | NDOT ch.9, 11 |

## Diagram

```mermaid
flowchart TB
  subgraph FH["FHWA / Federal Oversight (external)"]
    direction LR
    FH_a["[1] ROW Authorization: preliminary, then acquisition (Diagram 1)"]
    FH_b["[7] Reimbursement review and audit (Diagram 7)"]
    FH_c["[9] Concur with ROW Certification (Diagram 10)"]
  end
  subgraph A["System Administrator"]
    direction LR
    A_a["Configure rules, workflows, statuses, approvals, security and integrations (Diagrams 1 and 11)"]
  end
  subgraph P["ROW Project Lead / Project Manager"]
    direction LR
    P_a["[1] Create project; funding, estimate, budget, acquisition plan; submit work request (Diagram 1)"]
    P_b["[2] Parcels, parent tracts, owners and interests; team and milestones (Diagram 1)"]
    P_a --> P_b
  end
  subgraph E["Environmental Staff"]
    direction LR
    E_a["[1] Record NEPA decision; acquisition gate by funding source (Diagram 1)"]
  end
  subgraph T["Title Examiner and Surveyor"]
    direction LR
    T_a["[3] Title research, survey, legals and appraisal package (Diagram 2)"]
  end
  subgraph AP["Appraisal team and Approving Official"]
    direction LR
    AP_a["[4] Scope, assign, appraise and review (Diagram 3)"]
    AP_b["[4] Establish just compensation (Diagram 3)"]
    AP_a --> AP_b
  end
  subgraph N["ROW Agent / Negotiator"]
    direction LR
    N_a["[5] Offer package, negotiation, settlement (Diagram 4)"]
    N_b{"Agreement reached?"}
    N_c["[5] Approve, sign, due diligence, close and record (Diagrams 4 and 7)"]
    N_d["[6] Impasse: condemnation request (Diagrams 4 and 6)"]
    N_a --> N_b
    N_b -->|"Yes"| N_c
    N_b -->|"No"| N_d
  end
  subgraph O["Property Owner / Displacee / Tenant (external)"]
    direction LR
    O_a["Respond to notices, offers and relocation assistance"]
  end
  subgraph L["Legal Counsel / Attorney's Office"]
    direction LR
    L_a["[6] Condemnation filings, possession, deposit, award and appeal (Diagram 6)"]
  end
  subgraph R["Relocation Agent"]
    direction LR
    R_a["[7] Planning from project start; notices, advisory, payments, appeals (Diagram 5)"]
  end
  subgraph U["Utility and Railroad Coordinator"]
    direction LR
    U_a["[8] Utility and railroad coordination, in parallel with acquisition (Diagram 9)"]
  end
  subgraph F["Finance / Accounts Payable"]
    direction LR
    F_a["Payments, closing, expenditure and federal billing (Diagram 7)"]
  end
  subgraph CL["Clearance and Certification team"]
    direction LR
    CL_a["[9] Clearance, status matrix and ROW Certification (Diagram 10)"]
  end
  subgraph PM["Property Manager"]
    direction LR
    PM_a["[10] Inventory, leases, inspections, surplus and disposal (Diagram 8)"]
  end
  subgraph S["All ROW users and System"]
    direction LR
    S_a["Documents, workflow, notifications, templates, GIS and search (Diagram 12)"]
  end
  subgraph M["Management / Reporting Users"]
    direction LR
    M_a["Dashboards, KPIs and regulatory reports (Diagram 13)"]
  end
  A -->|"Rules in place"| P
  P -->|"Project and parcels"| E
  E -->|"NEPA decision"| FH
  FH -->|"Authorization"| T
  P -->|"Work request"| T
  T -->|"Title and survey package"| AP
  AP -->|"Just compensation approved"| N
  N -->|"Offer"| O
  O -->|"Counter or acceptance"| N
  N -->|"Impasse"| L
  P -->|"Relocation planning"| R
  O -->|"Displacement"| R
  R -->|"Relocation payments"| F
  N -->|"Signed agreement"| F
  L -->|"Deposit and award"| F
  P -->|"Utility conflicts"| U
  N -->|"Acquired parcels"| CL
  R -->|"Vacated"| CL
  U -->|"Utility clearance"| CL
  L -->|"Possession"| CL
  CL -->|"Certification"| FH
  FH -->|"Concurrence"| PM
  CL -->|"Cleared parcels"| PM
  F -->|"Cost data"| M
  PM -->|"Operations data"| M
  M -->|"Reports"| FH
  S -->|"Supports every step"| N
  classDef new fill:#fff8d6,stroke:#b8960c
  classDef agency stroke-dasharray:5 4,stroke:#7a4f9a
  style FH fill:#fdf0e0,stroke:#c07a1f
  style A fill:#e8f1fb,stroke:#4a78b5
  style P fill:#e8f1fb,stroke:#4a78b5
  style E fill:#e8f1fb,stroke:#4a78b5
  style T fill:#e8f1fb,stroke:#4a78b5
  style AP fill:#e8f1fb,stroke:#4a78b5
  style N fill:#e8f1fb,stroke:#4a78b5
  style O fill:#fdf0e0,stroke:#c07a1f
  style L fill:#e8f1fb,stroke:#4a78b5
  style R fill:#e8f1fb,stroke:#4a78b5
  style U fill:#e8f1fb,stroke:#4a78b5
  style F fill:#eaf6ea,stroke:#4a8f4a
  style CL fill:#e8f1fb,stroke:#4a78b5
  style PM fill:#e8f1fb,stroke:#4a78b5
  style S fill:#eaf6ea,stroke:#4a8f4a
  style M fill:#e8f1fb,stroke:#4a78b5
```

## Source verification matrix

| Node | Evidence | Source and detail |
|---|---|---|
| FH_a [1] Authorization | V | TH 4.4 (project-level; supplemental when parcels are added); HDOT authorization letter; lifecycle Stage 1 deliverable. Diagram 1 |
| FH_b [7] Reimbursement and audit | O, V | ROW-079; HDOT objectives. Diagram 7 |
| FH_c [9] Certification concurrence | V | Lifecycle Stage 9; HDOT. Diagram 10 |
| A_a Configuration | O | Diagrams 1 and 11 |
| P_a, P_b [1], [2] Project and parcels | V | Lifecycle Stages 1 and 2; TH phases 1 and 2. Diagram 1 |
| E_a [1] NEPA and gate | V | NDOT 1.502-1.504; lifecycle Stage 1. Diagram 1 |
| T_a [3] Title and survey | V | Lifecycle Stage 3; TH phase 2; REM Phase I. Diagram 2 |
| AP_a, AP_b [4] Appraisal | V | Lifecycle Stage 4; NDOT 6. Diagram 3 |
| N_a to N_d [5], [6] Negotiation, closing, impasse | V, A | Lifecycle Stages 5 and 6; PA and ED SOPs. Diagrams 4, 6, 7 |
| O_a Owner | O | All sources |
| L_a [6] Condemnation | V | HDOT 2.9; ED SOP. Diagram 6 |
| R_a [7] Relocation | V | ATP; NDOT 8. Diagram 5 |
| U_a [8] Utilities and railroads | V | NDOT 9, 11; lifecycle Stage 8. Diagram 9 |
| F_a Payments and billing | V | RFF SOP; HDOT. Diagram 7 |
| CL_a [9] Clearance and certification | V | Lifecycle Stage 9; Practitioner Handbook Phase 6. Diagram 10 |
| PM_a [10] Property management | V | NDOT 13; lifecycle Stage 10. Diagram 8 |
| S_a, M_a Services, reports | O | Diagrams 12 and 13 |

## Open items

1. The two Aurigo lifecycles differ (nine phases in the Training Handbook, ten stages in the lifecycle document and a different phase list in the Practitioner Handbook). The Master follows the ten-stage document by your decision.
