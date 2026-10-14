# Diagram 6 – Condemnation and Legal

Paste the block under **Diagram** into https://mermaid.live. It renders without errors (mermaid-cli 11.12). Size: about 3801 characters.

**Legend:** blue = ROW staff and approvers, orange = external party, green = system or finance. **Yellow = a new requirement (ROW-233 onward; see New_Requirements.md).** **Dashed purple outline = agency-specific item.** Evidence codes: **V** stated in a project source, **A** shown in your agency SOPs or workflows, **O** carried from your original diagram or feature list, **D** my design decision, **U** your decision.

Source short names: NDOT (Nevada DOT ROW Manual 2025), HDOT (Hawaii DOT HWY-R Manual 2025), ATP (Austin Transit Partnership manuals), TH (Training Handbook), TBW (Tampa Bay Water guideline), REM, GP, EF, PA SOP, ED SOP, Donation, Developer, Early, Settlement and RFF SOPs (Orange County, Florida), P1, P2 and Conveyance workflows, PUA matrix (City of Virginia Beach), IRWIN manuals, lifecycle document (Researcher Copilot).

## What changed in this final set

| Change | Reason |
|---|---|
| Approval is a configurable chain (supervisor or engineer, review board, manager, board resolution) | NDOT 2.410, 7.515; ED SOP; HDOT impasse section |
| Added order-of-taking checklist and pre-order updates | ED SOP |
| Added motion for order of possession and court deposit; deposit withdrawal by the owner | HDOT 2.9; ED SOP; RFF packet |
| Added settlement and stipulated judgment during litigation, trial report, final judgment recording | HDOT 2.9; ED SOP; Phase II suit path |
| Moved condemnation-rate analytics (ROW-105) to Diagram 13 | Reporting capability |

## Diagram

```mermaid
flowchart TB
  subgraph A["ROW Agent / Negotiator"]
    direction LR
    A_a["Document impasse: last offer, counter-offers, attempts, appraisal, diary and title; confirm at least 30 days since the offer<br/>(ROW-101)"]
    A_b["Prepare condemnation request: ROW maps and descriptions, certificate of search, appraisal, offer letter, diary, correspondence<br/>(ROW-101)"]
    A_c["Record decision to condemn and reasons<br/>(ROW-100)"]
    A_d["Route request electronically for approval<br/>(ROW-101)"]
    A_a --> A_b
    A_b --> A_c
    A_c --> A_d
  end
  subgraph AP["Approvers – configurable chain"]
    direction LR
    AP_c["Approval chain: supervisor or engineer, review board, manager, resolution to the board where required<br/>(ROW-278)"]
    AP_a{"Approve condemnation?"}
    AP_b["Approved: send packet to Legal<br/>(ROW-101)"]
    AP_d["Not approved: return to negotiation or revise plans<br/>(ROW-101)"]
    AP_c --> AP_a
    AP_a -->|"Yes"| AP_b
    AP_a -->|"No"| AP_d
  end
  subgraph L1["Legal Counsel / Attorney's Office – filing and possession"]
    direction LR
    L1_a["Record date packet received; open condemnation case<br/>(ROW-100)"]
    L1_b["Update appraisal and specialty reports; complete order-of-taking checklist<br/>(ROW-279)"]
    L1_c["Prepare notices, declaration of taking or complaint, and summons<br/>(ROW-102)"]
    L1_d["File complaint; serve parties; record notice; send required copies<br/>(ROW-102)"]
    L1_e["Move for order of possession with affidavit of estimated just compensation<br/>(ROW-280)"]
    L1_f["Track file location and recorded-document status<br/>(ROW-103)"]
    L1_a --> L1_b
    L1_b --> L1_c
    L1_c --> L1_d
    L1_d --> L1_e
    L1_e --> L1_f
  end
  subgraph F1["Finance / Accounts Payable – deposit"]
    direction LR
    F1_a["Deposit estimated just compensation into the court registry by the court deadline<br/>(ROW-104)"]
    F1_b["Pay process servers and expert witnesses; warrant payable to court<br/>(ROW-104)"]
    F1_a --> F1_b
  end
  subgraph O["Property Owner (external)"]
    direction LR
    O_a["Receive notices; may contest possession; may withdraw the deposit after title and tax clearance<br/>(ROW-281)"]
    O_b["Mediation or trial"]
    O_c{"Appeal award?"}
    O_a --> O_b
    O_b --> O_c
  end
  subgraph L2["Legal Counsel – milestones and outcome"]
    direction LR
    L2_a["Record possession, mediation, trial and judgment milestones<br/>(ROW-100)"]
    L2_b["Negotiate settlement in litigation; ROW manager concurs; stipulated judgment<br/>(ROW-282)"]
    L2_c["Record trial report<br/>(ROW-283)"]
    L2_d["Record court award and outcome<br/>(ROW-100)"]
    L2_e["Recommend and approve appeal; record appeal, settled date and appeal amounts<br/>(ROW-106)"]
    L2_f["Record final judgment; update title and GIS layer (Diagram 2)"]
    L2_a --> L2_b
    L2_b --> L2_c
    L2_c --> L2_d
    L2_d --> L2_e
    L2_e --> L2_f
  end
  subgraph F2["Finance / Accounts Payable – final payment"]
    direction LR
    F2_a["Make court-ordered and final condemnation payments, including owner's attorney fees and expert costs (Diagram 7)<br/>(ROW-104)"]
  end
  A -->|"Request for approval"| AP
  AP -->|"Packet approved"| L1
  AP -->|"Not approved"| A
  L1 -->|"Deposit requested"| F1
  F1 -->|"Deposit made; possession"| O
  O -->|"Hearing; appeal if filed"| L2
  L2 -->|"Award or judgment entered"| F2
  classDef new fill:#fff8d6,stroke:#b8960c
  classDef agency stroke-dasharray:5 4,stroke:#7a4f9a
  class AP_c,L1_b,L1_e,O_a,L2_b,L2_c new
  style A fill:#e8f1fb,stroke:#4a78b5
  style AP fill:#e8f1fb,stroke:#4a78b5
  style L1 fill:#e8f1fb,stroke:#4a78b5
  style F1 fill:#eaf6ea,stroke:#4a8f4a
  style O fill:#fdf0e0,stroke:#c07a1f
  style L2 fill:#e8f1fb,stroke:#4a78b5
  style F2 fill:#eaf6ea,stroke:#4a8f4a
```

## Source verification matrix

| Node | Evidence | Source and detail |
|---|---|---|
| A_a, A_b Impasse record and request | V, O | ROW-101. ED SOP (unable to settle after a minimum of 30 days); HDOT request exhibits (ROW maps and descriptions, certificate of search, appraisal, offer letter, diary, correspondence) |
| A_c, A_d Decision, routing | O | ROW-100, ROW-101 |
| AP_c Approval chain | V, A | NDOT 2.410 (Negotiator's Report, Condemnation Review Board, resolution to the Transportation Board); ED SOP (engineer approves parcel, REM Manager approves, board approves resolution, then Litigation); HDOT (Principal ROW Agent concurs, ROW Branch Head approves). Configurable. **ROW-278** |
| AP_a, AP_b, AP_d Decision | O | ROW-101 |
| L1_a Open case | O | ROW-100 |
| L1_b Order-of-taking checklist | A | ED SOP: updated cure report, updated appraisal requested by Legal, checks of parcel file, schedules, legals and testimony, reserve deposit. **ROW-279** |
| L1_c, L1_d Documents, filing | O, V | ROW-102. HDOT: complaint with description (Exhibit A) and map (Exhibit B), summons answered within 20 days |
| L1_e Order of possession | V | HDOT: ex parte motion with affidavit of estimated just compensation; deposit required before the order; owner may contest within 10 days. **ROW-280** |
| L1_f File tracking | O | ROW-103 (the original wording named "Land Titles", generalised) |
| F1_a Court deposit | V, A | HDOT (request for payment, warrant, receipt of deposit, certificate of deposit and possession); ED SOP (deposit no later than 20 days after the order); RFF packet (deposit into the court registry by the deadline). ROW-104 |
| F1_b Servers and experts | O | ROW-104 |
| O_a Notices, contest, withdrawal | V | HDOT: owner may withdraw the deposit if title is clear or by motion; tax clearance; withdrawal abandons defences except further compensation. **ROW-281** |
| O_b, O_c Mediation or trial, appeal | O, A | ED SOP: mediation, then trial |
| L2_a Milestones | O | ROW-100 |
| L2_b Settlement in litigation | V, A | HDOT (attorney recommends, ROW Manager concurs, FHWA concurrence optional, stipulated judgment); ED SOP (mediation settlement approved by PW, REM and Litigation). **ROW-282** |
| L2_c Trial report | V | HDOT trial report contents. **ROW-283** |
| L2_d Award and outcome | O | ROW-100 |
| L2_e Appeal | V, O | HDOT (trial attorney recommends, Senior Deputy reviews, ROW Manager approves). ROW-106 |
| L2_f Final judgment, title, GIS | A | Phase II (final judgment recording, GIS layer); ED SOP |
| F2_a Final payments | V, O | RFF packet (final judgment, attorney fees, expert costs); ROW-104 |

## Moved out of this diagram

| Feature | Now in |
|---|---|
| ROW-105 condemnation-rate analytics | Diagram 13 |

## Open items

1. Interest on awards is not federally eligible in Hawaii; handled in Diagram 7 categorisation.
2. Land Court subdivision and inverse condemnation (Hawaii) are state-specific and not drawn.
