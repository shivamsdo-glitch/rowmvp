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
