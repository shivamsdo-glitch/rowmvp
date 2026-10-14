# Diagram 8 – Property Management, Leases, Inspections and Disposal

Paste the block under **Diagram** into https://mermaid.live. It renders without errors (mermaid-cli 11.12). Size: about 6316 characters.

**Legend:** blue = ROW staff and approvers, orange = external party, green = system or finance. **Yellow = a new requirement (ROW-233 onward; see New_Requirements.md).** **Dashed purple outline = agency-specific item.** Evidence codes: **V** stated in a project source, **A** shown in your agency SOPs or workflows, **O** carried from your original diagram or feature list, **D** my design decision, **U** your decision.

Source short names: NDOT (Nevada DOT ROW Manual 2025), HDOT (Hawaii DOT HWY-R Manual 2025), ATP (Austin Transit Partnership manuals), TH (Training Handbook), TBW (Tampa Bay Water guideline), REM, GP, EF, PA SOP, ED SOP, Donation, Developer, Early, Settlement and RFF SOPs (Orange County, Florida), P1, P2 and Conveyance workflows, PUA matrix (City of Virginia Beach), IRWIN manuals, lifecycle document (Researcher Copilot).

## What changed in this final set

| Change | Reason |
|---|---|
| Added activation after title vests and occupant notice, inventory verification, initial occupant rental, surveillance | NDOT 13.601-606 |
| Disposal rebuilt: excess identification, committee, legal description, valuation and minimum bids, environmental clearance, federal approval, board approval, disposal methods | NDOT 13.8; Tampa Bay Water guideline |
| Added occupancy permit handling | NDOT ch.10 |
| Demolition and securing (ROW-098) moved to Diagram 10; railroad crossing agreements (ROW-145) moved to Diagram 9 | Your decisions |
| NH-specific rail data kept with the agency tag; reports moved to Diagram 13 | Your decisions |

## Diagram

```mermaid
flowchart TB
  subgraph P1["Property Manager – activation and inventory"]
    direction LR
    P1_a["Receive hand-off: Acquisition, Ownership and Occupancy Data form and inventory of improvements; take over occupied property only after relocation notice or approved rent-back<br/>(ROW-082, ROW-291)"]
    P1_b["Create inventory record with location, description and structures, linked to acquisition history<br/>(ROW-117)"]
    P1_c["Verify inventory of improvements; complete property management checklist and diary<br/>(ROW-293)"]
    P1_d["Set property lifecycle status from a configurable list: active, surplus, leased<br/>(ROW-118)"]
    P1_e["Account for area: acquired, remaining, sold, leased<br/>(ROW-119)"]
    P1_f["Apply legacy parcel ID or ROW numbering<br/>(ROW-122)"]
    P1_g["Inventory remnants and excess land, including post-construction remnants<br/>(ROW-120)"]
    P1_h["Detect duplicate surplus records<br/>(ROW-121)"]
    P1_a --> P1_b
    P1_b --> P1_c
    P1_c --> P1_d
    P1_d --> P1_e
    P1_e --> P1_f
    P1_f --> P1_g
    P1_g --> P1_h
  end
  subgraph P2["Property Manager – search and use"]
    direction LR
    P2_a["Search inventory by district, county, route, project, owner, section, township and range<br/>(ROW-123)"]
    P2_b["Link property to use agreements and work orders where structures are managed<br/>(ROW-124)"]
    P2_a --> P2_b
  end
  subgraph P4["Property Manager – initial use of acquired property"]
    direction LR
    P4_a["Rent or lease acquired improvements until construction to maximise project credit; initial occupant may rent back, with a rent-free period before a rental agreement<br/>(ROW-292)"]
  end
  subgraph O["Lessee / Tenant (external)"]
    direction LR
    O_a["Request lease, agreement or property; provide insurance and licenses<br/>(ROW-133, ROW-129)"]
    O_b["Pay rent<br/>(ROW-126)"]
    O_a --> O_b
  end
  subgraph P3["Property Manager – leases and agreements"]
    direction LR
    P3_a["Advertise available leasable and use-agreement property publicly<br/>(ROW-132)"]
    P3_b["Create lease; apply compliance factors and expiry alerts for insurance and licenses<br/>(ROW-125, ROW-129)"]
    P3_c["Set rent roll, payment schedule and invoicing<br/>(ROW-126)"]
    P3_d["Recalculate rent from index data at renewal; use MLS market rent<br/>(ROW-127, ROW-128)"]
    P3_e["Manage use agreements: temporary use and occupancy, encroachment, alternative use<br/>(ROW-130)"]
    P3_f["Manage franchise agreements, general-use and access permits; process occupancy permit applications, revocations and encroachments<br/>(ROW-131, ROW-295)"]
    P3_g["Create annual letters<br/>(ROW-134)"]
    P3_a --> P3_b
    P3_b --> P3_c
    P3_c --> P3_d
    P3_d --> P3_e
    P3_e --> P3_f
    P3_f --> P3_g
  end
  subgraph F["Finance / Accounts Payable"]
    direction LR
    F_a["Bill tenants, track payments against balance due, record lease revenue<br/>(ROW-126, ROW-143)"]
  end
  subgraph I["Inspector"]
    direction LR
    I_a["Schedule routine inspections; annotate and upload photos<br/>(ROW-136)"]
    I_b["Survey vacant property at regular intervals; arrange security where needed<br/>(ROW-294)"]
    I_c["Log miles driven; post to finance<br/>(ROW-137)"]
    I_d["Raise maintenance work order<br/>(ROW-138)"]
    I_a --> I_b
    I_b --> I_c
    I_c --> I_d
  end
  subgraph DS["District / Maintenance Staff"]
    direction LR
    DS_a["Complete maintenance work; update work order<br/>(ROW-138)"]
  end
  subgraph S["Property Manager – excess property and disposal"]
    direction LR
    S_a["Identify excess: R/W plan property schedule, post-construction review or public request; research maps and title<br/>(ROW-139)"]
    S_b["Review with affected units; Excess Property Committee or board declares surplus<br/>(ROW-296)"]
    S_c["Prepare legal description and survey-grade sketch<br/>(ROW-297)"]
    S_d["Establish fair market value and apply minimum-bid rules; obtain broker opinion, rent study or surplus appraisal<br/>(ROW-054, ROW-298)"]
    S_e["Obtain environmental clearance<br/>(ROW-299)"]
    S_f["Coordinate federal approval where federal funds were used<br/>(ROW-140)"]
    S_g["Obtain board approval<br/>(ROW-300)"]
    S_h["Dispose: auction or sealed bid, direct sale, relinquishment, abandonment, adjacent owner, member government or donation<br/>(ROW-140, ROW-301)"]
    S_i["Record sale, proceeds and sold property; keep searchable by quarter and year<br/>(ROW-141, ROW-143)"]
    S_a --> S_b
    S_b --> S_c
    S_c --> S_d
    S_d --> S_e
    S_e --> S_f
    S_f --> S_g
    S_g --> S_h
    S_h --> S_i
  end
  subgraph FH["FHWA / Federal Oversight (external)"]
    direction LR
    FH_a["Approve disposal of federally funded ROW<br/>(ROW-140)"]
  end
  subgraph C["Property Manager – compliance"]
    direction LR
    C_a["Track compliance: annual inventory, state buildings, surplus procedures, environmental clearance<br/>(ROW-142)"]
  end
  subgraph R["Rail Property Staff (agency-specific: NH)"]
    direction LR
    R_b["Preserve existing rail data: valuation maps, Rail Access database<br/>(ROW-146)"]
    R_c["Use rail map layers: valuation maps, town property maps, trails, railroad layers<br/>(ROW-148)"]
    R_d["Integrate with named NH systems<br/>(ROW-149)"]
    R_b --> R_c
    R_c --> R_d
  end
  P1 -->|"Inventory ready"| P2
  P2 -->|"Parcel in service"| P4
  P4 -->|"Property available"| O
  O -->|"Lease or agreement request"| P3
  P3 -->|"Invoices"| F
  P3 -->|"Properties to inspect"| I
  I -->|"Work order"| DS
  DS -->|"Property no longer needed"| S
  S -->|"Approval request"| FH
  FH -->|"Disposal approved"| C
  C -->|"Rail items (NH only)"| R
  classDef new fill:#fff8d6,stroke:#b8960c
  classDef agency stroke-dasharray:5 4,stroke:#7a4f9a
  class P1_a,P1_c,P4_a,P3_f,I_b,S_b,S_c,S_d,S_e,S_g,S_h new
  class R_b,R_c,R_d agency
  style P1 fill:#e8f1fb,stroke:#4a78b5
  style P2 fill:#e8f1fb,stroke:#4a78b5
  style P4 fill:#e8f1fb,stroke:#4a78b5
  style O fill:#fdf0e0,stroke:#c07a1f
  style P3 fill:#e8f1fb,stroke:#4a78b5
  style F fill:#eaf6ea,stroke:#4a8f4a
  style I fill:#e8f1fb,stroke:#4a78b5
  style DS fill:#e8f1fb,stroke:#4a78b5
  style S fill:#e8f1fb,stroke:#4a78b5
  style FH fill:#fdf0e0,stroke:#c07a1f
  style C fill:#e8f1fb,stroke:#4a78b5
  style R fill:#e8f1fb,stroke:#4a78b5
```

## Source verification matrix

| Node | Evidence | Source and detail |
|---|---|---|
| P1_a Hand-off, activation | V, O | ROW-082. NDOT 13.601-13.602 (starts after title vests; receives Acquisition, Ownership and Occupancy Data and Inventory of Improvements; does not take over occupied property until the Relocation Agent's Property Management Notice B). **ROW-291** |
| P1_b Inventory record | O | ROW-117 |
| P1_c Verify inventory, checklist | V | NDOT 13.602B, 13.604 (inventory verified at possession; move-in and move-out checklist). **ROW-293** |
| P1_d to P1_h Status, area, legacy ID, remnants, duplicates | O | ROW-118, 119, 122, 120, 121. No source for duplicate detection (ROW-121). Lifecycle status is a manual configurable list by your decision |
| P2_a, P2_b Search, link | O | ROW-123, ROW-124 |
| P4_a Initial use | V | NDOT 13.504 (rent or lease advance-acquired property to maximise project credits), 13.603 (initial occupant rent-free up to 60 days after close of escrow, then a rental agreement); HDOT Fair Rental (24.102(m)). **ROW-292** |
| O_a, O_b Lessee | O | ROW-133, 129, 126 |
| P3_a to P3_e, P3_g Leases and agreements | O | ROW-132, 125, 129, 126, 127, 128, 130, 134. NDOT 13.701-13.708 cover rental and use agreements. No source for annual letters or advertisement of leasable property |
| P3_f Permits | O, V | ROW-131. NDOT ch.10 (applications, processing, revocation, temporary occupancy, illegal encroachments). **ROW-295** |
| F_a Billing | O | ROW-126, ROW-143 |
| I_a Inspections | O, V | ROW-136. NDOT 13.605 |
| I_b Vacant property | V | NDOT 13.606 (regular inspection for damage, vandalism, unauthorised use; law enforcement or security where needed). **ROW-294** |
| I_c, I_d Mileage, work order | O | ROW-137, ROW-138. No source |
| DS_a Maintenance | O | ROW-138; NDOT 13.607 |
| S_a Identify excess | V, O | ROW-139. NDOT 13.801 (three ways: R/W plan property schedule, post-construction review, public request) |
| S_b Committee declares surplus | V | NDOT 13.802 (Excess Property Committee, quorum of four program areas); TBW (in-house review of other uses before the Board or General Manager declares surplus). **ROW-296** |
| S_c Legal description | V | NDOT 13.803. **ROW-297** |
| S_d Value, minimum bid | V, O | NDOT 13.804, 13.509 (fair market value; 90% minimum bid at auction, 100% for direct sale of Title 23 property). ROW-054, **ROW-298** |
| S_e Environmental clearance | V | NDOT 13.805A. **ROW-299** |
| S_f Federal approval | V, O | ROW-140. NDOT 13.805B, 13.508 (23 CFR 710.403, 710.405; approval can be delegated except on the Interstate) |
| S_g Board approval | V | NDOT 13.806; TBW. **ROW-300** |
| S_h Disposal methods | V, O | NDOT 13.807, 13.510-13.511; TBW (sealed bid, advertisement, adjacent owner, member government, other agency or charity). ROW-140, **ROW-301** |
| S_i Record sale | O | ROW-141, ROW-143 |
| FH_a, C_a Federal approval, compliance | O | ROW-140, ROW-142 |
| R_b to R_d Rail data (agency-specific) | O | ROW-146, 148, 149. Dashed outline marks NH-specific systems |

## Moved out of this diagram

| Feature | Now in |
|---|---|
| ROW-145 railroad crossing agreements | Diagram 9 |
| ROW-135, 144, 147 reports | Diagram 13 |

## Open items

1. ROW-121, 134, 137, 138, 132 have no source evidence in the files; kept.
