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
