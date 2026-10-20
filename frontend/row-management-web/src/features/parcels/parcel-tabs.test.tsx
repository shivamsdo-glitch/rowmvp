import { beforeEach, describe, expect, it } from 'vitest'
import { fireEvent, screen, waitFor, within } from '@testing-library/react'
import userEventModule from '@testing-library/user-event'
import { ROLES } from '@/api/auth'
import type { Project } from '@/api/projects'
import { levelFor, permissionRowsFor } from '@/test/contract'
import { fakeTabsApi, type FakeTabsOptions } from '@/test/fake-tabs'
import { renderApp } from '@/test/render-app'

beforeEach(() => sessionStorage.clear())

const can = (role: string, page: string, action: 'read' | 'create' | 'update' | 'delete') => {
  const level = levelFor(role, page)
  if (action === 'read') return level !== null
  if (action === 'delete') return level === 'P'
  if (action === 'update') return level === 'U' || level === 'S' || level === 'P'
  return level === 'S' || level === 'P'
}

const PROJECTS: Partial<Project>[] = [{ id: 'p1', number: 'ALR-PPhase 1-029', name: 'Harbor Bridge', oversightType: 'Local' }]

function open(role = 'Negotiator', extra: Partial<FakeTabsOptions> = {}) {
  const fake = fakeTabsApi({
    role, rows: permissionRowsFor(role), projects: PROJECTS,
    parcels: [{ id: 'pc1', projectId: 'p1', number: 'PN-1001', address: '1 Oak St' }, { id: 'pc-gone', projectId: 'p1', number: '009', state: 'Split' }],
    contactLog: [{ parcelId: 'pc1', id: 'cl1', number: 'CL-PN-1001-001', mode: 'Phone', contactedBy: 'Pat', contactPerson: 'Alex Marlowe', notes: 'First call.', links: [{ id: 'l1', recordIdentifier: 'APP-1', moduleName: 'Appraisal', notes: null }] }],
    easements: [{ parcelId: 'pc1', id: 'es1', number: 'E-1', easementType: 'Sidewalk Easement', startDate: '2026-01-01', endDate: '2036-01-01', startStation: '10+00', endStation: '12+50' }],
    ...extra,
  })
  const user = userEventModule.setup()
  const app = renderApp({ path: '/parcels/pc1', fetchImpl: fake.fetch, signedIn: true })
  return { fake, user, ...app }
}

const tab = async (user: ReturnType<typeof userEventModule.setup>, name: string) => user.click(await screen.findByRole('tab', { name }))

describe('Contact log (AT-NE-40)', () => {
  it('lists the contacts newest first with their ids, mode, people and how many records they refer to', async () => {
    const { user } = open()
    await tab(user, 'Contact log')

    const table = await screen.findByRole('table', { name: 'Contact log' })
    const row = within(table).getAllByRole('row')[1]
    expect(row).toHaveTextContent('CL-PN-1001-001')
    expect(row).toHaveTextContent('Phone')
    expect(row).toHaveTextContent('Alex Marlowe')
    expect(row).toHaveTextContent('First call.')
    expect(within(row).getAllByRole('cell')[6]).toHaveTextContent('1')
  })

  it('logs a contact with a mode from the Library and linked records, and sends the time as UTC', async () => {
    const { fake, user } = open()
    await tab(user, 'Contact log')
    const form = await screen.findByRole('form', { name: 'Log a contact' })

    await user.click(within(form).getByRole('button', { name: 'Add contact' }))
    expect(await within(form).findByText('Enter the date and time of the contact.')).toBeInTheDocument()

    fireEvent.change(within(form).getByLabelText(/^Date and time of contact/), { target: { value: '2026-10-02T09:15' } })
    await waitFor(() => expect(within(within(form).getByLabelText(/^Mode of contact/)).getAllByRole('option').map((o) => o.textContent)).toEqual(['Select one', 'Phone', 'In person', 'Letter']))
    await user.selectOptions(within(form).getByLabelText(/^Mode of contact/), 'In person')
    await user.type(within(form).getByLabelText(/^Contact person/), 'Dana Owner')
    await user.type(within(form).getByLabelText(/^Discussion notes/), 'Walked the boundary.')
    await user.click(within(form).getByRole('button', { name: 'Add linked record' }))
    expect(await within(form).findByText('Enter the record identifier.')).toBeInTheDocument()
    await user.type(within(form).getByLabelText(/^Record identifier/), 'OFFER-7')
    await user.type(within(form).getByLabelText(/^Module name/), 'Negotiation')
    await user.click(within(form).getByRole('button', { name: 'Add linked record' }))
    expect(within(form).getByRole('table', { name: 'Linked records' })).toHaveTextContent('OFFER-7')
    await user.click(within(form).getByRole('button', { name: 'Add contact' }))

    expect(await screen.findByRole('row', { name: /CL-PN-1001-002/ })).toBeInTheDocument()
    const call = fake.calls.find((c) => c.key === 'POST /api/v1/parcels/pc1/contact-log')!
    expect(call.body).toMatchObject({ mode: 'In person', contactPerson: 'Dana Owner', notes: 'Walked the boundary.', links: [{ recordIdentifier: 'OFFER-7', moduleName: 'Negotiation', notes: null }] })
    expect(call.body.contactedAt).toBe(new Date('2026-10-02T09:15').toISOString())
  })

  it('changes a contact (keeping its id and replacing its linked records) and removes one', async () => {
    const { fake, user } = open()
    await tab(user, 'Contact log')
    await user.click(await screen.findByRole('button', { name: 'Edit contact CL-PN-1001-001' }))
    const form = await screen.findByRole('form', { name: 'Change contact' })

    expect(within(form).getByLabelText(/^Contact person/)).toHaveValue('Alex Marlowe')
    await user.click(within(form).getByRole('button', { name: 'Remove linked record APP-1' }))
    await user.clear(within(form).getByLabelText(/^Discussion notes/))
    await user.type(within(form).getByLabelText(/^Discussion notes/), 'Changed.')
    await user.click(within(form).getByRole('button', { name: 'Save changes' }))

    await waitFor(() => expect(fake.contactLog[0].notes).toBe('Changed.'))
    expect(fake.contactLog[0].links).toEqual([])
    expect(fake.contactLog[0].number).toBe('CL-PN-1001-001')

    await user.click(await screen.findByRole('button', { name: 'Remove contact CL-PN-1001-001' }))
    await waitFor(() => expect(fake.contactLog).toHaveLength(0))
    expect(await screen.findByText('No contacts logged')).toBeInTheDocument()
  })

  it('attaches and removes a file on a contact being changed, through the shared documents API', async () => {
    const { fake, user } = open()
    await tab(user, 'Contact log')
    await user.click(await screen.findByRole('button', { name: 'Edit contact CL-PN-1001-001' }))
    const panel = await screen.findByRole('region', { name: 'Attachments for CL-PN-1001-001' })
    expect(await within(panel).findByText('No attachments')).toBeInTheDocument()

    await user.upload(within(panel).getByLabelText('Upload document'), new File(['hello'], 'sketch.pdf', { type: 'application/pdf' }))
    expect(await within(panel).findByText('sketch.pdf')).toBeInTheDocument()
    expect(fake.documents[0]).toMatchObject({ entityType: 'ContactLog', entityId: 'cl1', fileName: 'sketch.pdf' })

    await user.click(within(panel).getByRole('button', { name: 'Remove sketch.pdf' }))
    await waitFor(() => expect(fake.documents).toHaveLength(0))
  })

  it('shows a message instead of a list when the parcel is no longer active', async () => {
    const { user } = open('Negotiator', { parcels: [{ id: 'pc1', projectId: 'p1', number: 'PN-1001', state: 'Split' }] })
    await tab(user, 'Contact log')

    expect(await screen.findByRole('table', { name: 'Contact log' })).toBeInTheDocument()
    expect(screen.queryByRole('form', { name: 'Log a contact' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /^Edit contact/ })).not.toBeInTheDocument()
  })
})

describe('Easements (AT-SU-40)', () => {
  it('lists the easements with their type, dates and stations', async () => {
    const { user } = open('Surveyor')
    await tab(user, 'Easements')

    const row = (await screen.findByRole('row', { name: /E-1/ }))
    expect(row).toHaveTextContent('Sidewalk Easement')
    expect(row).toHaveTextContent('10+00')
    expect(row).toHaveTextContent('12+50')
  })

  it('adds an easement, refusing a missing number, backwards dates and a number already used', async () => {
    const { fake, user } = open('Surveyor')
    await tab(user, 'Easements')
    const form = await screen.findByRole('form', { name: 'Add an easement' })

    await user.click(within(form).getByRole('button', { name: 'Add easement' }))
    expect(await within(form).findByText('Easement number is required.')).toBeInTheDocument()

    await user.type(within(form).getByLabelText(/^Easement number/), 'E-2')
    fireEvent.change(within(form).getByLabelText(/^Start date/), { target: { value: '2026-05-01' } })
    fireEvent.change(within(form).getByLabelText(/^End date/), { target: { value: '2026-04-01' } })
    await user.click(within(form).getByRole('button', { name: 'Add easement' }))
    expect(await within(form).findByText('The end date cannot be before the start date.')).toBeInTheDocument()

    fireEvent.change(within(form).getByLabelText(/^End date/), { target: { value: '2027-04-01' } })
    await waitFor(() => expect(within(within(form).getByLabelText(/^Easement type/)).getAllByRole('option').map((o) => o.textContent)).toEqual(['Select one', 'Sidewalk Easement', 'Drainage Easement']))
    await user.selectOptions(within(form).getByLabelText(/^Easement type/), 'Drainage Easement')
    await user.type(within(form).getByLabelText(/^Start station/), '14+00')
    await user.click(within(form).getByRole('button', { name: 'Add easement' }))
    expect(await screen.findByRole('row', { name: /E-2/ })).toBeInTheDocument()
    expect(fake.easements.find((e) => e.number === 'E-2')).toMatchObject({ easementType: 'Drainage Easement', startStation: '14+00', startDate: '2026-05-01', endDate: '2027-04-01' })

    await user.type(within(await screen.findByRole('form', { name: 'Add an easement' })).getByLabelText(/^Easement number/), 'E-1')
    await user.click(screen.getByRole('button', { name: 'Add easement' }))
    expect(await screen.findByText("This parcel already has an easement numbered 'E-1'.")).toBeInTheDocument()
  })

  it('changes and removes an easement', async () => {
    const { fake, user } = open('Surveyor')
    await tab(user, 'Easements')
    await user.click(await screen.findByRole('button', { name: 'Edit easement E-1' }))
    const form = await screen.findByRole('form', { name: 'Change easement' })

    await user.clear(within(form).getByLabelText(/^End station/))
    await user.type(within(form).getByLabelText(/^End station/), '13+00')
    await user.click(within(form).getByRole('button', { name: 'Save changes' }))
    await waitFor(() => expect(fake.easements[0].endStation).toBe('13+00'))

    await user.click(await screen.findByRole('button', { name: 'Remove easement E-1' }))
    await waitFor(() => expect(fake.easements).toHaveLength(0))
  })
})

describe('Ownership details (AT-DE-42)', () => {
  const OWN = [
    { parcelId: 'pc1', id: 'ow1', ownershipNumber: 1, ownerName: 'Kamali', ownerType: 'Fee Owner',
      addresses: [{ id: 'a1', addressType: 'Tax Card Mailing Address', address: '1 Main St', contactPerson: 'Kamali, Devanshi', city: 'Austin', state: 'TX', zip: '78701' }],
      contacts: [{ id: 'c1', contactType: 'Broker', name: 'Dev Kamali', phone: '(112) 345-6789', email: 'k@example.com', address: null, isActive: true, isPrimary: true }] },
  ]

  it('lists each ownership with its number, type and how many addresses and contacts it has, and the parcel shows the owner name', async () => {
    const { user } = open('Designer', { ownerships: OWN })
    expect(await screen.findByRole('region', { name: 'Parcel synopsis' })).toHaveTextContent('Kamali')
    await tab(user, 'Ownership details')

    const row = await screen.findByRole('row', { name: /Fee Owner/ })
    expect(row).toHaveTextContent('Kamali')
    expect(within(row).getAllByRole('cell')[0]).toHaveTextContent('1')
    expect(within(row).getAllByRole('cell')[3]).toHaveTextContent('1')
    expect(within(row).getAllByRole('cell')[4]).toHaveTextContent('1')
  })

  it('adds an ownership with an address and contacts, refusing a missing owner name and a second primary contact', async () => {
    const { fake, user } = open('Designer', { ownerships: OWN })
    await tab(user, 'Ownership details')
    const form = await screen.findByRole('form', { name: 'Add an ownership' })

    await user.click(within(form).getByRole('button', { name: 'Add ownership' }))
    expect(await within(form).findByText('Owner name is required.')).toBeInTheDocument()

    await user.type(within(form).getByLabelText(/^Owner name/), 'Okafor Holdings')
    await waitFor(() => expect(within(within(form).getByLabelText(/^Owner type/)).getAllByRole('option').map((o) => o.textContent)).toEqual(['Select one', 'CAD Owner', 'Fee Owner', 'Vested Owner']))
    await user.selectOptions(within(form).getByLabelText(/^Owner type/), 'Vested Owner')

    await user.click(within(form).getByRole('button', { name: 'Add address' }))
    expect(await within(form).findByText('Enter at least part of the address.')).toBeInTheDocument()
    await waitFor(() => expect(within(within(form).getByLabelText(/^Address type/)).getAllByRole('option').map((o) => o.textContent)).toEqual(['Select one', 'Tax Card Mailing Address']))
    await user.selectOptions(within(form).getByLabelText(/^Address type/), 'Tax Card Mailing Address')
    await user.type(within(form).getByLabelText(/^Address$/), '9 Elm St')
    await user.type(within(form).getByLabelText(/^City/), 'Dallas')
    await user.click(within(form).getByRole('button', { name: 'Add address' }))
    expect(within(form).getByRole('table', { name: 'Addresses' })).toHaveTextContent('9 Elm St')

    await user.click(within(form).getByRole('button', { name: 'Add contact' }))
    expect(await within(form).findByText('Enter the contact name.')).toBeInTheDocument()
    await user.type(within(form).getByLabelText(/^Contact name/), 'Pat Primary')
    await user.click(within(form).getByLabelText('Is primary'))
    await user.click(within(form).getByRole('button', { name: 'Add contact' }))
    await user.type(within(form).getByLabelText(/^Contact name/), 'Second Primary')
    await user.click(within(form).getByLabelText('Is primary'))
    await user.click(within(form).getByRole('button', { name: 'Add contact' }))
    expect(await within(form).findByText('Only one contact can be the primary contact.')).toBeInTheDocument()

    await user.click(within(form).getByRole('button', { name: 'Add ownership' }))
    expect(await screen.findByRole('row', { name: /Okafor Holdings/ })).toBeInTheDocument()
    expect(fake.ownerships.find((o) => o.ownerName === 'Okafor Holdings')).toMatchObject({
      ownershipNumber: 2, ownerType: 'Vested Owner',
      addresses: [{ addressType: 'Tax Card Mailing Address', address: '9 Elm St', city: 'Dallas' }], contacts: [{ name: 'Pat Primary', isPrimary: true, isActive: true }],
    })
    expect(fake.parcels.find((p) => p.id === 'pc1')!.ownerName).toBe('Kamali; Okafor Holdings')
  })

  it('changes an ownership, replacing its addresses and contacts, and removes it so the owner name goes with it', async () => {
    const { fake, user } = open('Designer', { ownerships: OWN })
    await tab(user, 'Ownership details')
    await user.click(await screen.findByRole('button', { name: 'Edit ownership 1' }))
    const form = await screen.findByRole('form', { name: 'Change ownership' })

    expect(within(form).getByLabelText(/^Owner name/)).toHaveValue('Kamali')
    await user.click(within(form).getByRole('button', { name: 'Remove address 1' }))
    await user.click(within(form).getByRole('button', { name: 'Remove contact Dev Kamali' }))
    await user.clear(within(form).getByLabelText(/^Owner name/))
    await user.type(within(form).getByLabelText(/^Owner name/), 'Kamali Family Trust')
    await user.click(within(form).getByRole('button', { name: 'Save changes' }))
    await waitFor(() => expect(fake.ownerships[0].ownerName).toBe('Kamali Family Trust'))
    expect(fake.ownerships[0].addresses).toEqual([])
    expect(fake.ownerships[0].contacts).toEqual([])
    expect(fake.parcels.find((p) => p.id === 'pc1')!.ownerName).toBe('Kamali Family Trust')

    await user.click(await screen.findByRole('button', { name: 'Remove ownership 1' }))
    await waitFor(() => expect(fake.ownerships).toHaveLength(0))
    expect(await screen.findByText('No ownerships recorded')).toBeInTheDocument()
    expect(fake.parcels.find((p) => p.id === 'pc1')!.ownerName).toBeNull()
  })

  it('shows the owner name read-only on the parcel form', async () => {
    const { user } = open('Designer', { ownerships: OWN })
    await user.click(await screen.findByRole('button', { name: 'Edit parcel' }))
    const form = await screen.findByRole('form', { name: 'Edit parcel' })

    expect(within(form).getByLabelText(/^Owner name/)).toBeDisabled()
    expect(within(form).getByLabelText(/^Owner name/)).toHaveValue('Kamali')
  })
})

describe('Parcel location (AT-DE-43)', () => {
  it('shows the parcel id, its CAD number and that nothing has been saved to ESRI', async () => {
    const { user } = open('Designer', { parcels: [{ id: 'pc1', projectId: 'p1', number: 'PN-1001', cadNumber: 'CAD-77' }] })
    await tab(user, 'Parcel location')

    const table = await screen.findByRole('table', { name: 'Parcel location' })
    const row = within(table).getAllByRole('row')[1]
    expect(row).toHaveTextContent('PN-1001')
    expect(row).toHaveTextContent('CAD-77')
    expect(row).toHaveTextContent('Not saved')
    expect(screen.getByRole('button', { name: 'Export to CSV' })).toBeInTheDocument()
    expect(screen.queryByRole('form', { name: /parcel location/i })).not.toBeInTheDocument()
  })
})

describe('Title encumbrances (AT-TE-40)', () => {
  const ENC = [{ parcelId: 'pc1', id: 'en1', number: 'ENC-PN-1001-001', encumbranceType: 'Mortgage Lien', documentTitle: 'Deed of Trust 2019', grantor: 'Kamali Family', grantee: 'First Bank', cureStatus: 'In progress', dateRecorded: '2019-03-15' }]

  it('lists the encumbrances with their id, type, document, parties and cure status', async () => {
    const { user } = open('TitleResearcher', { encumbrances: ENC })
    await tab(user, 'Title encumbrances')

    const row = await screen.findByRole('row', { name: /ENC-PN-1001-001/ })
    expect(row).toHaveTextContent('Mortgage Lien')
    expect(row).toHaveTextContent('Deed of Trust 2019')
    expect(row).toHaveTextContent('Kamali Family')
    expect(row).toHaveTextContent('First Bank')
    expect(row).toHaveTextContent('In progress')
  })

  it('adds an encumbrance, refusing the missing required fields, and an unrecorded document drops the recording date', async () => {
    const { fake, user } = open('TitleResearcher', { encumbrances: ENC })
    await tab(user, 'Title encumbrances')
    const form = await screen.findByRole('form', { name: 'Add an encumbrance' })

    await user.click(within(form).getByRole('button', { name: 'Add encumbrance' }))
    for (const m of ['Encumbrance type is required.', 'Document title is required.', 'Grantor / lessor is required.', 'Grantee / lessee is required.'])
      expect(await within(form).findByText(m)).toBeInTheDocument()

    await waitFor(() => expect(within(within(form).getByLabelText(/^Encumbrance type/)).getAllByRole('option').map((o) => o.textContent)).toEqual(['Select one', 'Mortgage Lien', 'Easement - Access', 'Judgment']))
    await user.selectOptions(within(form).getByLabelText(/^Encumbrance type/), 'Easement - Access')
    await user.type(within(form).getByLabelText(/^Document title/), 'Access easement')
    await user.type(within(form).getByLabelText(/^Grantor/), 'Okafor')
    await user.type(within(form).getByLabelText(/^Grantee/), 'County')
    fireEvent.change(within(form).getByLabelText(/^Date recorded/), { target: { value: '2020-01-02' } })
    await user.click(within(form).getByLabelText('Unrecorded document'))
    expect(within(form).getByLabelText(/^Date recorded/)).toBeDisabled()
    expect(within(form).getByLabelText(/^Date recorded/)).toHaveValue('')
    fireEvent.change(within(form).getByLabelText(/^Exception to title submitted date/), { target: { value: '2026-03-01' } })
    fireEvent.change(within(form).getByLabelText(/^Exception to title approved date/), { target: { value: '2026-02-01' } })
    await user.click(within(form).getByRole('button', { name: 'Add encumbrance' }))
    expect(await within(form).findByText('The exception cannot be approved before it is submitted.')).toBeInTheDocument()

    fireEvent.change(within(form).getByLabelText(/^Exception to title approved date/), { target: { value: '2026-03-10' } })
    await user.type(within(form).getByLabelText(/^Record identifier/), 'TC-1')
    await user.click(within(form).getByRole('button', { name: 'Add linked record' }))
    await user.click(within(form).getByRole('button', { name: 'Add encumbrance' }))

    expect(await screen.findByRole('row', { name: /Access easement/ })).toHaveTextContent('Unrecorded')
    expect(fake.encumbrances.find((e) => e.documentTitle === 'Access easement')).toMatchObject({
      number: 'ENC-PN-1001-002', isUnrecorded: true, dateRecorded: null, exceptionApprovedDate: '2026-03-10', links: [{ recordIdentifier: 'TC-1' }],
    })
  })

  it('changes an encumbrance, attaches a file, and removes it', async () => {
    const { fake, user } = open('TitleResearcher', { encumbrances: ENC })
    await tab(user, 'Title encumbrances')
    await user.click(await screen.findByRole('button', { name: 'Edit encumbrance ENC-PN-1001-001' }))
    const form = await screen.findByRole('form', { name: 'Change encumbrance' })

    expect(within(form).getByLabelText(/^Grantee/)).toHaveValue('First Bank')
    await user.clear(within(form).getByLabelText(/^Review notes/))
    await user.type(within(form).getByLabelText(/^Review notes/), 'Release requested.')
    await user.click(within(form).getByRole('button', { name: 'Save changes' }))
    await waitFor(() => expect(fake.encumbrances[0].reviewNotes).toBe('Release requested.'))

    await user.click(await screen.findByRole('button', { name: 'Edit encumbrance ENC-PN-1001-001' }))
    const panel = await screen.findByRole('region', { name: 'Attachments for ENC-PN-1001-001' })
    await user.upload(within(panel).getByLabelText('Upload document'), new File(['x'], 'release.pdf', { type: 'application/pdf' }))
    expect(await within(panel).findByText('release.pdf')).toBeInTheDocument()
    expect(fake.documents[0]).toMatchObject({ entityType: 'Encumbrance', entityId: 'en1' })

    await user.click(await screen.findByRole('button', { name: 'Remove encumbrance ENC-PN-1001-001' }))
    await waitFor(() => expect(fake.encumbrances).toHaveLength(0))
    expect(await screen.findByText('No encumbrances recorded')).toBeInTheDocument()
  })
})

describe('Title and closing (AT-TE-41)', () => {
  const TC = [{
    parcelId: 'pc1', id: 'tc1', number: 'TC-PN-1001', vestedOwnerName: 'Vested One', titleCompany: 'Lone Star Title', gfNumber: 'GF-100', psaAmount: 125000.5,
    psaSignedByOwnerDate: '2026-03-01', curativeItemsCompleted: true,
    conveyanceDocuments: [{ id: 'cv1', conveyanceType: 'Warranty Deed', signedDate: '2026-04-20', recordedDate: '2026-04-25', recordingNumber: '2026-0001' }],
    titleCommitments: [{ id: 'cm1', vestedOwnerName: 'Kamali', requestedDate: '2026-02-01', receivedDate: null, effectiveDate: null, issuedDate: null, curativePlanSubmittedDate: null, curativePlanApprovedDate: null }],
  }]

  it('shows an empty form with no id yet and creates the record on the first save', async () => {
    const { fake, user } = open('TitleResearcher')
    await tab(user, 'Title and closing')
    const form = await screen.findByRole('form', { name: 'Title and closing' })

    expect(form).toHaveTextContent('Assigned when saved')
    await waitFor(() => expect(within(within(form).getByLabelText(/^Title company/)).getAllByRole('option').map((o) => o.textContent)).toEqual(['Select one', 'Lone Star Title', 'Other Title']))
    await waitFor(() => expect(within(within(form).getByLabelText(/^Assigned to/)).getAllByRole('option').map((o) => o.textContent)).toEqual(['Select one', 'Casey Closer', 'Pat Agent']))
    await user.selectOptions(within(form).getByLabelText(/^Title company/), 'Lone Star Title')
    await user.selectOptions(within(form).getByLabelText(/^Assigned to/), 'Casey Closer')
    await user.type(within(form).getByLabelText(/^GF number/), 'GF-9')
    await user.type(within(form).getByLabelText(/^Purchase and sale agreement amount/), '125000.5')
    fireEvent.change(within(form).getByLabelText(/^Anticipated closing date/), { target: { value: '2026-05-01' } })
    await user.click(within(form).getByLabelText('Curative items completed'))
    await user.click(within(form).getByRole('button', { name: 'Save title and closing' }))

    expect(await screen.findByText('Title and closing saved.')).toBeInTheDocument()
    expect(fake.titleClosings).toHaveLength(1)
    expect(fake.titleClosings[0]).toMatchObject({
      number: 'TC-PN-1001', titleCompany: 'Lone Star Title', assignedTo: 'Casey Closer', gfNumber: 'GF-9', psaAmount: 125000.5, anticipatedClosingDate: '2026-05-01', curativeItemsCompleted: true,
    })
    expect(await screen.findByText('TC-PN-1001')).toBeInTheDocument()
    expect(await screen.findByRole('button', { name: 'Save changes' })).toBeInTheDocument()
  })

  it('loads the saved record, shows the vested owner name read-only, and changes the grids and a date', async () => {
    const { fake, user } = open('TitleResearcher', { titleClosings: TC })
    await tab(user, 'Title and closing')
    const form = await screen.findByRole('form', { name: 'Title and closing' })

    expect(form).toHaveTextContent('Vested One')
    expect(within(form).getByLabelText(/^GF number/)).toHaveValue('GF-100')
    expect(within(form).getByLabelText('Curative items completed')).toBeChecked()
    expect(within(form).getByRole('table', { name: 'Conveyance documents' })).toHaveTextContent('2026-0001')
    expect(within(form).getByRole('table', { name: 'Title commitments' })).toHaveTextContent('Kamali')

    await user.click(within(form).getByRole('button', { name: 'Add conveyance document' }))
    expect(await within(form).findByText('Enter at least one detail of the conveyance document.')).toBeInTheDocument()
    fireEvent.change(within(form).getByLabelText(/^Conveyance document signed date/), { target: { value: '2026-05-10' } })
    fireEvent.change(within(form).getByLabelText(/^Conveyance document recorded date/), { target: { value: '2026-05-01' } })
    await user.click(within(form).getByRole('button', { name: 'Add conveyance document' }))
    expect(await within(form).findByText('A conveyance cannot be recorded before it is signed.')).toBeInTheDocument()
    fireEvent.change(within(form).getByLabelText(/^Conveyance document recorded date/), { target: { value: '2026-05-11' } })
    await user.click(within(form).getByRole('button', { name: 'Add conveyance document' }))

    await user.click(within(form).getByRole('button', { name: 'Add title commitment' }))
    expect(await within(form).findByText('Enter the vested owner name.')).toBeInTheDocument()
    await user.click(within(form).getByRole('button', { name: 'Remove title commitment Kamali' }))
    await user.clear(within(form).getByLabelText(/^GF number/))
    await user.type(within(form).getByLabelText(/^GF number/), 'GF-200')
    await user.click(within(form).getByRole('button', { name: 'Save changes' }))

    await waitFor(() => expect(fake.titleClosings[0].gfNumber).toBe('GF-200'))
    expect(fake.titleClosings[0].conveyanceDocuments).toHaveLength(2)
    expect(fake.titleClosings[0].titleCommitments).toEqual([])
  })

  it('refuses a settlement statement executed before it was received and a negative amount', async () => {
    const { fake, user } = open('TitleResearcher')
    await tab(user, 'Title and closing')
    const form = await screen.findByRole('form', { name: 'Title and closing' })

    fireEvent.change(within(form).getByLabelText(/^Settlement statement received date/), { target: { value: '2026-04-05' } })
    fireEvent.change(within(form).getByLabelText(/^Settlement statement executed date/), { target: { value: '2026-04-01' } })
    await user.type(within(form).getByLabelText(/^Purchase and sale agreement amount/), '-5')
    await user.click(within(form).getByRole('button', { name: 'Save title and closing' }))

    expect(await within(form).findByText('The statement cannot be executed before it is received.')).toBeInTheDocument()
    expect(within(form).getByText('The amount must be zero or more.')).toBeInTheDocument()
    expect(fake.titleClosings).toHaveLength(0)
  })

  it('attaches a file once the record exists, and removes the record', async () => {
    const { fake, user } = open('Admin', { titleClosings: TC })
    await tab(user, 'Title and closing')
    const panel = await screen.findByRole('region', { name: 'Attachments for TC-PN-1001' })

    await user.upload(within(panel).getByLabelText('Upload document'), new File(['x'], 'policy.pdf', { type: 'application/pdf' }))
    expect(await within(panel).findByText('policy.pdf')).toBeInTheDocument()
    expect(fake.documents[0]).toMatchObject({ entityType: 'TitleClosing', entityId: 'tc1' })

    await user.click(screen.getByRole('button', { name: 'Remove title and closing record' }))
    await waitFor(() => expect(fake.titleClosings).toHaveLength(0))
    expect(await screen.findByText('Assigned when saved')).toBeInTheDocument()
  })
})

const AP = [{ parcelId: 'pc1', id: 'ap1', number: 'APR-PN-1001-001', appraisalType: 'Full Appraisal', appraiser: 'Pat Agent', appraiserCompany: 'Acme', appraisalTotal: 1500,
  amounts: [{ id: 'am0', amountType: 'Land', description: null, amount: 1500 }], approvedJustCompensation: 1400, reviewAppraiser: 'Casey Closer' }]

describe('Appraisal (AT-AP-40, AT-AA-40)', () => {
  it('lists appraisals with their company, total and approved just compensation', async () => {
    const { user } = open('Appraiser', { appraisals: AP })
    await tab(user, 'Appraisal')

    const row = within(await screen.findByRole('table', { name: 'Appraisals' })).getAllByRole('row')[1]
    expect(row).toHaveTextContent('APR-PN-1001-001')
    expect(row).toHaveTextContent('Acme')
    expect(row).toHaveTextContent('$1,500.00')
    expect(row).toHaveTextContent('$1,400.00')
  })

  it('adds an appraisal with amounts whose total is shown, and refuses a date that goes backwards', async () => {
    const { fake, user } = open('Appraiser')
    await tab(user, 'Appraisal')
    const form = await screen.findByRole('form', { name: 'Add an appraisal' })

    await waitFor(() => expect(within(within(form).getByLabelText(/^Appraisal type/)).getAllByRole('option').map((o) => o.textContent)).toEqual(['Select one', 'Full Appraisal', 'Review']))
    await user.selectOptions(within(form).getByLabelText(/^Appraisal type/), 'Full Appraisal')
    await user.click(within(form).getByRole('button', { name: 'Add amount' }))
    expect(await within(form).findByText('Choose the amount type.')).toBeInTheDocument()
    await user.selectOptions(within(form).getByLabelText(/^Amount type/), 'Land')
    await user.type(within(form).getByLabelText(/^Amount$/), '100.5')
    await user.click(within(form).getByRole('button', { name: 'Add amount' }))
    await user.selectOptions(within(form).getByLabelText(/^Amount type/), 'Damages')
    await user.type(within(form).getByLabelText(/^Amount$/), '20')
    await user.click(within(form).getByRole('button', { name: 'Add amount' }))
    expect(within(form).getByText('$120.50')).toBeInTheDocument()

    fireEvent.change(within(form).getByLabelText(/^Appraisal requested date/), { target: { value: '2026-03-01' } })
    fireEvent.change(within(form).getByLabelText(/^Appraisal received date$/), { target: { value: '2026-02-01' } })
    await user.click(within(form).getByRole('button', { name: 'Add appraisal' }))
    expect(await within(form).findByText('The appraisal cannot be received before it is requested.')).toBeInTheDocument()
    expect(fake.appraisals).toHaveLength(0)

    fireEvent.change(within(form).getByLabelText(/^Appraisal received date$/), { target: { value: '2026-03-10' } })
    await user.click(within(form).getByRole('button', { name: 'Add appraisal' }))
    await waitFor(() => expect(fake.appraisals).toHaveLength(1))
    expect(fake.appraisals[0]).toMatchObject({ number: 'APR-PN-1001-001', appraisalType: 'Full Appraisal', appraisalTotal: 120.5, receivedDate: '2026-03-10' })
    expect(fake.appraisals[0].amounts.map((a) => a.amountType)).toEqual(['Land', 'Damages'])
  })

  it('shows the review section read-only to the Appraiser, who can still save the appraisal', async () => {
    const { fake, user } = open('Appraiser', { appraisals: AP })
    await tab(user, 'Appraisal')
    await user.click(await screen.findByRole('button', { name: 'Edit appraisal APR-PN-1001-001' }))
    const form = await screen.findByRole('form', { name: 'Change appraisal' })

    expect(within(form).getByLabelText(/^Approved just compensation/)).toBeDisabled()
    expect(within(form).getByLabelText(/^Approved just compensation/)).toHaveValue(1400)
    expect(within(form).getByLabelText(/^Review type/)).toBeDisabled()
    await user.click(within(form).getByRole('checkbox', { name: 'FTA concurrence needed' }))
    await user.click(within(form).getByRole('button', { name: 'Save changes' }))

    await waitFor(() => expect(fake.appraisals[0].ftaConcurrenceNeeded).toBe(true))
    expect(fake.appraisals[0].approvedJustCompensation).toBe(1400)
  })

  it('lets the Appraisal Approver set the approved just compensation', async () => {
    const { fake, user } = open('AppraisalApprover', { appraisals: AP })
    await tab(user, 'Appraisal')
    await user.click(await screen.findByRole('button', { name: 'Edit appraisal APR-PN-1001-001' }))
    const form = await screen.findByRole('form', { name: 'Change appraisal' })

    expect(within(form).getByLabelText(/^Approved just compensation/)).toBeEnabled()
    await user.clear(within(form).getByLabelText(/^Approved just compensation/))
    await user.type(within(form).getByLabelText(/^Approved just compensation/), '1600')
    await user.click(within(form).getByRole('button', { name: 'Save changes' }))

    await waitFor(() => expect(fake.appraisals[0].approvedJustCompensation).toBe(1600))
  })

  it('attaches a file to an appraisal and removes the appraisal', async () => {
    const { fake, user } = open('Admin', { appraisals: AP })
    await tab(user, 'Appraisal')
    await user.click(await screen.findByRole('button', { name: 'Edit appraisal APR-PN-1001-001' }))
    const panel = await screen.findByRole('region', { name: 'Attachments for APR-PN-1001-001' })

    await user.upload(within(panel).getByLabelText('Upload document'), new File(['x'], 'report.pdf', { type: 'application/pdf' }))
    expect(await within(panel).findByText('report.pdf')).toBeInTheDocument()
    expect(fake.documents[0]).toMatchObject({ entityType: 'ParcelAppraisal', entityId: 'ap1' })

    await user.click(screen.getByRole('button', { name: 'Remove appraisal APR-PN-1001-001' }))
    await waitFor(() => expect(fake.appraisals).toHaveLength(0))
  })
})

const NG = [{ parcelId: 'pc1', id: 'ng1', number: 'NEG-PN-1001', totalAgreementAmount: 5000 }]

describe('Negotiation (AT-NE-42)', () => {
  it('shows the parcel facts read-only and saves a first negotiation with its grids', async () => {
    const { fake, user } = open('Negotiator')
    await tab(user, 'Negotiation')
    const form = await screen.findByRole('form', { name: 'Negotiation' })

    expect(form).toHaveTextContent('PN-1001')
    expect(form).toHaveTextContent('1 Oak St')
    expect(form).toHaveTextContent('Assigned when saved')

    await user.click(within(form).getByRole('button', { name: 'Add status note' }))
    expect(await within(form).findByText('Enter the status note.')).toBeInTheDocument()
    await user.type(within(form).getByLabelText('Status note'), 'Owner called back.')
    await user.click(within(form).getByRole('button', { name: 'Add status note' }))
    expect(within(form).getByRole('table', { name: 'Negotiation status notes' })).toHaveTextContent('Owner called back.')

    await user.click(within(form).getByRole('button', { name: 'Add agent' }))
    expect(await within(form).findByText('Choose the agent.')).toBeInTheDocument()
    await user.selectOptions(within(form).getByLabelText('Agent'), 'Pat Agent')
    await user.click(within(form).getByRole('button', { name: 'Add agent' }))

    await waitFor(() => expect(within(within(form).getByLabelText('Offer type')).getAllByRole('option').map((o) => o.textContent)).toEqual(['Select one', 'Initial Offer', 'Final Offer']))
    await user.selectOptions(within(form).getByLabelText('Offer type'), 'Initial Offer')
    await user.type(within(form).getByLabelText('Offer amount'), '-5')
    await user.click(within(form).getByRole('button', { name: 'Add offer' }))
    expect(await within(form).findByText('The amount must be zero or more.')).toBeInTheDocument()
    await user.clear(within(form).getByLabelText('Offer amount'))
    await user.type(within(form).getByLabelText('Offer amount'), '200000')
    await user.click(within(form).getByRole('button', { name: 'Add offer' }))
    expect(within(form).getByRole('table', { name: 'Offers' })).toHaveTextContent('$200,000.00')

    await user.selectOptions(within(form).getByLabelText('Settlement type'), 'Administrative')
    await user.type(within(form).getByLabelText('Counteroffer amount'), '260000')
    await user.click(within(form).getByRole('button', { name: 'Add settlement' }))
    await user.type(within(form).getByLabelText('Total agreement amount'), '250000')
    await user.click(within(form).getByRole('button', { name: 'Save negotiation' }))

    await waitFor(() => expect(fake.negotiations).toHaveLength(1))
    expect(fake.negotiations[0]).toMatchObject({ number: 'NEG-PN-1001', totalAgreementAmount: 250000 })
    expect(fake.negotiations[0].statusNotes.map((n) => n.text)).toEqual(['Owner called back.'])
    expect(fake.negotiations[0].offers).toHaveLength(1)
    expect(fake.negotiations[0].settlements[0]).toMatchObject({ settlementType: 'Administrative', counterofferAmount: 260000 })
    expect(await within(form).findByText('Acme Realty')).toBeInTheDocument()
    expect(await screen.findByText('Negotiation saved.')).toBeInTheDocument()
  })

  it('refuses dates that go backwards before saving anything', async () => {
    const { fake, user } = open('Negotiator')
    await tab(user, 'Negotiation')
    const form = await screen.findByRole('form', { name: 'Negotiation' })

    fireEvent.change(within(form).getByLabelText('Counteroffer submitted to JAT'), { target: { value: '2026-06-10' } })
    fireEvent.change(within(form).getByLabelText('Counteroffer response from JAT'), { target: { value: '2026-06-01' } })
    fireEvent.change(within(form).getByLabelText('PUA executed'), { target: { value: '2026-07-10' } })
    fireEvent.change(within(form).getByLabelText('PUA closed'), { target: { value: '2026-07-01' } })
    await user.click(within(form).getByRole('button', { name: 'Save negotiation' }))

    expect(await within(form).findByText('The JAT cannot respond before the counteroffer is submitted.')).toBeInTheDocument()
    expect(within(form).getByText('The PUA cannot be closed before it is executed.')).toBeInTheDocument()
    expect(fake.negotiations).toHaveLength(0)

    await user.click(within(form).getByRole('button', { name: 'Add right of entry' }))
    expect(await within(form).findByText('Enter at least one detail of the right of entry.')).toBeInTheDocument()
  })

  it('changes a saved record and removes a row from a grid', async () => {
    const { fake, user } = open('Negotiator', {
      negotiations: [{ parcelId: 'pc1', id: 'ng1', number: 'NEG-PN-1001', offers: [{ id: 'o1', offerType: 'Initial Offer', mailedDate: null, deliveredDate: null, amount: 100, notes: null }] }],
    })
    await tab(user, 'Negotiation')
    const form = await screen.findByRole('form', { name: 'Negotiation' })

    expect(within(form).getByRole('table', { name: 'Offers' })).toHaveTextContent('$100.00')
    await user.click(within(form).getByRole('button', { name: 'Remove offer 1' }))
    await user.type(within(form).getByLabelText('Notes'), 'Spoke to owner.')
    await user.click(within(form).getByRole('button', { name: 'Save changes' }))

    await waitFor(() => expect(fake.negotiations[0].notes).toBe('Spoke to owner.'))
    expect(fake.negotiations[0].offers).toEqual([])
    expect(within(form).queryByRole('button', { name: 'Save negotiation' })).toBeNull()
  })

  it('attaches a file once the record exists, and removes the record', async () => {
    const { fake, user } = open('Admin', { negotiations: NG })
    await tab(user, 'Negotiation')
    const panel = await screen.findByRole('region', { name: 'Attachments for NEG-PN-1001' })

    await user.upload(within(panel).getByLabelText('Upload document'), new File(['x'], 'offer.pdf', { type: 'application/pdf' }))
    expect(await within(panel).findByText('offer.pdf')).toBeInTheDocument()
    expect(fake.documents[0]).toMatchObject({ entityType: 'ParcelNegotiation', entityId: 'ng1' })

    await user.click(screen.getByRole('button', { name: 'Remove negotiation record' }))
    await waitFor(() => expect(fake.negotiations).toHaveLength(0))
    expect(await screen.findByText('Assigned when saved')).toBeInTheDocument()
  })
})

const QS = [{ parcelId: 'pc1', id: 'qs1', number: 'QST-PN-1001-001', relocationType: 'Residential', dueByDate: '2026-08-01',
  questions: [{ id: 'q1', question: 'Do you rent?', answer: 'No', notes: null }] }]

describe('Questionnaire (AT-RA-40)', () => {
  it('lists questionnaires with their type, dates and number of questions', async () => {
    const { user } = open('RelocationAgent', { questionnaires: QS })
    await tab(user, 'Questionnaire')

    const row = within(await screen.findByRole('table', { name: 'Questionnaires' })).getAllByRole('row')[1]
    expect(row).toHaveTextContent('QST-PN-1001-001')
    expect(row).toHaveTextContent('Residential')
    expect(row).toHaveTextContent('1')
  })

  it('adds a questionnaire with interests and questions, and requires a relocation type', async () => {
    const { fake, user } = open('RelocationAgent')
    await tab(user, 'Questionnaire')
    const form = await screen.findByRole('form', { name: 'Add a questionnaire' })

    await user.click(within(form).getByRole('button', { name: 'Add questionnaire' }))
    expect(await within(form).findByText('Relocation type is required.')).toBeInTheDocument()
    expect(fake.questionnaires).toHaveLength(0)

    await waitFor(() => expect(within(within(form).getByLabelText(/^Relocation type/)).getAllByRole('option').map((o) => o.textContent)).toEqual(['Select one', 'Residential', 'Non-residential', 'Personal Property only']))
    await user.selectOptions(within(form).getByLabelText(/^Relocation type/), 'Non-residential')
    await user.click(within(form).getByRole('button', { name: 'Add ownership interest' }))
    expect(await within(form).findByText('Enter the name of the owner or interest holder.')).toBeInTheDocument()
    await user.type(within(form).getByLabelText('Interest holder name'), 'Kamali')
    await user.click(within(form).getByRole('button', { name: 'Add ownership interest' }))
    await user.click(within(form).getByRole('button', { name: 'Add question' }))
    expect(await within(form).findByText('Enter the question.')).toBeInTheDocument()
    await user.type(within(form).getByLabelText('Question', { selector: 'input' }), 'Do you rent?')
    await user.type(within(form).getByLabelText('Answer'), 'No')
    await user.click(within(form).getByRole('button', { name: 'Add question' }))
    await user.click(within(form).getByRole('button', { name: 'Add questionnaire' }))

    await waitFor(() => expect(fake.questionnaires).toHaveLength(1))
    expect(fake.questionnaires[0]).toMatchObject({ number: 'QST-PN-1001-001', relocationType: 'Non-residential' })
    expect(fake.questionnaires[0].interests.map((i) => i.name)).toEqual(['Kamali'])
    expect(fake.questionnaires[0].questions).toMatchObject([{ question: 'Do you rent?', answer: 'No' }])
  })

  it('changes a questionnaire, attaches a file and removes it', async () => {
    const { fake, user } = open('Admin', { questionnaires: QS })
    await tab(user, 'Questionnaire')
    await user.click(await screen.findByRole('button', { name: 'Edit questionnaire QST-PN-1001-001' }))
    const form = await screen.findByRole('form', { name: 'Change questionnaire' })

    await user.click(within(form).getByRole('button', { name: 'Remove question 1' }))
    await user.click(within(form).getByRole('button', { name: 'Save changes' }))
    await waitFor(() => expect(fake.questionnaires[0].questions).toEqual([]))

    await user.click(await screen.findByRole('button', { name: 'Edit questionnaire QST-PN-1001-001' }))
    const panel = await screen.findByRole('region', { name: 'Attachments for QST-PN-1001-001' })
    await user.upload(within(panel).getByLabelText('Upload document'), new File(['x'], 'answers.pdf', { type: 'application/pdf' }))
    expect(await within(panel).findByText('answers.pdf')).toBeInTheDocument()
    expect(fake.documents[0]).toMatchObject({ entityType: 'ParcelQuestionnaire', entityId: 'qs1' })

    await user.click(screen.getByRole('button', { name: 'Remove questionnaire QST-PN-1001-001' }))
    await waitFor(() => expect(fake.questionnaires).toHaveLength(0))
  })
})

describe('Condemnation (AT-NE-43)', () => {
  it('saves a first condemnation with its dates, people and grids, refusing an approval before the submission', async () => {
    const { fake, user } = open('Negotiator')
    await tab(user, 'Condemnation')
    const form = await screen.findByRole('form', { name: 'Condemnation' })
    expect(form).toHaveTextContent('Assigned when saved')

    fireEvent.change(within(form).getByLabelText('Preliminary condemnation package submitted to ATP'), { target: { value: '2026-03-10' } })
    fireEvent.change(within(form).getByLabelText('Preliminary condemnation package approved by ATP'), { target: { value: '2026-03-01' } })
    await user.click(within(form).getByRole('button', { name: 'Save condemnation' }))
    expect(await within(form).findByText('The package cannot be approved before it is submitted.')).toBeInTheDocument()
    expect(fake.condemnations).toHaveLength(0)

    fireEvent.change(within(form).getByLabelText('Preliminary condemnation package approved by ATP'), { target: { value: '2026-03-12' } })
    await user.selectOptions(within(form).getByLabelText('ED coordinator'), 'Casey Closer')
    await user.click(within(form).getByRole('checkbox', { name: 'Objections filed?' }))

    await user.type(within(form).getByLabelText('Status note'), 'Petition drafted.')
    await user.click(within(form).getByRole('button', { name: 'Add status note' }))
    await user.click(within(form).getByRole('button', { name: 'Add attorney' }))
    expect(await within(form).findByText('Enter the party represented.')).toBeInTheDocument()
    await user.type(within(form).getByLabelText('Party represented'), 'Owner')
    await user.type(within(form).getByLabelText('Attorney firm'), 'Law LLP')
    await user.click(within(form).getByRole('button', { name: 'Add attorney' }))
    await user.type(within(form).getByLabelText('Name of commissioner'), 'C. Commissioner')
    await user.click(within(form).getByRole('button', { name: 'Add commissioner' }))
    await user.type(within(form).getByLabelText('Party to serve'), 'Owner')
    await user.click(within(form).getByRole('button', { name: 'Add notice of hearing' }))
    await user.click(within(form).getByRole('button', { name: 'Save condemnation' }))

    await waitFor(() => expect(fake.condemnations).toHaveLength(1))
    expect(fake.condemnations[0]).toMatchObject({ number: 'CON-PN-1001' })
    expect(fake.condemnations[0].details).toMatchObject({ edCoordinator: 'Casey Closer', objectionsFiled: true, prelimPackageApprovedByAtp: '2026-03-12' })
    expect(fake.condemnations[0].statusNotes.map((n) => n.text)).toEqual(['Petition drafted.'])
    expect(fake.condemnations[0].attorneys).toMatchObject([{ partyRepresented: 'Owner', firm: 'Law LLP' }])
    expect(fake.condemnations[0].commissioners[0].name).toBe('C. Commissioner')
    expect(fake.condemnations[0].notices[0].partyToServe).toBe('Owner')
    expect(await screen.findByText('Condemnation saved.')).toBeInTheDocument()
  })

  it('gives the negotiator no Remove button and lets the Admin attach a file and remove the record', async () => {
    const neg = open('Negotiator', { condemnations: [{ parcelId: 'pc1', id: 'cn1', number: 'CON-PN-1001' }] })
    await tab(neg.user, 'Condemnation')
    await screen.findByRole('form', { name: 'Condemnation' })
    expect(screen.queryByRole('button', { name: 'Remove condemnation record' })).toBeNull()
    expect(screen.getByRole('button', { name: 'Save changes' })).toBeInTheDocument()
  })

  it('attaches a file and removes the record as Admin', async () => {
    const { fake, user } = open('Admin', { condemnations: [{ parcelId: 'pc1', id: 'cn1', number: 'CON-PN-1001' }] })
    await tab(user, 'Condemnation')
    const panel = await screen.findByRole('region', { name: 'Attachments for CON-PN-1001' })
    await user.upload(within(panel).getByLabelText('Upload document'), new File(['x'], 'petition.pdf', { type: 'application/pdf' }))
    expect(await within(panel).findByText('petition.pdf')).toBeInTheDocument()
    expect(fake.documents[0]).toMatchObject({ entityType: 'ParcelCondemnation', entityId: 'cn1' })
    await user.click(screen.getByRole('button', { name: 'Remove condemnation record' }))
    await waitFor(() => expect(fake.condemnations).toHaveLength(0))
  })
})

describe('Property management and disposition (AT-PM-43)', () => {
  it('saves a first record with disposition details and funding, refusing a negative price and percentages over 100', async () => {
    const { fake, user } = open('ProjectManager')
    await tab(user, 'Property management & disposition')
    const form = await screen.findByRole('form', { name: 'Property management and disposition' })

    await user.type(within(form).getByLabelText('Sale price / consideration value ($)'), '-5')
    await user.click(within(form).getByRole('button', { name: 'Save property management' }))
    expect(await within(form).findByText('The amount must be zero or more.')).toBeInTheDocument()
    expect(fake.properties).toHaveLength(0)
    await user.clear(within(form).getByLabelText('Sale price / consideration value ($)'))
    await user.type(within(form).getByLabelText('Sale price / consideration value ($)'), '125000')

    await waitFor(() => expect(within(within(form).getByLabelText('Disposition type')).getAllByRole('option').map((o) => o.textContent)).toEqual(['Select one', 'Sale', 'Transfer']))
    await user.selectOptions(within(form).getByLabelText('Disposition type'), 'Sale')
    await user.click(within(form).getByRole('checkbox', { name: 'FTA concurrence required?' }))

    await user.click(within(form).getByRole('button', { name: 'Add funding' }))
    expect(await within(form).findByText('Choose the fund type.')).toBeInTheDocument()
    await user.selectOptions(within(form).getByLabelText(/^Fund type/), 'Federal')
    await user.type(within(form).getByLabelText('Funding percentage'), '60')
    await user.click(within(form).getByRole('button', { name: 'Add funding' }))
    await user.selectOptions(within(form).getByLabelText(/^Fund type/), 'Local')
    await user.type(within(form).getByLabelText('Funding percentage'), '50')
    await user.click(within(form).getByRole('button', { name: 'Add funding' }))
    expect(await within(form).findByText('The funding percentages cannot add up to more than 100.')).toBeInTheDocument()
    await user.clear(within(form).getByLabelText('Funding percentage'))
    await user.type(within(form).getByLabelText('Funding percentage'), '40')
    await user.click(within(form).getByRole('button', { name: 'Add funding' }))

    await user.click(within(form).getByRole('button', { name: 'Save property management' }))
    await waitFor(() => expect(fake.properties).toHaveLength(1))
    expect(fake.properties[0]).toMatchObject({ number: 'PMD-PN-1001' })
    expect(fake.properties[0].details).toMatchObject({ salePrice: 125000, dispositionType: 'Sale', ftaConcurrenceRequired: true })
    expect(fake.properties[0].funding.map((x) => x.percentage)).toEqual([60, 40])
  })

  it('attaches a file and removes the record', async () => {
    const { fake, user } = open('Admin', { properties: [{ parcelId: 'pc1', id: 'pm1', number: 'PMD-PN-1001' }] })
    await tab(user, 'Property management & disposition')
    const panel = await screen.findByRole('region', { name: 'Attachments for PMD-PN-1001' })
    await user.upload(within(panel).getByLabelText('Upload document'), new File(['x'], 'memo.pdf', { type: 'application/pdf' }))
    expect(await within(panel).findByText('memo.pdf')).toBeInTheDocument()
    expect(fake.documents[0]).toMatchObject({ entityType: 'ParcelProperty', entityId: 'pm1' })
    await user.click(screen.getByRole('button', { name: 'Remove property management record' }))
    await waitFor(() => expect(fake.properties).toHaveLength(0))
  })
})

describe('Relocation (AT-RA-41)', () => {
  it('adds a relocation with its required fields, contacts, eligibility and contact log', async () => {
    const { fake, user } = open('RelocationAgent')
    await tab(user, 'Relocation')
    const form = await screen.findByRole('form', { name: 'Add a relocation' })

    await user.click(within(form).getByRole('button', { name: 'Add relocation' }))
    expect(await within(form).findByText('Relocation number is required.')).toBeInTheDocument()
    expect(within(form).getByText('Displacee name is required.')).toBeInTheDocument()
    expect(within(form).getByText('Relocation type is required.')).toBeInTheDocument()
    expect(fake.relocations).toHaveLength(0)

    await user.type(within(form).getByLabelText(/^Relocation number/), 'R-7')
    await user.type(within(form).getByLabelText(/^Displacee name/), 'Kamali')
    await user.selectOptions(within(form).getByLabelText(/^Relocation type/), 'Residential')
    fireEvent.change(within(form).getByLabelText('GIN mailed date'), { target: { value: '2026-03-05' } })
    fireEvent.change(within(form).getByLabelText('GIN delivered date'), { target: { value: '2026-03-01' } })
    await user.click(within(form).getByRole('button', { name: 'Add relocation' }))
    expect(await within(form).findByText('The GIN cannot be delivered before it is mailed.')).toBeInTheDocument()
    fireEvent.change(within(form).getByLabelText('GIN delivered date'), { target: { value: '2026-03-07' } })

    await user.type(within(form).getByLabelText('Contact name'), 'Alex')
    await user.click(within(form).getByRole('checkbox', { name: 'Primary contact' }))
    await user.click(within(form).getByRole('button', { name: 'Add relocation contact' }))
    await user.type(within(form).getByLabelText('Contact name'), 'Blair')
    await user.click(within(form).getByRole('checkbox', { name: 'Primary contact' }))
    await user.click(within(form).getByRole('button', { name: 'Add relocation contact' }))
    expect(await within(form).findByText('Only one contact can be the primary contact.')).toBeInTheDocument()

    await user.selectOptions(within(form).getByLabelText('Eligibility type'), 'Moving Expenses')
    await user.type(within(form).getByLabelText('Claim limit'), '5000')
    await user.click(within(form).getByRole('button', { name: 'Add eligibility' }))
    fireEvent.change(within(form).getByLabelText('Log date and time'), { target: { value: '2026-03-04T15:30' } })
    await user.type(within(form).getByLabelText('Log discussion notes'), 'Left a message.')
    await user.click(within(form).getByRole('button', { name: 'Add contact log entry' }))
    await user.click(within(form).getByRole('button', { name: 'Add relocation' }))

    await waitFor(() => expect(fake.relocations).toHaveLength(1))
    expect(fake.relocations[0].details).toMatchObject({ relocationNumber: 'R-7', displaceeName: 'Kamali', relocationType: 'Residential', ginDeliveredDate: '2026-03-07' })
    expect(fake.relocations[0].contacts).toMatchObject([{ name: 'Alex', isPrimary: true }])
    expect(fake.relocations[0].eligibilities).toMatchObject([{ eligibilityType: 'Moving Expenses', claimLimit: 5000 }])
    expect(fake.relocations[0].contactLog[0].notes).toBe('Left a message.')
  })

  it('changes a relocation, attaches a file and removes it', async () => {
    const { fake, user } = open('Admin', { relocations: [{ parcelId: 'pc1', id: 'rl1' }] })
    await tab(user, 'Relocation')
    await user.click(await screen.findByRole('button', { name: 'Edit relocation R-1' }))
    const form = await screen.findByRole('form', { name: 'Change relocation' })

    fireEvent.change(within(form).getByLabelText('Target vacate date'), { target: { value: '2026-09-01' } })
    await user.click(within(form).getByRole('button', { name: 'Save changes' }))
    await waitFor(() => expect(fake.relocations[0].details.targetVacateDate).toBe('2026-09-01'))

    await user.click(await screen.findByRole('button', { name: 'Edit relocation R-1' }))
    const panel = await screen.findByRole('region', { name: 'Attachments for relocation R-1' })
    await user.upload(within(panel).getByLabelText('Upload document'), new File(['x'], 'plan.pdf', { type: 'application/pdf' }))
    expect(await within(panel).findByText('plan.pdf')).toBeInTheDocument()
    expect(fake.documents[0]).toMatchObject({ entityType: 'ParcelRelocation', entityId: 'rl1' })
    await user.click(screen.getByRole('button', { name: 'Remove relocation R-1' }))
    await waitFor(() => expect(fake.relocations).toHaveLength(0))
  })
})

describe.each(ROLES.map((r) => [r]))('what %s sees and can do on the parcel tabs (AT-X-46, AT-X-47)', (role) => {
  it('shows the Contact log tab and its controls exactly as the Contact Log page allows', async () => {
    const { user } = open(role)
    await screen.findByRole('heading', { name: /PN-1001/ })

    expect(!!screen.queryByRole('tab', { name: 'Contact log' })).toBe(can(role, 'contactlog', 'read'))
    if (!can(role, 'contactlog', 'read')) return
    await tab(user, 'Contact log')
    await screen.findByRole('table', { name: 'Contact log' })
    expect(!!screen.queryByRole('form', { name: 'Log a contact' })).toBe(can(role, 'contactlog', 'create'))
    expect(!!screen.queryByRole('button', { name: 'Edit contact CL-PN-1001-001' })).toBe(can(role, 'contactlog', 'update'))
    expect(!!screen.queryByRole('button', { name: 'Remove contact CL-PN-1001-001' })).toBe(can(role, 'contactlog', 'delete'))
  })

  it('shows the Ownership details and Parcel location tabs exactly as their pages allow', async () => {
    const { user } = open(role, { ownerships: [{ parcelId: 'pc1', id: 'ow1', ownershipNumber: 1, ownerName: 'Kamali' }] })
    await screen.findByRole('heading', { name: /PN-1001/ })

    expect(!!screen.queryByRole('tab', { name: 'Parcel location' })).toBe(can(role, 'parcellocation', 'read'))
    expect(!!screen.queryByRole('tab', { name: 'Ownership details' })).toBe(can(role, 'ownership', 'read'))
    if (!can(role, 'ownership', 'read')) return
    await tab(user, 'Ownership details')
    await screen.findByRole('table', { name: 'Ownership details' })
    expect(!!screen.queryByRole('form', { name: 'Add an ownership' })).toBe(can(role, 'ownership', 'create'))
    expect(!!screen.queryByRole('button', { name: 'Edit ownership 1' })).toBe(can(role, 'ownership', 'update'))
    expect(!!screen.queryByRole('button', { name: 'Remove ownership 1' })).toBe(can(role, 'ownership', 'delete'))
  })

  it('shows the Title encumbrances tab and its controls exactly as the Title Encumbrances page allows', async () => {
    const { user } = open(role, { encumbrances: [{ parcelId: 'pc1', id: 'en1', number: 'ENC-PN-1001-001' }] })
    await screen.findByRole('heading', { name: /PN-1001/ })

    expect(!!screen.queryByRole('tab', { name: 'Title encumbrances' })).toBe(can(role, 'titleencumbrances', 'read'))
    if (!can(role, 'titleencumbrances', 'read')) return
    await tab(user, 'Title encumbrances')
    await screen.findByRole('table', { name: 'Title encumbrances' })
    expect(!!screen.queryByRole('form', { name: 'Add an encumbrance' })).toBe(can(role, 'titleencumbrances', 'create'))
    expect(!!screen.queryByRole('button', { name: 'Edit encumbrance ENC-PN-1001-001' })).toBe(can(role, 'titleencumbrances', 'update'))
    expect(!!screen.queryByRole('button', { name: 'Remove encumbrance ENC-PN-1001-001' })).toBe(can(role, 'titleencumbrances', 'delete'))
  })

  it('shows the Title and closing tab and its controls exactly as the Title and Closing page allows', async () => {
    const withRecord = open(role, { titleClosings: [{ parcelId: 'pc1', id: 'tc1', number: 'TC-PN-1001' }] })
    await screen.findByRole('heading', { name: /PN-1001/ })

    expect(!!screen.queryByRole('tab', { name: 'Title and closing' })).toBe(can(role, 'titleclosing', 'read'))
    if (!can(role, 'titleclosing', 'read')) return
    await tab(withRecord.user, 'Title and closing')
    await screen.findByRole('form', { name: 'Title and closing' })
    expect(!!screen.queryByRole('button', { name: 'Save changes' })).toBe(can(role, 'titleclosing', 'update'))
    expect(!!screen.queryByRole('button', { name: 'Remove title and closing record' })).toBe(can(role, 'titleclosing', 'delete'))
  })

  it('offers the first save of the Title and closing record only to roles that may create it', async () => {
    const { user } = open(role)
    await screen.findByRole('heading', { name: /PN-1001/ })
    if (!can(role, 'titleclosing', 'read')) return
    await tab(user, 'Title and closing')
    await screen.findByRole('form', { name: 'Title and closing' })

    expect(!!screen.queryByRole('button', { name: 'Save title and closing' })).toBe(can(role, 'titleclosing', 'create'))
  })

  it('shows the Appraisal tab and its controls exactly as the Appraisal page allows (AT-X-51)', async () => {
    const { user } = open(role, { appraisals: [{ parcelId: 'pc1', id: 'ap1', number: 'APR-PN-1001-001' }] })
    await screen.findByRole('heading', { name: /PN-1001/ })

    expect(!!screen.queryByRole('tab', { name: 'Appraisal' })).toBe(can(role, 'parcelappraisal', 'read'))
    if (!can(role, 'parcelappraisal', 'read')) return
    await tab(user, 'Appraisal')
    await screen.findByRole('table', { name: 'Appraisals' })
    expect(!!screen.queryByRole('form', { name: 'Add an appraisal' })).toBe(can(role, 'parcelappraisal', 'create'))
    expect(!!screen.queryByRole('button', { name: 'Edit appraisal APR-PN-1001-001' })).toBe(can(role, 'parcelappraisal', 'update'))
    expect(!!screen.queryByRole('button', { name: 'Remove appraisal APR-PN-1001-001' })).toBe(can(role, 'parcelappraisal', 'delete'))
  })

  it('shows the Negotiation tab and its controls exactly as the Negotiation page allows (AT-X-52)', async () => {
    const withRecord = open(role, { negotiations: [{ parcelId: 'pc1', id: 'ng1', number: 'NEG-PN-1001' }] })
    await screen.findByRole('heading', { name: /PN-1001/ })

    expect(!!screen.queryByRole('tab', { name: 'Negotiation' })).toBe(can(role, 'parcelnegotiation', 'read'))
    if (!can(role, 'parcelnegotiation', 'read')) return
    await tab(withRecord.user, 'Negotiation')
    await screen.findByRole('form', { name: 'Negotiation' })
    expect(!!screen.queryByRole('button', { name: 'Save changes' })).toBe(can(role, 'parcelnegotiation', 'update'))
    expect(!!screen.queryByRole('button', { name: 'Remove negotiation record' })).toBe(can(role, 'parcelnegotiation', 'delete'))
  })

  it('offers the first save of the Negotiation record only to roles that may create it', async () => {
    const { user } = open(role)
    await screen.findByRole('heading', { name: /PN-1001/ })
    if (!can(role, 'parcelnegotiation', 'read')) return
    await tab(user, 'Negotiation')
    await screen.findByRole('form', { name: 'Negotiation' })

    expect(!!screen.queryByRole('button', { name: 'Save negotiation' })).toBe(can(role, 'parcelnegotiation', 'create'))
  })

  it('shows the Questionnaire tab and its controls exactly as the Questionnaire page allows (AT-X-53)', async () => {
    const { user } = open(role, { questionnaires: [{ parcelId: 'pc1', id: 'qs1', number: 'QST-PN-1001-001' }] })
    await screen.findByRole('heading', { name: /PN-1001/ })

    expect(!!screen.queryByRole('tab', { name: 'Questionnaire' })).toBe(can(role, 'parcelquestionnaire', 'read'))
    if (!can(role, 'parcelquestionnaire', 'read')) return
    await tab(user, 'Questionnaire')
    await screen.findByRole('table', { name: 'Questionnaires' })
    expect(!!screen.queryByRole('form', { name: 'Add a questionnaire' })).toBe(can(role, 'parcelquestionnaire', 'create'))
    expect(!!screen.queryByRole('button', { name: 'Edit questionnaire QST-PN-1001-001' })).toBe(can(role, 'parcelquestionnaire', 'update'))
    expect(!!screen.queryByRole('button', { name: 'Remove questionnaire QST-PN-1001-001' })).toBe(can(role, 'parcelquestionnaire', 'delete'))
  })

  it('shows the Condemnation tab and its controls exactly as the Condemnation page allows (AT-X-54)', async () => {
    const withRecord = open(role, { condemnations: [{ parcelId: 'pc1', id: 'cn1', number: 'CON-PN-1001' }] })
    await screen.findByRole('heading', { name: /PN-1001/ })

    expect(!!screen.queryByRole('tab', { name: 'Condemnation' })).toBe(can(role, 'parcelcondemnation', 'read'))
    if (!can(role, 'parcelcondemnation', 'read')) return
    await tab(withRecord.user, 'Condemnation')
    await screen.findByRole('form', { name: 'Condemnation' })
    expect(!!screen.queryByRole('button', { name: 'Save changes' })).toBe(can(role, 'parcelcondemnation', 'update'))
    expect(!!screen.queryByRole('button', { name: 'Remove condemnation record' })).toBe(can(role, 'parcelcondemnation', 'delete'))
  })

  it('shows the Property management tab and its controls exactly as its page allows (AT-X-55)', async () => {
    const withRecord = open(role, { properties: [{ parcelId: 'pc1', id: 'pm1', number: 'PMD-PN-1001' }] })
    await screen.findByRole('heading', { name: /PN-1001/ })

    expect(!!screen.queryByRole('tab', { name: 'Property management & disposition' })).toBe(can(role, 'parcelproperty', 'read'))
    if (!can(role, 'parcelproperty', 'read')) return
    await tab(withRecord.user, 'Property management & disposition')
    await screen.findByRole('form', { name: 'Property management and disposition' })
    expect(!!screen.queryByRole('button', { name: 'Save changes' })).toBe(can(role, 'parcelproperty', 'update'))
    expect(!!screen.queryByRole('button', { name: 'Remove property management record' })).toBe(can(role, 'parcelproperty', 'delete'))
  })

  it('shows the Relocation tab and its controls exactly as the Relocation page allows (AT-X-56)', async () => {
    const { user } = open(role, { relocations: [{ parcelId: 'pc1', id: 'rl1' }] })
    await screen.findByRole('heading', { name: /PN-1001/ })

    expect(!!screen.queryByRole('tab', { name: 'Relocation' })).toBe(can(role, 'parcelrelocation', 'read'))
    if (!can(role, 'parcelrelocation', 'read')) return
    await tab(user, 'Relocation')
    await screen.findByRole('table', { name: 'Relocations' })
    expect(!!screen.queryByRole('form', { name: 'Add a relocation' })).toBe(can(role, 'parcelrelocation', 'create'))
    expect(!!screen.queryByRole('button', { name: 'Edit relocation R-1' })).toBe(can(role, 'parcelrelocation', 'update'))
    expect(!!screen.queryByRole('button', { name: 'Remove relocation R-1' })).toBe(can(role, 'parcelrelocation', 'delete'))
  })

  it('shows the Easements tab and its controls exactly as the Easements page allows', async () => {
    const { user } = open(role)
    await screen.findByRole('heading', { name: /PN-1001/ })

    expect(!!screen.queryByRole('tab', { name: 'Easements' })).toBe(can(role, 'easements', 'read'))
    if (!can(role, 'easements', 'read')) return
    await tab(user, 'Easements')
    await screen.findByRole('table', { name: 'Easements' })
    expect(!!screen.queryByRole('form', { name: 'Add an easement' })).toBe(can(role, 'easements', 'create'))
    expect(!!screen.queryByRole('button', { name: 'Edit easement E-1' })).toBe(can(role, 'easements', 'update'))
    expect(!!screen.queryByRole('button', { name: 'Remove easement E-1' })).toBe(can(role, 'easements', 'delete'))
  })
})
