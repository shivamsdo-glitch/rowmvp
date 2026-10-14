# Negotiator — acceptance cases

**Who:** the ROW Agent. Prepares and delivers offers, negotiates with owners and interest holders, records settlements, obtains approvals and signatures, and takes the parcel through closing.
**Sees:** full control of Negotiations, Agreements and Parties (owners); create/read/update on Closing and Payments (they can add but not delete); read-only on Projects, Parcels, Title, Appraisals and Approvals. No Survey, no Administration.
**Also run:** every case in [Everyone.md](Everyone.md) as this role.

## Jobs possible today

| Id | Job | Steps | Expected: on screen | Expected: server and data | Status | Run by |
|---|---|---|---|---|---|---|
| AT-NG-10 | Keep the deal paperwork | Attach the offer letter to the negotiation and the signed agreement to the agreement record | – | Both stored under the right records and listed | ✅ | E2E `AT-NG-10`; API `RoleAcceptanceApiTests` |
| AT-NG-11 | Add to a closing record, never erase from it | Attach the checklist, then the recording receipt; try to remove the checklist | – | Both attach (201); removal refused (403); both remain listed | ✅ | E2E `AT-NG-11` |
| AT-NG-12 | Capture owner paperwork | Attach a W-9 to an owner record, then remove it | – | Full control: stored, then removed (204) and gone from the list | ✅ | E2E `AT-NG-12` |
| AT-NG-13 | Read upstream work, never alter it | Open the title commitment and the appraisal report; try to remove or add; try to look at survey records | – | Title and appraisal open; changes refused; survey refused entirely (403) | ✅ | E2E `AT-NG-13`; API matrix |
| AT-NG-14 | Finance sees the deal | Finance opens my offer, agreement and closing documents | – | Readable by Finance; Finance cannot attach to them | ✅ | API `RoleAcceptanceApiTests` |
| AT-NG-15 | Add supporting papers to a payment | Attach supporting documents to a payment record; try to remove Finance's request | – | Attach allowed; removal refused (payments are create/read/update for me) | ✅ | API `RoleAcceptanceApiTests` |

## Jobs that arrive with later phases

| Id | Job | Phase | Success looks like |
|---|---|---|---|
| AT-NG-20 | Build an offer package from approved just compensation and deliver it | 4 | Offer cannot be created until compensation is approved; delivery is tracked |
| AT-NG-21 | Record a counter-offer and the settlement analysis | 4 | Offer, counter and agreed figures with the variance against just compensation |
| AT-NG-22 | Record an administrative settlement | 4 | Needs a written justification and goes to approval |
| AT-NG-23 | Send the agreement through its approval chain and record the signature | 4 | Approvers depend on the amount; "signed" recorded with the uploaded signed document |
| AT-NG-24 | Complete the closing checklist | 4 | Checklist done and recorded → parcel becomes Closed |
| AT-NG-25 | Record an impasse | 4 | Parcel status is Impasse with the data for a later condemnation hand-off; closing is blocked |

## Changelog
- Initial: AT-NG-10..15 automated; 20..25 specified.
