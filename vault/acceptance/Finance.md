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
| AT-FN-15 | See who owns a parcel and whether a tax ID is on file, without being able to change it | Open Parties, search, open a party; open a parcel's Parties tab and a tract's owners panel as the Finance user | The people are listed; "On file" and the masked number show; no New party, Edit, tax form, Add, Remove or Delete controls | Every create, edit, tax-info, clear, delete and link call is refused (403) and the data is unchanged; the number never appears in any response | ✅ | E2E `AT-FN-15`; API `PartyApiTests` role matrix |
| AT-FN-24 | Approve or reject acquisition requests | Open Requests; approve one entered by someone else; reject another without a reason (refused), then with one | Approve and Reject on each waiting request; the decision and note show on the card | A decision needs the Approvals page; rejection needs a note (400); a decided request cannot be decided again (409) | ✅ | E2E `AT-PM-31` (finance half); API `AcquisitionApiTests`; frontend `acquisition.test.tsx` |
| AT-FN-25 | Approve or reject a staff report or certificate of necessity | Open a parcel's Reviews & reports; reject a submitted report without a note (refused) and with one; approve the resubmitted one | The decision note shows on the report; an approved report is final | Only a submitted report can be decided (409); the submitter cannot decide their own (403) | ✅ | E2E `AT-PM-33` (finance half); API `AcquisitionApiTests`; frontend `acquisition.test.tsx` |

## Jobs that arrive with later phases

| Id | Job | Phase | Success looks like |
|---|---|---|---|
| AT-FN-20 | Create a payment request | 4 | Multiple payees; the split must add up to the total or it is refused |
| AT-FN-21 | Approve a payment by threshold | 4 | The right approvers for the amount; nobody can approve their own request |
| AT-FN-22 | Mark a payment paid | 4 | Status and method recorded; the parcel moves toward Closed |
| AT-FN-23 | Work the queue of payments | 4 | Pending, approved and paid are separated; counts match the lists |

## Changelog
- Phase 1 slice 3c: AT-FN-24, 25 automated (approving requests and staff reports).
- Phase 1 slice 3a: AT-FN-15 automated (read-only access to parties; Finance sees whether a payee's W-9 is on file).
- Initial: AT-FN-10..13 automated; 20..23 specified.
