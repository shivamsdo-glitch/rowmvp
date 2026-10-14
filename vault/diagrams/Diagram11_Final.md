# Diagram 11 – Platform Set-up, Security, Hosting and Implementation

Paste the block under **Diagram** into https://mermaid.live. It renders without errors (mermaid-cli 11.12). Size: about 3410 characters.

**Legend:** blue = ROW staff and approvers, orange = external party, green = system or finance. **Yellow = a new requirement (ROW-233 onward; see New_Requirements.md).** **Dashed purple outline = agency-specific item.** Evidence codes: **V** stated in a project source, **A** shown in your agency SOPs or workflows, **O** carried from your original diagram or feature list, **D** my design decision, **U** your decision.

Source short names: NDOT (Nevada DOT ROW Manual 2025), HDOT (Hawaii DOT HWY-R Manual 2025), ATP (Austin Transit Partnership manuals), TH (Training Handbook), TBW (Tampa Bay Water guideline), REM, GP, EF, PA SOP, ED SOP, Donation, Developer, Early, Settlement and RFF SOPs (Orange County, Florida), P1, P2 and Conveyance workflows, PUA matrix (City of Virginia Beach), IRWIN manuals, lifecycle document (Researcher Copilot).

## What changed in this final set

| Change | Reason |
|---|---|
| Redrawn as an implementation flow plus capability groups with no sequence arrows | Your decision |
| ROW-038 (field-book migration) moved in from Diagram 2 | Your decision |
| Each feature appears once, in the flow or in a capability group | Audit script |

## Diagram

```mermaid
flowchart TB
  subgraph IMP["Implementation sequence (Vendor, System Administrator, IT)"]
    direction LR
    IM_a["Plan project, discovery and configuration; run testing and user acceptance<br/>(ROW-231)"]
    IM_b["Configure the system without vendor help: roles, forms, templates, workflows<br/>(ROW-224)"]
    IM_c["Migrate and validate legacy data, including the existing field-book system<br/>(ROW-203, ROW-038)"]
    IM_d["Train users; deliver documentation and online help<br/>(ROW-229)"]
    IM_e["Go live; provide support, SLAs, releases and updates<br/>(ROW-230)"]
    IM_f["Support data ownership, open data licence, exit or archive<br/>(ROW-232)"]
    IM_a --> IM_b
    IM_b --> IM_c
    IM_c --> IM_d
    IM_d --> IM_e
    IM_e --> IM_f
  end
  subgraph G1["Identity, access, security and audit"]
    direction TB
    S1["Single sign-on and identity federation, including MFA<br/>(ROW-210)"]
    S2["Roles, permissions and user profiles<br/>(ROW-212)"]
    S3["Restrict fields and rows by role<br/>(ROW-213)"]
    S4["Authentication, password and session policies<br/>(ROW-218)"]
    S5["Audit trail of data changes and access<br/>(ROW-214)"]
    S6["Security controls: encryption, NIST and CIS, vulnerability testing<br/>(ROW-216)"]
    S7["Compliance certifications: FedRAMP or TX-RAMP, SOC 2, ISO 27001<br/>(ROW-217)"]
    S8["Audit readiness for federal and project-document audits<br/>(ROW-215)"]
  end
  subgraph G2["Hosting and operations"]
    direction TB
    H1["Cloud SaaS hosting with US data residency and tenant isolation<br/>(ROW-219)"]
    H2["Backup, disaster recovery and recovery objectives<br/>(ROW-220)"]
    H3["Availability, performance and scalability<br/>(ROW-221)"]
    H4["Monitoring, error logging and administration<br/>(ROW-222)"]
    H5["Separate test, training and production environments<br/>(ROW-223)"]
  end
  subgraph G3["Data management"]
    direction TB
    D1n["Import and export with configurable mapping<br/>(ROW-202)"]
    D2n["Validation, duplicate and inconsistency flagging<br/>(ROW-204)"]
    D3n["Real-time synchronisation of data<br/>(ROW-205)"]
    D4n["Unique transaction identifiers and metadata<br/>(ROW-206)"]
    D5n["Records retention, archive and disposal<br/>(ROW-207)"]
    D6n["Public-records and Right-to-Know request support<br/>(ROW-208)"]
  end
  subgraph G4["Integration and configuration"]
    direction TB
    I1["Open APIs and data access (REST and JSON)<br/>(ROW-209)"]
    I2["Integration with enterprise and agency systems: finance, project tracking, city applications<br/>(ROW-211)"]
    I3["User-created forms, templates and standard reports<br/>(ROW-178)"]
    I4["Locked-down file structure for consistent data entry<br/>(ROW-169)"]
  end
  subgraph G5["Usability and accessibility"]
    direction TB
    U1n["Accessibility: Section 508, WCAG 2.1 AA, ADA<br/>(ROW-225)"]
    U2n["Browser and device compatibility<br/>(ROW-226)"]
    U3n["Ease of use: shortcuts, tool-tips, in-system help<br/>(ROW-227)"]
    U4n["Mobile and field access: view, update, upload<br/>(ROW-228)"]
  end
  classDef new fill:#fff8d6,stroke:#b8960c
  IMP ~~~ G1
  G1 ~~~ G2
  G2 ~~~ G3
  G3 ~~~ G4
  G4 ~~~ G5
  style IMP fill:#e8f1fb,stroke:#4a78b5
  style G1 fill:#e8f1fb,stroke:#4a78b5
  style G2 fill:#e8f1fb,stroke:#4a78b5
  style G3 fill:#e8f1fb,stroke:#4a78b5
  style G4 fill:#e8f1fb,stroke:#4a78b5
  style G5 fill:#e8f1fb,stroke:#4a78b5
```

## Source verification matrix

| Node | Evidence | Source and detail |
|---|---|---|
| IM_a to IM_f Implementation sequence | O | ROW-231, 224, 203, 229, 230, 232 and ROW-038 from Diagram 2. The sequence plan, configure, migrate, train, go live, exit is the original's; it is the only real flow among the platform diagrams |
| G1 Identity, access, security and audit | O | ROW-210, 212, 213, 218, 214, 216, 217, 215 |
| G2 Hosting and operations | O | ROW-219 to ROW-223 |
| G3 Data management | O | ROW-202, 204 to 208 |
| G4 Integration and configuration | O | ROW-209, 211, 178, 169 |
| G5 Usability and accessibility | O | ROW-225 to ROW-228 |

## Open items

1. Platform requirements are capabilities, not agency process; no agency SOP in the files describes them.
