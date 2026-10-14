# Finance — acceptance cases

**Who:** Finance / Accounts Payable. Processes payments and tracks expenditure; approves payments within their authority.
**Sees:** full control of Payments; approves (Approvals: create/read/update); read-only on Projects, Parcels, Parties, Negotiations, Agreements and Closing. Nothing of Title, Survey or Appraisals, and no Administration.
**Also run:** every case in [Everyone.md](Everyone.md) as this role.

## Jobs possible today

| Id | Job | Steps | Expected: on screen | Expected: server and data | Status | Run by |
|---|---|---|---|---|---|---|
| AT-FN-10 | Keep payment paperwork | Attach a payment request and a W-9 to a parcel's payment record; remove the request | – | Both stored; removal succeeds (204); only the W-9 remains | ✅ | E2E `AT-FN-10`; API `RoleAcceptanceApiTests` |
| AT-FN-11 | Check the deal behind a payment | Open the offer, agreement and closing documents; try to attach to or remove from them | – | All three open; every change refused (403) | ✅ | E2E `AT-FN-11`; API matrix |
| AT-FN-12 | Stay out of title and valuation | Try to list or open title, survey and appraisal records | – | All refused (403), even when the document id is known | ✅ | E2E `AT-FN-12` |
| AT-FN-13 | A negotiator's supporting papers reach me | The negotiator adds supporting documents to a payment; I read them | – | Visible to me; the negotiator cannot remove mine | ✅ | API `RoleAcceptanceApiTests` |

## Jobs that arrive with later phases

| Id | Job | Phase | Success looks like |
|---|---|---|---|
| AT-FN-20 | Create a payment request | 4 | Multiple payees; the split must add up to the total or it is refused |
| AT-FN-21 | Approve a payment by threshold | 4 | The right approvers for the amount; nobody can approve their own request |
| AT-FN-22 | Mark a payment paid | 4 | Status and method recorded; the parcel moves toward Closed |
| AT-FN-23 | Work the queue of payments | 4 | Pending, approved and paid are separated; counts match the lists |

## Changelog
- Initial: AT-FN-10..13 automated; 20..23 specified.
