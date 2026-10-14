# Diagram 13 – Reporting, Dashboards and Analytics

Paste the block under **Diagram** into https://mermaid.live. It renders without errors (mermaid-cli 11.12). Size: about 2736 characters.

**Legend:** blue = ROW staff and approvers, orange = external party, green = system or finance. **Yellow = a new requirement (ROW-233 onward; see New_Requirements.md).** **Dashed purple outline = agency-specific item.** Evidence codes: **V** stated in a project source, **A** shown in your agency SOPs or workflows, **O** carried from your original diagram or feature list, **D** my design decision, **U** your decision.

Source short names: NDOT (Nevada DOT ROW Manual 2025), HDOT (Hawaii DOT HWY-R Manual 2025), ATP (Austin Transit Partnership manuals), TH (Training Handbook), TBW (Tampa Bay Water guideline), REM, GP, EF, PA SOP, ED SOP, Donation, Developer, Early, Settlement and RFF SOPs (Orange County, Florida), P1, P2 and Conveyance workflows, PUA matrix (City of Virginia Beach), IRWIN manuals, lifecycle document (Researcher Copilot).

## What changed in this final set

| Change | Reason |
|---|---|
| Redrawn as capability groups with no sequence arrows | Your decision |
| Operational reports from Diagrams 1 to 9 collected in one group | Cross-diagram reconciliation |

## Diagram

```mermaid
flowchart TB
  subgraph G1["Dashboards and self-service"]
    direction TB
    A1["Configurable dashboards with drill-down and status indicators<br/>(ROW-188)"]
    A2["Ad-hoc query and self-service report builder<br/>(ROW-189)"]
    A3["Report portal; saved and shared reports; scheduled distribution<br/>(ROW-190)"]
    A4["Export formats: spreadsheet, PDF and others<br/>(ROW-191)"]
    A5["Charts, graphs and gauges<br/>(ROW-192)"]
  end
  subgraph G2["Performance and analytics"]
    direction TB
    B1["User-defined performance measures and KPIs<br/>(ROW-193)"]
    B2["Variance, forecasting and trend analytics, including earned value<br/>(ROW-194)"]
    B3["Project status, master schedule, cost and cash-flow reports<br/>(ROW-195)"]
    B4["Acquisition, parcel and agent-level status and performance reports<br/>(ROW-199)"]
  end
  subgraph G3["Regulatory reports"]
    direction TB
    C1["FHWA annual reports and statistics<br/>(ROW-196)"]
    C2["Title VI civil-rights reporting<br/>(ROW-197)"]
    C3["IRS 1099, municipal tax and year-end reports<br/>(ROW-198)"]
    C4["Relocation measures, performance and funding-agency relocation reports, including the final relocation report<br/>(ROW-099)"]
  end
  subgraph G4["Operational reports from the process diagrams"]
    direction TB
    D1["Annual ROW budgets (Diagram 1)<br/>(ROW-013)"]
    D2["Title workload and survey status (Diagram 2)<br/>(ROW-030, ROW-041)"]
    D3["Appraisal status and cross-parcel summaries (Diagram 3)<br/>(ROW-050, ROW-061)"]
    D4["Negotiator progress by project and parcel (Diagram 4)<br/>(ROW-069)"]
    D5["Condemnation-rate analytics (Diagram 6)<br/>(ROW-105)"]
    D6["FHWA expenditure reports; appraised versus paid compensation (Diagram 7)<br/>(ROW-113, ROW-114)"]
    D7["Utility agreement, conflict cost, performance and contact reports (Diagram 9)<br/>(ROW-156)"]
    D8["Lease and tenant compliance, sales and surplus, property financials and inventory reports (Diagram 8)<br/>(ROW-135, ROW-141, ROW-143, ROW-144)"]
    D9["Rail long-range and G and C reports (agency-specific: NH) (Diagram 8)<br/>(ROW-147)"]
  end
  subgraph G5["Specialty reports and data access"]
    direction TB
    E1["Specialty reports: bonding, GIS parcels, historical, owner and contact lists<br/>(ROW-200)"]
    E2["Read-only ODBC access and data-warehouse style reporting<br/>(ROW-201)"]
  end
  classDef agency stroke-dasharray:5 4,stroke:#7a4f9a
  class D9 agency
  classDef new fill:#fff8d6,stroke:#b8960c
  G1 ~~~ G2
  G2 ~~~ G3
  G3 ~~~ G4
  G4 ~~~ G5
  style G1 fill:#e8f1fb,stroke:#4a78b5
  style G2 fill:#e8f1fb,stroke:#4a78b5
  style G3 fill:#e8f1fb,stroke:#4a78b5
  style G4 fill:#e8f1fb,stroke:#4a78b5
  style G5 fill:#e8f1fb,stroke:#4a78b5
```

## Source verification matrix

| Node | Evidence | Source and detail |
|---|---|---|
| G1 Dashboards and self-service | O | ROW-188 to ROW-192 |
| G2 Performance and analytics | O | ROW-193, 194, 195, 199 |
| G3 Regulatory reports | O, V | ROW-196 to ROW-198; ROW-099 (ATP 11: monthly or quarterly reports to the funding agency and a final relocation report; records kept three years after final payment). HDOT 2.8: 1099-S reporting when a purchase exceeds the reporting threshold |
| G4 Operational reports | O | ROW-013 (annual budgets; NDOT 2.312), 030, 041, 050, 061, 069, 105, 113, 114, 156, 135, 141, 143, 144, 147. Source diagrams are named on each box |
| G5 Specialty and data access | O | ROW-200, ROW-201 |

