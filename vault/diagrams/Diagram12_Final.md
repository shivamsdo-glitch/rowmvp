# Diagram 12 – Everyday Platform Services: Documents, Workflow, Communications, Templates, GIS and Search

Paste the block under **Diagram** into https://mermaid.live. It renders without errors (mermaid-cli 11.12). Size: about 3120 characters.

**Legend:** blue = ROW staff and approvers, orange = external party, green = system or finance. **Yellow = a new requirement (ROW-233 onward; see New_Requirements.md).** **Dashed purple outline = agency-specific item.** Evidence codes: **V** stated in a project source, **A** shown in your agency SOPs or workflows, **O** carried from your original diagram or feature list, **D** my design decision, **U** your decision.

Source short names: NDOT (Nevada DOT ROW Manual 2025), HDOT (Hawaii DOT HWY-R Manual 2025), ATP (Austin Transit Partnership manuals), TH (Training Handbook), TBW (Tampa Bay Water guideline), REM, GP, EF, PA SOP, ED SOP, Donation, Developer, Early, Settlement and RFF SOPs (Orange County, Florida), P1, P2 and Conveyance workflows, PUA matrix (City of Virginia Beach), IRWIN manuals, lifecycle document (Researcher Copilot).

## What changed in this final set

| Change | Reason |
|---|---|
| Redrawn as capability groups with no sequence arrows | Your decision |
| ROW-033 (hearing notification), ROW-040 (public inquiry) and ROW-062 (open an appraisal from the map) moved in | Diagrams 2 and 3 |
| Used-by links recorded in the matrix | Cross-diagram reconciliation |

## Diagram

```mermaid
flowchart TB
  subgraph G1["Documents and photos"]
    direction TB
    A1["Central document repository by project and parcel<br/>(ROW-157)"]
    A2["Tagging, categorisation and metadata<br/>(ROW-158)"]
    A3["Scanning with OCR; bulk import and export<br/>(ROW-159)"]
    A4["Versions, annotation and approval routing<br/>(ROW-160)"]
    A5["Integration with an external document system<br/>(ROW-161)"]
    A6["Photo storage and categorisation, including HEIC<br/>(ROW-162)"]
    A7["Document expiration tracking<br/>(ROW-163)"]
  end
  subgraph G2["Search"]
    direction TB
    B1["Record and document search by full text and metadata<br/>(ROW-164)"]
    B2["Flexible global search with wildcards and role-limited fields<br/>(ROW-185)"]
    B3["Saved searches and saved filters<br/>(ROW-186)"]
    B4["Keyword and fuzzy search<br/>(ROW-187)"]
  end
  subgraph G3["Workflow, tasks and schedules"]
    direction TB
    C1["Configurable workflow engine with approvals and escalations<br/>(ROW-165)"]
    C2["Task management, to-do lists and task filters<br/>(ROW-166)"]
    C3["Project schedules, milestones and on-schedule notifications<br/>(ROW-167)"]
    C4["Automated hand-off messages to the next team<br/>(ROW-168)"]
  end
  subgraph G4["Notifications, communication and portal"]
    direction TB
    D1["Automated alerts, reminders and scheduled notifications<br/>(ROW-170)"]
    D2["Mass messaging to multiple users<br/>(ROW-171)"]
    D3["Status transparency for interested parties<br/>(ROW-172)"]
    D4["External portal for title companies, appraisers, owners and utilities<br/>(ROW-173)"]
    D5["Correspondence tracking and Outlook integration<br/>(ROW-174)"]
    D6["Public-inquiry response: tag, answer and store<br/>(ROW-040)"]
  end
  subgraph G5["Templates, mail and signature"]
    direction TB
    E1["Document templates with auto-fill from parcel and project data<br/>(ROW-175)"]
    E2["Mail merge and mailing labels<br/>(ROW-176)"]
    E3["Certified-mail and USPS delivery tracking<br/>(ROW-177)"]
    E4["Electronic signature and tablet signing<br/>(ROW-179)"]
    E5["Hearing notification from template, with mail merge and USPS receipt (agency-specific: NH Land Titles data)<br/>(ROW-033)"]
  end
  subgraph G6["GIS and maps"]
    direction TB
    F1["GIS integration and interactive map viewer<br/>(ROW-180)"]
    F2["Linear referencing (Esri Roads and Highways)<br/>(ROW-181)"]
    F3["Parcel status colour-coding on maps<br/>(ROW-182)"]
    F4["Map overlays: appraisal, environmental, utility, historical, planned projects<br/>(ROW-183)"]
    F5["External map providers, base maps and geocoding standards<br/>(ROW-184)"]
    F6["Open an appraisal from the map<br/>(ROW-062)"]
  end
  classDef agency stroke-dasharray:5 4,stroke:#7a4f9a
  class E5 agency
  classDef new fill:#fff8d6,stroke:#b8960c
  G1 ~~~ G2
  G2 ~~~ G3
  G3 ~~~ G4
  G4 ~~~ G5
  G5 ~~~ G6
  style G1 fill:#e8f1fb,stroke:#4a78b5
  style G2 fill:#e8f1fb,stroke:#4a78b5
  style G3 fill:#e8f1fb,stroke:#4a78b5
  style G4 fill:#e8f1fb,stroke:#4a78b5
  style G5 fill:#e8f1fb,stroke:#4a78b5
  style G6 fill:#e8f1fb,stroke:#4a78b5
```

## Source verification matrix

| Node | Evidence | Source and detail |
|---|---|---|
| G1 Documents and photos | O | ROW-157 to ROW-163. The Orange County SOPs keep the permanent file in a shared drive; document versioning is also referenced by Diagram 3 (ROW-047) |
| G2 Search | O | ROW-164, 185 to 187 |
| G3 Workflow, tasks and schedules | O | ROW-165 to ROW-168. The configurable approval chains in Diagrams 3, 4, 6, 7 and 9 run on this engine |
| G4 Notifications, communication and portal | O | ROW-170 to ROW-174; ROW-040 from Diagram 2. ROW-173 is the external portal used by title companies (Diagram 2) and appraisers (Diagram 3) |
| G5 Templates, mail and signature | O, V | ROW-175 to ROW-177, 179; ROW-033 from Diagram 2 (agency-specific, dashed). ROW-179 safeguards in NDOT 7.403 (timestamp, signature linked to the document, certification); the York RFP is the only source for tablet signing. Used by Diagram 4 |
| G6 GIS and maps | O | ROW-180 to ROW-184; ROW-062 from Diagram 3 (ROW-183 already offers an appraisal overlay) |

