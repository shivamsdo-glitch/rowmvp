# Appraiser — acceptance cases

**Who:** the appraisal team and approving official. Scopes and performs appraisals, reviews them, determines value and establishes just compensation.
**Sees:** full control of Appraisals; approves (Approvals: create/read/update); read-only on Projects, Parcels, Parties, Title and Survey. Nothing of Negotiations, Agreements, Closing or Payments, and no Administration.
**Also run:** every case in [Everyone.md](Everyone.md) as this role.

## Jobs possible today

| Id | Job | Steps | Expected: on screen | Expected: server and data | Status | Run by |
|---|---|---|---|---|---|---|
| AT-AP-10 | Hand off the appraisal report | Attach the appraisal report to the parcel's appraisal; the negotiator opens it | – | Stored; the negotiator downloads identical bytes; the negotiator cannot edit or remove it | ✅ | E2E `AT-AP-10`; API appraisal hand-off |
| AT-AP-11 | Read the title and survey package | Open the commitment and the plat; try to change them | – | Both open; attach and remove on title and survey are refused (403) | ✅ | E2E `AT-AP-11` |
| AT-AP-12 | Stay out of negotiation and money | Try to list or attach to offers, agreements, closing and payments | – | Every one refused (403) | ✅ | E2E `AT-AP-12`; API matrix |
| AT-AP-13 | A title examiner can see my outcome | The title examiner opens my appraisal documents | – | Readable; not removable | ✅ | API `RoleAcceptanceApiTests` |
| AT-AP-15 | See who owns a parcel and whether a tax ID is on file, without being able to change it | Open Parties, search, open a party; open a parcel's Parties tab and a tract's owners panel as the Appraiser | The people are listed; "On file" and the masked number show; no New party, Edit, tax form, Add, Remove or Delete controls | Every create, edit, tax-info, clear, delete and link call is refused (403) and the data is unchanged; the number never appears in any response | ✅ | E2E `AT-AP-15`; API `PartyApiTests` role matrix |

## Jobs that arrive with later phases

| Id | Job | Phase | Success looks like |
|---|---|---|---|
| AT-AP-20 | Receive an appraisal request and be assigned the parcel | 3 | Appears in my queue only when the package is complete; assignment (internal or external) is recorded |
| AT-AP-21 | Submit an appraisal | 3 | Type, dates and valuation basis saved; several appraisals per parcel keep their history |
| AT-AP-22 | Review an appraisal | 3 | Accept or reject; rejection **requires** a justification; full history retained |
| AT-AP-23 | Establish just compensation | 3 | The approval chain for the amount is applied (different amounts → different approvers); nobody can approve out of order |
| AT-AP-24 | Protect an approved value | 3 | An approved appraisal cannot be edited; a revision creates a new version and the old one stays visible |

## Changelog
- Phase 1 slice 3a: AT-AP-15 automated (read-only access to parties).
- Initial: AT-AP-10..13 automated; 20..24 specified.
