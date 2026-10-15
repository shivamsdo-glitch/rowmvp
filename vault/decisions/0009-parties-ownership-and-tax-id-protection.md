# ADR 0009 — Parties: owners, interests, contacts, tenants, abutters and tax ID protection

**Status:** accepted (Phase 1, slice 3a). Scope: ROW-014, 015, 016, 017, 018.

## The model
- A **`Party`** is a person or organization, entered **once**: name, legal name (as on the vesting deed), contact details, address, notes. It is tenant-owned like every aggregate (ADR 0001).
- A **`PartyLink`** says how one party relates to **one tract or one parcel** (exactly one of `parent_tract_id`, `parcel_id`; enforced by the check constraint `ck_party_links_one_target`). It carries the **role**, an optional **share percent**, notes, and for tenants the lease-on-file flag, lease details and insurance details.
- Roles fall into the four lists the requirements name: **Ownership** (fee owner, easement holder, lienholder, trustee, life-estate holder, other interest — ROW-014), **Contact** (attorney, bank, owner designee, owner representative, other — ROW-015), **Occupant** (tenant, occupant — ROW-016), **Abutter** (abutter, interested party — ROW-017). The role set is a fixed enum for now; if an agency needs its own list it becomes data like parcel statuses (ADR 0008).
- One party holds a given role once per tract or parcel (partial unique indexes; the API answers 409 `party_link_exists`). The same party may hold different roles, and may appear on many parcels.
- A party that is still linked anywhere cannot be deleted (409 `party_in_use`; the foreign key is `RESTRICT` as the backstop).

## Owners are recorded once, on the tract (decision D)
Ownership belongs to the **parent tract of record** (ROW-233). A parcel that has a tract **inherits** its tract's owners and interest holders on screen and in the API (`source: "Tract"`), and **cannot take owner roles itself** (400 `owners_on_tract`), so a change of ownership is made once. Contacts, tenants and abutters are recorded on the parcel (`source: "Parcel"`) because they differ parcel to parcel. A parcel with **no tract** takes owners directly.

Open domain question (flagged, not resolved): whether a project's parcels should always sit on a tract, so owners are never entered at parcel level. Today both are allowed.

## Shares (ROW-014)
Undivided interests carry a share. A share is more than 0 and at most 100 with at most four decimals, and the shares of **one role on one tract or parcel** cannot add up to more than 100 (409 `share_exceeds_whole`, naming the unclaimed amount). Only fee owners, easement holders and life-estate holders have a share. The rule is a pure function (`OwnershipShares`) used by the handlers. Because "check then insert" could let two simultaneous requests both pass, **adding, changing or removing a share also touches the tract's or parcel's row**, whose `xmin` concurrency token makes the second writer fail (409 `party_changed`): the stored total can never pass 100, even across servers.

## Split and combine keep the people (ROW-007 with ROW-014..017)
A split or combine creates new parcels, but the people around the land do not change. The new parcels **take over the source parcels' own links** (the tract's links reach them through the tract). Combining parcels that share a party in the same role gives the new parcel that link once. A superseded parcel keeps its own links for the record.

## Tax ID protection (ROW-018)
The number on a W-9 is the most sensitive field in the product, so:
- **Write-only.** The API accepts it (`PUT /parties/{id}/tax-info`) and never returns it. Responses carry the **type**, a **mask** built from the last four digits (`***-**-6789`; EIN `**-***6789`), a `hasTaxId` flag, the W-9 received date and the 1099-reportable flag. The screen's number field is a password-type input that empties after saving.
- **Encrypted at rest.** AES-256-GCM with a fresh random nonce per value; the **party id is authenticated data**, so a value copied onto another party's row fails to open. Stored as `v1.` + base64(nonce, tag, cipher text). There is no plain-text column. The last four digits are kept separately only to draw the mask.
- **Key.** `Security:TaxIdKey` (environment variable `Security__TaxIdKey`), base64 of 32 random bytes (for example `openssl rand -base64 32`). It must be supplied everywhere except Development, which uses a fixed dev-only key; the app **refuses to start** without a valid key (same rule as the JWT signing key, ADR 0002). **Losing the key makes stored numbers unreadable**; it is held in the agency's secret store and backed up with the database. Rotation (re-encrypt under a new key; the `v1.` prefix leaves room for key versions) is not built; nothing in the product ever decrypts for display, so there is no reveal feature either. Those are for the agency to decide when payments (Phase 4) need the number.
- **Never in logs or the audit trail.** `TaxIdProtected` and `TaxIdLast4` are in the audit interceptor's secret set: the fact of a change is recorded (`[redacted]`), never a value, not even the encrypted form. The command's `ToString()` is overridden so a logged or exception-printed request cannot print the number, and the body type of the controller does the same.
- **Validated without echo.** Digits, spaces and hyphens only; nine digits; an SSN cannot have area 000, 666 or 9xx, group 00 or serial 0000; an ITIN starts with 9 and has a valid group; an EIN cannot start 00. The messages never repeat the input. The W-9 received date cannot be in the future **in the agency's time zone** (ADR 0008).
- **Roles.** Setting, replacing or clearing the number needs update rights on Parties (Project Manager, Negotiator, Administrator by default). Everyone who can read parties sees only the mask and that a number is on file (Finance needs to know a W-9 exists before paying, and never sees more).
- Changing the type of an existing number without entering the number again is refused (400 `tax_id_type_change`).

## Permissions
Parties is its own page key (`parties`, already in the access matrix since Phase 0): Project Manager, Negotiator and Administrator have create/read/update/delete; Title Examiner, Appraiser and Finance read only. Linking, editing a link and removing a link follow create/update/delete on that page, not on Parcels, so the Negotiator (who captures owners, Diagram 1) works without being able to edit parcels.

## API
`/api/v1/parties` (list with search, kind filter and paging; get; usage; create; update; delete), `/parties/{id}/tax-info` (PUT) and `/parties/{id}/tax-info/number` (DELETE), `/parcels/{id}/parties` and `/projects/{id}/tracts/{tractId}/parties` (GET, POST), `/party-links/{id}` (PUT, DELETE). A superseded parcel takes no new people (409 `parcel_not_active`).

## Verified by
Unit (`TaxIdAndSharesTests`, `TaxIdProtectorTests`, `PartyTests`), real-Postgres integration (`PartyApiTests`: jobs, 6 roles in both directions, races, tenancy, constraint and index checks, audit, hostile input, startup key), frontend (`parties.test.tsx`, axe), and Playwright (`parties.spec.ts`: AT-PM-22, AT-NG-16, AT-TE-15, AT-AP-15, AT-FN-15, AT-AD-27, AT-X-35, AT-X-36).
