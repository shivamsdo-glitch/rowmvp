# ADR 0026 — Relocation case workflow (Diagram 5, no benefit calculation)

**Status:** accepted and built 2026-10-22 (design agreed with the user the same day). Supersedes the scope note of ADR 0020 and the "relocation is deferred to v2" line in CLAUDE.md **for the Relocation Agent's case workflow only**. Condemnation, property management and utilities stay deferred.

## Decisions taken with the user (U)
1. **Lift the deferral** for relocation as a real process on top of the Relocation tab (ADR 0020).
2. **Depth: case workflow, no benefit calculation.** Covers Diagram 5 nodes ROW-085, 086, 092–097, 269–277: planning, notices (GIN, eligibility, 90-day, 30-day), advisory interview, housing study and decent-safe-sanitary inspection, last resort, eligibility exceptions, claim-expiry alert, advance payment, payment request, denial and appeal, vacate hand-off flag. Moving, purchase-supplement and rental-assistance **amounts are typed by the agent**; no formula is built (ROW-087–091 calculation stays out).
3. **Benefit limits and rates:** none seeded. If a calculation is added later it reads agency-entered data.
4. **Payments reuse the approval chain.** A payment request routes through a configurable chain (threshold bands → ordered roles, `ApprovalChain.Evaluate`), then Finance marks it paid. The bands are persisted per tenant (the pure calculation existed; its config did not) and edited on the Configuration page. Default bands are placeholders, like `DefaultParcelRules.DeadlineRules`. The person who raised a request cannot sign it (ADR 0012).
5. **Acquired rule.** Each relocation is linked to one **ownership interest** on the parcel (a `PartyLink`), which carries a `DisplacementRequired` flag. When every flagged interest has a relocation whose **Relocation complete** date is set, the parcel status is set automatically to **Acquired**. A parcel with no flagged interests is unaffected. Manual status changes stay possible.
6. **"Acquired" is a new default status** in the parcel status list (add-only seed for new tenants; existing tenants add it in the Library). The auto-set looks it up by name; if the agency removed it, nothing changes and the parcel summary shows a warning.
7. **Complete date:** "Relocation complete" is the trigger; "Relocation complete 1" stays a plain record date and remains flagged to the agency (ADR 0020).

## Also updated
Dashboard (waiting/done for the Relocation Agent), Program view and Team roll-up (relocation step and team, stuck cases, counts), a Relocation work screen at `/relocations`, the parcel Summary (relocation progress), the Help center, and the Relocation Agent's permissions.

## Open items for the agency (flag, don't guess)
- Appeal time limit and form (60 days in the Austin procedure) — kept as agency data.
- Whether "Relocation complete 1" has a distinct meaning.
- Federal dollar limits for replacement-housing payments (the only brochure in the files is dated).
