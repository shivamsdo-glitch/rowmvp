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
| AT-NG-16 | Capture an owner, their details and their W-9 number, and tidy up after a mistake | On Parties create a party (blank name, then a bad email refused, then valid); on a parcel link them as a fee owner found by search with 100%; try the same person in the same role again; enter an SSN on the party; remove the link and delete the party | Name and email errors beside the fields; duplicate refused with a plain message; the number shows only as `***-**-6789`; link and party removed | Full control of every parties endpoint (create, link, edit, tax info, remove, delete); no response contains the number | ✅ | E2E `AT-NG-16` ×2; API `PartyApiTests` role matrix |
| AT-NG-17 | Keep the negotiation diary | Open a parcel's Diary tab. Try an empty form, a future date, then log a call and a meeting (who, how, who started it, what was said, what was promised); the project manager opens the same diary; remove a mistaken entry | Newest first; a Print diary button; the project manager sees the entries but no form or remove buttons | Dates are counted in the project's time zone; the project manager's write is refused (403); removal is audited | ✅ | E2E `AT-NG-17`; API `ParcelRecordApiTests`; frontend `records.test.tsx` |
| AT-NE-40 | Keep the contact log of a parcel | Open a parcel's Contact log tab. Try an empty form; log a contact (date and time, mode, contacted by, contact person, notes, linked records); change it; attach a letter; remove the attachment and the contact | Each contact shows its id CL-<parcel>-001, 002...; the form shows what is missing; the attachments panel appears when a contact is being changed | Ids count up per parcel and are not reissued when an earlier entry is removed; ten at once all get different ids; times are stored in UTC; over-long text and a linked record without an identifier are refused (400); a parcel that was split or combined cannot be changed (409) | ✅ | E2E `AT-NE-40` ×2; API `ParcelTabsApiTests`; frontend `parcel-tabs.test.tsx`; unit `ParcelTabsTests` |
| AT-NG-26 | Read rights of entry, reviews, reports and requests | Open a parcel's Right of entry and Reviews & reports tabs and the Requests page | Everything is visible with no forms and no Approve, Reject, Write or Record buttons | Reads allowed; plan reviews and new requests are refused (403) | ✅ | E2E `AT-NG-26`; API `AcquisitionApiTests` (role matrix) |

## Jobs that arrive with later phases

| Id | Job | Phase | Success looks like |
|---|---|---|---|
| AT-NG-20 | Build an offer package from approved just compensation and deliver it | 4 | Offer cannot be created until compensation is approved; delivery is tracked |
| AT-NG-21 | Record a counter-offer and the settlement analysis | 4 | Offer, counter and agreed figures with the variance against just compensation |
| AT-NG-22 | Record an administrative settlement | 4 | Needs a written justification and goes to approval |
| AT-NG-23 | Send the agreement through its approval chain and record the signature | 4 | Approvers depend on the amount; "signed" recorded with the uploaded signed document |
| AT-NG-24 | Complete the closing checklist | 4 | Checklist done and recorded → parcel becomes Closed |
| AT-NG-25 | Record an impasse | 4 | Parcel status is Impasse with the data for a later condemnation hand-off; closing is blocked |
| AT-NE-41 | Update the closing dates on a parcel's title and closing record | Open a parcel's Title and closing tab where the title researcher has started the record; set the anticipated closing date; save; look for a way to remove the record | The record shows the title researcher's values; the save says it worked; there is no Remove button | The negotiator may create and change the record but not remove it (403) | ✅ | E2E `AT-NE-41`; API `TitleClosingApiTests`; frontend `parcel-tabs.test.tsx` |
| AT-NE-42 | Keep the negotiation record of a parcel | Open a parcel's Negotiation tab. Add a status note, an agent, a right of entry (try a letter delivered before it was mailed), an offer, an administrative settlement, the agreement amount and the JAT and PUA dates (try a JAT response before the submission), save, attach a file, reload, remove an offer, save again, remove the record | One form with an ID assigned on the first save; the parcel's own details show read-only; the agent's company, phone and email come from the Library; the first save reads "Save negotiation", later ones "Save changes" | One record per parcel (ten first saves at once leave one); dates that go backwards, a negative amount, an empty note or agent and more than 50 rows are refused (400); the grids are replaced as sent | ✅ | E2E `AT-NE-42`; API `NegotiationApiTests`; frontend `parcel-tabs.test.tsx`; unit `NegotiationTests` |

## Changelog
- Phase 1.5 slice 7a: AT-NE-42 automated (the Negotiation tab). ADR 0019.
- Phase 1.5 slice 6b: AT-NE-41 automated (updating the closing dates). ADR 0018.
- Phase 1.5 slice 5b: AT-NE-40 automated (the Contact Log tab, with attachments). ADR 0017.
- Phase 1 slice 3c: AT-NG-26 automated (read-only access to acquisition records).
- Phase 1 slice 3b: AT-NG-17 automated (negotiation diary).
- Phase 1 slice 3a: AT-NG-16 automated (parties).
- Initial: AT-NG-10..15 automated; 20..25 specified.
