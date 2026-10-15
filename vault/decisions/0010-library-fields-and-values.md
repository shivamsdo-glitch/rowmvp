# ADR 0010 — The Library: fields, types, allowed values and defaults

**Status:** accepted (between Phase 1 slices 3a and 3b). Decisions marked **U** were made by the user on 2026-10-15, from the product's reference screens.

## What it is
The Library is where an administrator defines, once, the fields the agency's forms use: a **name**, a **type** (Text, Number, Date, Yes/No, List) and, for a list, the **values** a person may choose. **Defaults are attached** when an agency is set up; where nothing has been configured, forms use the system defaults. Nothing in the product reads the Library yet except the parcel status list: the parcel form that uses the other fields is a later slice (U: "build only the Library for now").

## Model
- `LibraryField` (per agency): `Key` (stable, unique per agency), `Label`, `FieldType`, `IsSystem`, order. `LibraryValue`: `Value`, `NormalizedValue` (upper case; unique per field, so no two values are alike ignoring case, enforced by an index as the backstop for simultaneous adds), `IsActive`, order.
- **Values are switched off, never deleted** (the reference screen has an "Is Active" column and no delete), so a record that used a value stays readable.
- **System fields** are the ones the product refers to by key; their key and type are fixed (a rename is allowed). Fields an administrator defines are not system fields: their type can change only while they have no values.
- Controlled by the **Administration** page permission (administrator only today). Read access for other roles will be added when forms consume the lists.

## Defaults (U)
Parcel status (16 values: Inactive, In Design, Pre-Acquisition, Appraisal, Initial Offer, Final Offer, In Possession - PUA, In Possession - CLOSED, In Possession - NOD, Pending Condemnation, Petition Filed, Hearing Scheduled, Objections Filed, Deleted, Disposition In Progress, Disposition Complete); possession type (PUA, Closing, Notice of Deposit, Judgement); interest type (the ten shown on the reference screen, without its test row); agent type (Appraiser) and contact type (Broker) as the only values seen on the reference screens; CAD number, segment and priority as text. Seeding is **add-only** (`EnsureLibraryAsync`): an edited, switched-off or added value is never touched or restored. Only the values seen on the reference screens are seeded; the agency adds the rest.

## The parcel status list lives in one place
`parcel_status` is a Library field, but its values **are** the agency's parcel statuses (U: "Library manages it; no colours; add the default values"). Reading shows the status table; adding, renaming and switching off delegate to the existing `SaveParcelStatusCommand`, so every status rule still holds (unique names, the initial status must be active, one initial status). New statuses take one neutral colour; the colour column remains for the board but is not edited in the Library. **New parcels start as `Inactive`** (the reference screen shows a new parcel as Inactive).
This **replaces the earlier default status list** (Identified, Title & survey, Appraisal, Negotiation, Agreement signed, Closed, Impasse). Existing agencies keep the statuses they have (the provisioner only seeds an agency with no statuses); the demo data and tests were moved to the new names.

## Import and export (U: "Excel Import / Export")
CSV, in the browser: export writes the field's values and whether each is active; import reads one value per line (the first column; a heading row is skipped; up to 500 values), adds the new ones and **skips values already there**, and says how many of each. A real .xlsx reader is not included.

## Open items (flagged, not decided)
- **Two editors for the status list:** the Configuration page still has its own Parcel statuses card (with colours). It edits the same list. Whether to remove it, or keep colours there, is the user's call when the parcel form is rebuilt.
- **Two kinds of custom field:** ROW-011 parcel custom fields (Configuration) and Library fields overlap. They are unified when the parcel form consumes the Library.
- **Interest type** is still the fixed list the parcel Interests tab uses (Fee taking, Remainder, ...). The Library list is the agency's own; switching the tab to it is part of the parcel form slice.
- Which default values are "right" for an agency (for example "Austin Energy Easement") is the agency's to confirm.

## Verified by
Unit (`LibraryTests`), real-Postgres integration (`LibraryApiTests`: defaults, the administrator's job, six roles, races, tenancy, constraints, audit, hostile input), frontend (`library.test.tsx`, axe), Playwright (`library.spec.ts`: AT-AD-28, AT-X-37).
