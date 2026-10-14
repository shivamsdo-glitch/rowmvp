# Phase 4 — Negotiation, Agreement & Closing

**Source diagrams:** 4 (negotiation), 7 (payments & closing, MVP slice). **Primary roles:** Negotiator, ProjectManager, Finance, Admin. Largest phase — split into 4a/4b for review if needed.

## 4a — Offer & negotiation
- Pre-offer management review, funding check, offer threshold limits (256) — `OfferThresholds` calculation; negotiator assignment & notification (064); offer package & delivery tracking (066); offer/counter-offer/agreement generation and tracking (063); revised offer with appraisal summary statement (260); offer approval chain by dollar threshold (258).
- Negotiation with other interest holders/tenants — releases, subordinations (259, 262); preliminary & final settlement analysis (257); settlement & administrative settlement recording with justification (065); owner retention of improvements/salvage valuation (068).
- Impasse (067): record impasse letter + status `Impasse` and data needed for later condemnation hand-off. **No condemnation workflow.**
- Diary/notes reuse from Phase 1 (020).

## 4b — Agreement, closing, payment
- Agreement milestones & threshold alerts (081); agreement approval chain incl. board/ordinance step (261); e-signature (179 in-range feature, MVP = "signed" record + uploaded signed PDF, typed-name acknowledgement; no tablet signing).
- Acquisition paths: donation (265), developer conveyance (266), early acquisition (267) as workflow variants of Phase 1 config (071); possession & use agreement + acquisition incentive (264); environmental site assessment ordering/tracking (263 — simple order/status record); easement management (078); key/property-item turnover (070).
- Audit trail & documentation for federal review (079): append-only audit log for approvals, offers, agreements, payments, viewable per parcel.
- **Payments & closing (Diagram 7 slice):** compensation/settlement/payment tracking per parcel (107); payment request with multi-payee and threshold approval (108); payment method check/EFT as a recorded attribute (109); closing scheduling & completion (110); closing package approved as to form/notary/recording checkboxes (288, 289 as checklist); **Parcel Closed** status after recording + file review (268). No financial-system integration (111) — Finance marks "paid" manually.
- Calculations: `OfferThresholds`, `SettlementVariance` (offer vs. JC vs. agreed), `AgreementApprovalChain`, `PaymentAllocation` (multi-payee splits sum to total) — all unit-tested.

**Frontend:** parcel Negotiation tab (offers timeline, counters, settlement analysis), approvals inbox, agreement view, closing checklist, payment request form, Finance queue.

## Requirements
ROW-063–068, 070, 078, 079, 081, 107–110, 256–268, 179(minimal).

## Deferred
069 (D13), 082 handoff to property mgmt, 083 lease-back, 111 fin-system integration, 112 vendor code auto-pop, 284–290 (fund reservation, court payments, tax proration, federal billing).

## Exit checklist
- [ ] Approved JC → offer → counter → settlement → agreement → approvals → closing → payment → Parcel Closed, end to end
- [ ] Approval chain varies correctly by amount; self-approval prevented
- [ ] Impasse sets status and blocks closing; no condemnation code present
- [ ] Audit log entries for every state transition
