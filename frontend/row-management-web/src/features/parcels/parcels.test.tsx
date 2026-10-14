import { beforeEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, screen, waitFor, within } from '@testing-library/react'
import userEventModule from '@testing-library/user-event'
import { ROLES } from '@/api/auth'
import type { Parcel } from '@/api/parcels'
import type { Project } from '@/api/projects'
import { levelFor, permissionRowsFor } from '@/test/contract'
import { fakeParcelsApi, type FakeParcelsOptions } from '@/test/fake-parcels'
import { renderApp } from '@/test/render-app'

beforeEach(() => sessionStorage.clear())

const canCreate = (role: string) => ['S', 'P'].includes(levelFor(role, 'parcels') ?? '')
const canDelete = (role: string) => levelFor(role, 'parcels') === 'P'
const setDate = (label: string, value: string, scope: HTMLElement | Document = document) =>
  fireEvent.change(within(scope as HTMLElement).getByLabelText(new RegExp(`^${label}`)), { target: { value } })

const PROJECTS: Partial<Project>[] = [
  { id: 'p-fed', number: '2026-0001', name: 'SR-20 Widening', oversightType: 'Federal' },
  { id: 'p-loc', number: '2026-0002', name: 'Maple Sidewalk', oversightType: 'Local' },
]

/** The standard milestones with the Notice to owner completed on `noticeOn`. */
const withNotice = (noticeOn: string): Parcel['milestones'] =>
  ['Parcel set up', 'Notice to owner', 'Offer delivered', 'Agreement signed', 'Closed'].map((name, i) => ({
    id: `m${i}`, name, dueOn: null, completedOn: name === 'Notice to owner' ? noticeOn : null, notes: null, isStandard: true, isOverdue: false,
  }))

const SEED: Partial<Parcel>[] = [
  { id: 'pc1', projectId: 'p-loc', number: '001', address: '12 Oak St', areaSqFt: 43560, statusId: 's-ident' },
  { id: 'pc2', projectId: 'p-loc', number: '002', address: '14 Oak St', areaSqFt: 21780, statusId: 's-neg' },
  { id: 'pc3', projectId: 'p-fed', number: '001', address: '9 Pine Rd', areaSqFt: 87120, statusId: 's-ident' },
]

function open(path: string, role = 'ProjectManager', extra: Partial<FakeParcelsOptions> = {}) {
  const fake = fakeParcelsApi({
    role, rows: permissionRowsFor(role), projects: PROJECTS, parcels: SEED, groups: [{ id: 'g1', projectId: 'p-loc', name: 'Main span' }], ...extra,
  })
  const user = userEventModule.setup()
  const app = renderApp({ path, fetchImpl: fake.fetch, signedIn: true })
  return { fake, user, ...app }
}

const listTable = () => screen.findByRole('table', { name: 'Parcels' })
/** The select checkbox of the list row with this address (parcel numbers repeat across projects). */
const pick = (address: string) => within(within(screen.getByRole('table', { name: 'Parcels' })).getByRole('row', { name: new RegExp(address) })).getByRole('checkbox')

// ── Parcel list ──────────────────────────────────────────────────────────

describe('Parcel list (ROW-005, 233, 235, 236)', () => {
  it('lists live parcels with project, area in acres, status and a link to each', async () => {
    open('/parcels')

    const table = await listTable()
    const row = within(table).getByRole('row', { name: /12 Oak St/ })
    expect(within(row).getByRole('link', { name: '001' })).toHaveAttribute('href', '/parcels/pc1')
    expect(within(row).getByText('2026-0002')).toBeInTheDocument()
    expect(within(row).getByText('1 ac')).toBeInTheDocument()
    expect(within(row).getByText('Identified')).toBeInTheDocument()
    expect(within(table).getAllByRole('row')).toHaveLength(4) // header + 3
  })

  it('searches, and filters by project, status and group', async () => {
    const { fake, user } = open('/parcels')
    await listTable()

    await user.type(screen.getByLabelText('Search parcels'), 'pine')
    await waitFor(() => expect(screen.queryByText('12 Oak St')).not.toBeInTheDocument())
    expect(screen.getByText('9 Pine Rd')).toBeInTheDocument()
    expect(fake.calls.some((c) => c.key.includes('search=pine'))).toBe(true)

    await user.clear(screen.getByLabelText('Search parcels'))
    expect(await screen.findByText('12 Oak St')).toBeInTheDocument()
    await user.selectOptions(screen.getByLabelText('Status'), 's-neg')
    await waitFor(() => expect(screen.queryByText('12 Oak St')).not.toBeInTheDocument())
    expect(screen.getByText('14 Oak St')).toBeInTheDocument()

    await user.selectOptions(screen.getByLabelText('Status'), '')
    expect(await screen.findByText('12 Oak St')).toBeInTheDocument()
    expect(screen.getByLabelText('Group')).toBeDisabled()
    await user.selectOptions(screen.getByLabelText('Project'), 'p-fed')
    await waitFor(() => expect(screen.queryByText('12 Oak St')).not.toBeInTheDocument())
    expect(await screen.findByRole('option', { name: 'All groups' })).toBeInTheDocument()
  })

  it('opens already narrowed to one project when linked from that project', async () => {
    open('/parcels?projectId=p-fed')

    const table = await listTable()
    expect(within(table).getByText('9 Pine Rd')).toBeInTheDocument()
    expect(within(table).queryByText('12 Oak St')).not.toBeInTheDocument()
    expect(screen.getByLabelText('Project')).toHaveValue('p-fed')
  })

  it("the project's Parcels tab links to its own parcels", async () => {
    const { user } = open('/projects/p-fed')
    await user.click(await screen.findByRole('tab', { name: 'Parcels' }))

    expect(await screen.findByRole('link', { name: /Open this project's parcels/ })).toHaveAttribute('href', '/parcels?projectId=p-fed')
  })

  it('shows the parcels as a board of status columns with counts', async () => {
    const { user } = open('/parcels')
    await listTable()

    await user.click(screen.getByRole('button', { name: 'Board' }))

    const board = await screen.findByRole('region', { name: 'Parcel board' })
    const identified = within(board).getByRole('region', { name: 'Identified column' })
    expect(within(identified).getAllByRole('listitem')).toHaveLength(2)
    expect(within(identified).getByRole('link', { name: '2026-0002 · 001' })).toHaveAttribute('href', '/parcels/pc1')
    expect(within(within(board).getByRole('region', { name: 'Negotiation column' })).getAllByRole('listitem')).toHaveLength(1)
    expect(within(board).queryByRole('region', { name: 'Closed column' })).not.toBeInTheDocument()
  })

  it('says when the board shows only part of the parcels', async () => {
    const many = Array.from({ length: 205 }, (_, i) => ({ id: `bulk${i}`, projectId: 'p-loc', number: String(i + 1).padStart(3, '0'), address: `${i} Bulk Rd`, statusId: 's-ident' }))
    const { user } = open('/parcels', 'ProjectManager', { parcels: many })
    await listTable()

    await user.click(screen.getByRole('button', { name: 'Board' }))

    expect(await screen.findByText('Showing the first 200 of 205 parcels. Narrow the filters to see the rest.')).toBeInTheDocument()
    expect(within(screen.getByRole('region', { name: 'Identified column' })).getAllByRole('listitem')).toHaveLength(200)
  })

  it('does not mention a limit when the board shows everything', async () => {
    const { user } = open('/parcels')
    await listTable()

    await user.click(screen.getByRole('button', { name: 'Board' }))

    await screen.findByRole('region', { name: 'Parcel board' })
    expect(screen.queryByText(/Showing the first/)).not.toBeInTheDocument()
  })

  it('says so plainly when nothing matches, when there are no parcels, and when loading fails', async () => {
    const first = open('/parcels')
    await listTable()
    await first.user.type(screen.getByLabelText('Search parcels'), 'zzz')
    expect(await screen.findByText('No parcels match your filters')).toBeInTheDocument()
    first.unmount()

    const empty = open('/parcels', 'ProjectManager', { parcels: [] })
    expect(await screen.findByText('No parcels yet')).toBeInTheDocument()
    expect(screen.getByText('Create the first parcel to get started.')).toBeInTheDocument()
    empty.unmount()

    open('/parcels', 'ProjectManager', { parcelListFails: true })
    expect(await screen.findByText('Parcels could not be loaded')).toBeInTheDocument()
  })

  it('leaves out parcels that were split or combined away', async () => {
    open('/parcels', 'ProjectManager', { parcels: [...SEED, { id: 'old', projectId: 'p-loc', number: '009', address: 'Gone Ln', state: 'Split' }] })
    await listTable()

    expect(screen.queryByText('Gone Ln')).not.toBeInTheDocument()
  })
})

// ── Creating ─────────────────────────────────────────────────────────────

describe('Creating a parcel (ROW-005, 011)', () => {
  it('asks for the project and an area, then creates the parcel with the next number and opens it', async () => {
    const { fake, user } = open('/parcels')
    await user.click(await screen.findByRole('button', { name: 'New parcel' }))
    const form = screen.getByRole('form', { name: 'New parcel' })

    await user.click(within(form).getByRole('button', { name: 'Create parcel' }))
    expect(await within(form).findByText('Project is required.')).toBeInTheDocument()
    expect(within(form).getByText('Area must be zero or more square feet.')).toBeInTheDocument()
    expect(fake.calls.some((c) => c.key === 'POST /api/v1/parcels')).toBe(false)

    await user.selectOptions(within(form).getByLabelText(/^Project/), 'p-loc')
    await user.type(within(form).getByLabelText(/^Area/), '5000')
    await user.type(within(form).getByLabelText('Address'), '1 New Rd')
    await user.click(within(form).getByRole('button', { name: 'Create parcel' }))

    expect(await screen.findByRole('heading', { name: '2026-0002 · Parcel 003' })).toBeInTheDocument()
    expect(fake.calls.find((c) => c.key === 'POST /api/v1/parcels')!.body).toMatchObject({ projectId: 'p-loc', number: null, areaSqFt: 5000, address: '1 New Rd', statusId: null })
  })

  it('shows a clash with an existing parcel number', async () => {
    const { user } = open('/parcels')
    await user.click(await screen.findByRole('button', { name: 'New parcel' }))
    const form = screen.getByRole('form', { name: 'New parcel' })

    await user.selectOptions(within(form).getByLabelText(/^Project/), 'p-loc')
    await user.type(within(form).getByLabelText('Parcel number'), '001')
    await user.type(within(form).getByLabelText(/^Area/), '100')
    await user.click(within(form).getByRole('button', { name: 'Create parcel' }))

    expect(await within(form).findByText("This project already has a parcel numbered '001'.")).toBeInTheDocument()
  })

  it('shows the agency custom fields and enforces the required ones', async () => {
    const { fake, user } = open('/parcels', 'ProjectManager', {
      customFields: [
        { key: 'zoning', label: 'Zoning', fieldType: 'List', options: ['Residential', 'Commercial'], isRequired: true },
        { key: 'frontage', label: 'Frontage (ft)', fieldType: 'Number' },
        { key: 'retired', label: 'Old field', isActive: false },
      ],
    })
    await user.click(await screen.findByRole('button', { name: 'New parcel' }))
    const form = screen.getByRole('form', { name: 'New parcel' })
    expect(within(form).queryByLabelText(/Old field/)).not.toBeInTheDocument()

    await user.selectOptions(within(form).getByLabelText(/^Project/), 'p-loc')
    await user.type(within(form).getByLabelText(/^Area/), '100')
    await user.click(within(form).getByRole('button', { name: 'Create parcel' }))
    expect(await within(form).findAllByText('Zoning is required.')).not.toHaveLength(0)

    await user.selectOptions(within(form).getByLabelText(/^Zoning/), 'Commercial')
    await user.type(within(form).getByLabelText('Frontage (ft)'), '80')
    await user.click(within(form).getByRole('button', { name: 'Create parcel' }))

    expect(await screen.findByRole('heading', { name: /Parcel 003/ })).toBeInTheDocument()
    expect(fake.parcels.find((p) => p.number === '003')!.customValues).toEqual({ zoning: 'Commercial', frontage: '80' })
  })

  it('cancel closes the form without sending anything', async () => {
    const { fake, user } = open('/parcels')
    await user.click(await screen.findByRole('button', { name: 'New parcel' }))

    await user.click(screen.getByRole('button', { name: 'Cancel' }))

    expect(screen.queryByRole('form', { name: 'New parcel' })).not.toBeInTheDocument()
    expect(fake.calls.some((c) => c.key.startsWith('POST'))).toBe(false)
  })
})

// ── Batch update and combine ─────────────────────────────────────────────

describe('Updating and combining several parcels (ROW-007, 236)', () => {
  it('sets one status on every selected parcel at once', async () => {
    const { fake, user } = open('/parcels')
    await listTable()

    await user.click(pick('12 Oak St'))
    expect(screen.getByText('1 parcel selected')).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Select all on this page' }))
    expect(screen.getByText('3 parcels selected')).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Apply to selected' }))
    expect(await screen.findByText('Choose a status or a group to apply.')).toBeInTheDocument()

    await user.selectOptions(screen.getByLabelText('Set status'), 's-closed')
    await user.click(screen.getByRole('button', { name: 'Apply to selected' }))

    expect(await screen.findByText('Updated 3 parcels.')).toBeInTheDocument()
    const sent = fake.calls.find((c) => c.key === 'POST /api/v1/parcels/batch-update')!.body
    expect([...sent.parcelIds].sort()).toEqual(['pc1', 'pc2', 'pc3'])
    expect(sent).toMatchObject({ statusId: 's-closed', clearGroup: false })
    expect(fake.parcels.every((p) => p.statusName === 'Closed')).toBe(true)
    expect(screen.queryByText(/parcels? selected/)).not.toBeInTheDocument()
  })

  it('moves selected parcels of one project into a group, or out of it', async () => {
    const { fake, user } = open('/parcels')
    await listTable()
    await user.click(pick('14 Oak St'))
    await user.click(pick('12 Oak St'))

    await screen.findByRole('option', { name: 'Main span' })
    await user.selectOptions(screen.getByLabelText('Set group'), 'g1')
    await user.click(screen.getByRole('button', { name: 'Apply to selected' }))
    expect(await screen.findByText('Updated 2 parcels.')).toBeInTheDocument()
    expect(fake.parcels.filter((p) => p.groupId === 'g1')).toHaveLength(2)

    await user.click(pick('14 Oak St'))
    await screen.findByRole('option', { name: 'Remove from group' })
    await user.selectOptions(screen.getByLabelText('Set group'), '__none')
    await user.click(screen.getByRole('button', { name: 'Apply to selected' }))
    await waitFor(() => expect(fake.parcels.find((p) => p.id === 'pc2')!.groupId).toBeNull())
  })

  it('only offers group changes when the selected parcels share a project', async () => {
    const { user } = open('/parcels')
    await user.click(await screen.findByRole('button', { name: 'Select all on this page' }))

    expect(await screen.findByLabelText('Set group')).toBeDisabled()
  })

  it('combines two selected parcels into one new parcel and opens it', async () => {
    const { fake, user } = open('/parcels')
    await listTable()
    await user.click(pick('14 Oak St'))
    expect(screen.getByRole('button', { name: 'Combine selected' })).toBeDisabled()

    await user.click(pick('12 Oak St'))
    await user.click(screen.getByRole('button', { name: 'Combine selected' }))

    expect(await screen.findByRole('heading', { name: '2026-0002 · Parcel 003' })).toBeInTheDocument()
    expect(screen.getByText('65,340 sq ft', { exact: false })).toBeInTheDocument()
    expect(fake.parcels.filter((p) => p.state === 'Combined')).toHaveLength(2)
  })

  it('reports why parcels from different projects cannot be combined', async () => {
    const { user } = open('/parcels')
    await user.click(await screen.findByRole('button', { name: 'Select all on this page' }))

    await user.click(screen.getByRole('button', { name: 'Combine selected' }))

    expect(await screen.findByText('Parcels from different projects cannot be combined.')).toBeInTheDocument()
  })
})

// ── Parcel detail ────────────────────────────────────────────────────────

describe('Parcel detail and synopsis (ROW-008, 073, 233)', () => {
  const TRACT = { id: 't1', projectId: 'p-loc', number: 'T-1001', taxParcelNumbers: ['APN-1', 'APN-2'] }

  it('shows the synopsis: project, status, area, tract, tax numbers, workflow step and interests', async () => {
    open('/parcels/pc1', 'ProjectManager', {
      tracts: [TRACT],
      parcels: [{ ...SEED[0], parentTractId: 't1', legalDescription: 'Lot 4, Block 2', interests: [{ id: 'i1', type: 'FeeTaking', areaSqFt: 1000, notes: null }] }],
    })

    expect(await screen.findByRole('heading', { name: '2026-0002 · Parcel 001' })).toBeInTheDocument()
    const synopsis = screen.getByRole('region', { name: 'Parcel synopsis' })
    expect(within(synopsis).getByText('2026-0002 · Maple Sidewalk')).toBeInTheDocument()
    expect(within(synopsis).getByText('1 ac', { exact: false })).toBeInTheDocument()
    expect(within(synopsis).getByText('T-1001')).toBeInTheDocument()
    expect(within(synopsis).getByText('APN-1, APN-2')).toBeInTheDocument()
    expect(within(synopsis).getByText('Parcel set up')).toBeInTheDocument()
    expect(within(synopsis).getByText(/Lot 4, Block 2/)).toBeInTheDocument()
    expect(screen.getByText('Fee taking — 1,000 sq ft')).toBeInTheDocument()
  })

  it('shows an overdue statutory deadline with how late it is', async () => {
    open('/parcels/pc1', 'ProjectManager', { parcels: [{ ...SEED[0], milestones: withNotice('2026-05-01') }] })

    const deadlines = await screen.findByRole('table', { name: 'Deadlines' })
    const row = within(deadlines).getByRole('row', { name: /Offer within 90 days/ })
    expect(within(row).getByText('Overdue')).toBeInTheDocument()
    expect(within(row).getByText('76 days late')).toBeInTheDocument()
    expect(within(row).getByText('Jul 30, 2026')).toBeInTheDocument()
  })

  it('prints the synopsis', async () => {
    const print = vi.spyOn(window, 'print').mockImplementation(() => {})
    const { user } = open('/parcels/pc1')

    await user.click(await screen.findByRole('button', { name: /Print synopsis/ }))

    expect(print).toHaveBeenCalledTimes(1)
    print.mockRestore()
  })

  it('says the parcel cannot be opened when it does not exist', async () => {
    open('/parcels/nope')

    expect(await screen.findByText('This parcel could not be opened')).toBeInTheDocument()
  })

  it('edits the parcel and shows the saved values', async () => {
    const { fake, user } = open('/parcels/pc1')
    await user.click(await screen.findByRole('button', { name: 'Edit parcel' }))
    const form = screen.getByRole('form', { name: 'Edit parcel' })
    expect(within(form).getByLabelText('Parcel number')).toHaveValue('001')

    await user.clear(within(form).getByLabelText('Address'))
    await user.type(within(form).getByLabelText('Address'), '99 Elm Ave')
    await user.click(within(form).getByRole('button', { name: 'Save parcel' }))

    await waitFor(() => expect(screen.queryByRole('form', { name: 'Edit parcel' })).not.toBeInTheDocument())
    expect(await screen.findByText('99 Elm Ave')).toBeInTheDocument()
    expect(fake.parcels.find((p) => p.id === 'pc1')!.address).toBe('99 Elm Ave')
  })

  it('shows a superseded parcel as a record only, with no way to change it', async () => {
    open('/parcels/pc1', 'ProjectManager', { parcels: [{ ...SEED[0], state: 'Split' }] })

    expect(await screen.findByText(/This parcel was split and is kept on record only/)).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Edit parcel' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Split' })).not.toBeInTheDocument()
  })
})

// ── Interests ────────────────────────────────────────────────────────────

describe('Interests (ROW-234)', () => {
  it('adds an interest with its area, then removes it', async () => {
    const { fake, user } = open('/parcels/pc1')
    await user.click(await screen.findByRole('tab', { name: 'Interests' }))
    expect(await screen.findByText('No interests recorded')).toBeInTheDocument()

    const form = screen.getByRole('form', { name: 'Add an interest' })
    await user.click(within(form).getByRole('button', { name: 'Add interest' }))
    expect(await within(form).findByText('Area must be zero or more square feet.')).toBeInTheDocument()

    await user.selectOptions(within(form).getByLabelText(/^Interest type/), 'PermanentEasement')
    await user.type(within(form).getByLabelText(/^Interest area/), '2500')
    await user.click(within(form).getByRole('button', { name: 'Add interest' }))

    const row = await screen.findByRole('row', { name: /Permanent easement/ })
    expect(within(row).getByText('2,500 sq ft')).toBeInTheDocument()
    expect(fake.parcels[0].interests).toHaveLength(1)

    await user.click(screen.getByRole('button', { name: 'Remove Permanent easement interest' }))
    await waitFor(() => expect(fake.parcels[0].interests).toHaveLength(0))
    expect(await screen.findByText('No interests recorded')).toBeInTheDocument()
  })

  it('a read-only role sees the interests but no way to add or remove them', async () => {
    open('/parcels/pc1', 'Appraiser', { parcels: [{ ...SEED[0], interests: [{ id: 'i1', type: 'FeeTaking', areaSqFt: 100, notes: null }] }] })
    await userEventModule.setup().click(await screen.findByRole('tab', { name: 'Interests' }))

    expect(await screen.findByRole('row', { name: /Fee taking/ })).toBeInTheDocument()
    expect(screen.queryByRole('form', { name: 'Add an interest' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /Remove/ })).not.toBeInTheDocument()
  })
})

// ── Milestones ───────────────────────────────────────────────────────────

describe('Milestones and deadlines (ROW-010, 073, 074)', () => {
  it('adds a custom milestone, completes a standard one and removes the custom one', async () => {
    const { fake, user } = open('/parcels/pc1')
    await user.click(await screen.findByRole('tab', { name: 'Milestones & deadlines' }))

    const add = screen.getByRole('form', { name: 'Add a milestone' })
    await user.click(within(add).getByRole('button', { name: 'Add milestone' }))
    expect(await within(add).findByText('Milestone name is required.')).toBeInTheDocument()
    await user.type(within(add).getByLabelText(/^Milestone name/), 'Right of entry')
    setDate('Due on', '2026-12-01', add)
    await user.click(within(add).getByRole('button', { name: 'Add milestone' }))
    const custom = await screen.findByRole('row', { name: /Right of entry/ })
    expect(within(custom).getByText('Dec 1, 2026')).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Edit milestone Offer delivered' }))
    const edit = screen.getByRole('form', { name: 'Edit milestone Offer delivered' })
    setDate('Completed on', '2026-10-01', edit)
    await user.click(within(edit).getByRole('button', { name: 'Save milestone' }))
    expect(await screen.findByText('Oct 1, 2026')).toBeInTheDocument()
    expect(fake.parcels[0].milestones.find((m) => m.name === 'Offer delivered')!.completedOn).toBe('2026-10-01')

    expect(screen.queryByRole('button', { name: 'Remove milestone Offer delivered' })).not.toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Remove milestone Right of entry' }))
    await waitFor(() => expect(screen.queryByRole('row', { name: /Right of entry/ })).not.toBeInTheDocument())
  })

  it('refuses a duplicate milestone name', async () => {
    const { user } = open('/parcels/pc1')
    await user.click(await screen.findByRole('tab', { name: 'Milestones & deadlines' }))
    const add = screen.getByRole('form', { name: 'Add a milestone' })

    await user.type(within(add).getByLabelText(/^Milestone name/), 'Closed')
    await user.click(within(add).getByRole('button', { name: 'Add milestone' }))

    expect(await screen.findByText("This parcel already has a milestone named 'Closed'.")).toBeInTheDocument()
  })

  it('flags a milestone that is past due and not completed', async () => {
    open('/parcels/pc1', 'ProjectManager', {
      parcels: [{ ...SEED[0], milestones: [{ id: 'm1', name: 'Right of entry', dueOn: '2026-09-01', completedOn: null, notes: null, isStandard: false, isOverdue: false }] }],
    })
    await userEventModule.setup().click(await screen.findByRole('tab', { name: 'Milestones & deadlines' }))

    expect(within(await screen.findByRole('row', { name: /Right of entry/ })).getByText('Overdue')).toBeInTheDocument()
  })

  it('explains a refused milestone date next to the field and keeps the milestone as it was', async () => {
    const { fake, user } = open('/parcels/pc1')
    await user.click(await screen.findByRole('tab', { name: 'Milestones & deadlines' }))
    await user.click(screen.getByRole('button', { name: 'Edit milestone Offer delivered' }))
    const edit = screen.getByRole('form', { name: 'Edit milestone Offer delivered' })

    setDate('Completed on', '2026-10-20', edit) // after "today" (14 Oct 2026)
    await user.click(within(edit).getByRole('button', { name: 'Save milestone' }))

    expect(await within(edit).findByText('A milestone cannot be completed in the future.')).toBeInTheDocument()
    expect(within(edit).getByLabelText('Completed on')).toHaveAttribute('aria-invalid', 'true')
    expect(within(edit).getByText('Please correct the highlighted fields.')).toBeInTheDocument()
    expect(fake.parcels[0].milestones.find((m) => m.name === 'Offer delivered')!.completedOn).toBeNull()

    setDate('Completed on', '2026-10-01', edit)
    setDate('Due date', '1899-01-01', edit)
    await user.click(within(edit).getByRole('button', { name: 'Save milestone' }))
    expect(await within(edit).findByText('Enter a valid due date.')).toBeInTheDocument()
  })

  it('shows a refused due date on the add form, beside the field', async () => {
    const { user } = open('/parcels/pc1')
    await user.click(await screen.findByRole('tab', { name: 'Milestones & deadlines' }))
    const add = screen.getByRole('form', { name: 'Add a milestone' })

    await user.type(within(add).getByLabelText(/^Milestone name/), 'Stakes set')
    setDate('Due on', '2101-01-01', add)
    await user.click(within(add).getByRole('button', { name: 'Add milestone' }))

    expect(await within(add).findByText('Enter a valid due date.')).toBeInTheDocument()
    expect(within(add).getByLabelText('Due on')).toHaveAttribute('aria-invalid', 'true')
  })

  it("the notice milestone's completion date is locked in the editor and points to where it is set", async () => {
    const { user } = open('/parcels/pc1', 'ProjectManager', { parcels: [{ ...SEED[0], milestones: withNotice('2026-05-01') }] })
    await user.click(await screen.findByRole('tab', { name: 'Milestones & deadlines' }))

    await user.click(screen.getByRole('button', { name: 'Edit milestone Notice to owner' }))
    const edit = screen.getByRole('form', { name: 'Edit milestone Notice to owner' })

    expect(within(edit).getByLabelText('Completed on')).toBeDisabled()
    expect(within(edit).getByLabelText('Completed on')).toHaveValue('2026-05-01')
    expect(within(edit).getByText('Set by recording the notice on the Site visits & notice tab.')).toBeInTheDocument()
    // Its due date and notes can still be saved, with the completion date untouched.
    setDate('Due date', '2026-08-01', edit)
    await user.click(within(edit).getByRole('button', { name: 'Save milestone' }))
    await waitFor(() => expect(screen.queryByRole('form', { name: 'Edit milestone Notice to owner' })).not.toBeInTheDocument())
  })

  it('recording the notice to owner starts the offer deadline', async () => {
    const { user } = open('/parcels/pc1')
    await user.click(await screen.findByRole('tab', { name: 'Site visits & notice' }))
    const form = screen.getByRole('form', { name: 'Notice to owner' })

    setDate('Notice issued on', '2026-10-10', form)
    await user.click(within(form).getByRole('button', { name: 'Save notice' }))
    expect(await within(form).findByText('Choose how the notice was delivered.')).toBeInTheDocument()
    await user.selectOptions(within(form).getByLabelText('Delivery method'), 'CertifiedMail')
    await user.click(within(form).getByRole('button', { name: 'Save notice' }))
    expect(await within(form).findByText('Notice saved.')).toBeInTheDocument()

    await user.click(screen.getByRole('tab', { name: 'Milestones & deadlines' }))
    const row = within(await screen.findByRole('table', { name: 'Deadlines' })).getByRole('row', { name: /Offer within 90 days/ })
    expect(within(row).getByText('Jan 8, 2027')).toBeInTheDocument()
    expect(within(row).getByText('Pending')).toBeInTheDocument()
  })
})

// ── Workflow and step gating ─────────────────────────────────────────────

describe('Workflow and ordered step gating (ROW-071, 072)', () => {
  const atStep = (key: string, projectId = 'p-loc'): Partial<FakeParcelsOptions> => ({
    parcels: [{ ...SEED[0], projectId, workflow: { steps: [], currentKey: key, nextKey: null, acquisitionGateOpen: false, canOverride: false } }],
  })

  it('moves to the next step in order without a reason and records it', async () => {
    const { fake, user } = open('/parcels/pc1')
    await user.click(await screen.findByRole('tab', { name: 'Workflow' }))
    expect(within(screen.getByRole('list', { name: 'Workflow steps' })).getByText('Parcel set up').closest('li')).toHaveAttribute('aria-current', 'step')

    await user.click(screen.getByRole('button', { name: 'Advance to Title & survey' }))

    await waitFor(() => expect(screen.getByRole('button', { name: 'Advance to Appraisal' })).toBeInTheDocument())
    expect(fake.calls.find((c) => c.key.endsWith('/advance'))!.body).toEqual({ targetStepKey: 'title_survey', overrideReason: null })
    const history = within(screen.getByRole('table', { name: 'Step history' }))
    expect(history.getByRole('row', { name: /Parcel set up.*Title & survey/ })).toBeInTheDocument()
    expect(history.getByText('No')).toBeInTheDocument()
  })

  it('holds the offer step while the project gate is closed, and says it cannot be overridden', async () => {
    open('/parcels/pc1', 'ProjectManager', atStep('appraisal', 'p-fed'))
    await userEventModule.setup().click(await screen.findByRole('tab', { name: 'Workflow' }))

    expect(await screen.findByText('Offer is on hold.')).toBeInTheDocument()
    expect(screen.getByText(/This cannot be overridden/)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Advance to Offer' })).toBeDisabled()
  })

  it('opens the offer step once the project gate is open', async () => {
    const { user } = open('/parcels/pc1', 'ProjectManager', atStep('appraisal'))
    await user.click(await screen.findByRole('tab', { name: 'Workflow' }))

    await user.click(await screen.findByRole('button', { name: 'Advance to Offer' }))

    await waitFor(() => expect(screen.getByRole('button', { name: 'Advance to Negotiation' })).toBeInTheDocument())
  })

  it('needs a written reason to skip ahead, then records the override', async () => {
    const { fake, user } = open('/parcels/pc1', 'ProjectManager')
    await user.click(await screen.findByRole('tab', { name: 'Workflow' }))
    const form = screen.getByRole('form', { name: 'Move to another step' })

    await user.selectOptions(within(form).getByLabelText('Move to step'), 'appraisal')
    await user.click(within(form).getByRole('button', { name: 'Move parcel' }))
    expect(await within(form).findByText('A reason is required when moving out of order.')).toBeInTheDocument()
    expect(fake.calls.some((c) => c.key.endsWith('/advance'))).toBe(false)

    await user.type(within(form).getByLabelText(/^Reason for the override/), 'Appraisal ordered early for hardship')
    await user.click(within(form).getByRole('button', { name: 'Move parcel' }))

    const history = within(await screen.findByRole('table', { name: 'Step history' }))
    const row = history.getByRole('row', { name: /Appraisal ordered early for hardship/ })
    expect(within(row).getByText('Override')).toBeInTheDocument()
  })

  it('does not let an override jump into a held offer step', async () => {
    const { user } = open('/parcels/pc3', 'ProjectManager', { parcels: [{ ...SEED[2] }] })
    await user.click(await screen.findByRole('tab', { name: 'Workflow' }))
    const form = screen.getByRole('form', { name: 'Move to another step' })

    await user.selectOptions(within(form).getByLabelText('Move to step'), 'closing')

    expect(within(form).getByText(/on hold until the project's acquisition gate opens/)).toBeInTheDocument()
    expect(within(form).getByRole('button', { name: 'Move parcel' })).toBeDisabled()
  })

  it('lets an override go backwards with a reason', async () => {
    const { fake, user } = open('/parcels/pc1', 'ProjectManager', atStep('appraisal'))
    await user.click(await screen.findByRole('tab', { name: 'Workflow' }))
    const form = screen.getByRole('form', { name: 'Move to another step' })

    await user.selectOptions(within(form).getByLabelText('Move to step'), 'title_survey')
    await user.type(within(form).getByLabelText(/^Reason for the override/), 'Survey needs redoing')
    await user.click(within(form).getByRole('button', { name: 'Move parcel' }))

    await waitFor(() => expect(fake.parcels[0].workflow.currentKey).toBe('title_survey'))
  })

  it('a role that may update parcels but not override can only advance in order', async () => {
    const rows = permissionRowsFor('Negotiator').map((r) => (r.pageKey === 'parcels' ? { ...r, canUpdate: true } : r))
    open('/parcels/pc1', 'Negotiator', { rows })
    await userEventModule.setup().click(await screen.findByRole('tab', { name: 'Workflow' }))

    expect(await screen.findByRole('button', { name: 'Advance to Title & survey' })).toBeEnabled()
    expect(screen.queryByRole('form', { name: 'Move to another step' })).not.toBeInTheDocument()
    expect(screen.getByText(/Only a project manager, an administrator or another role your agency has authorised/)).toBeInTheDocument()
  })

  it('an extra role the administrator added gets the override form', async () => {
    const rows = permissionRowsFor('Negotiator').map((r) => (r.pageKey === 'parcels' ? { ...r, canUpdate: true } : r))
    const { fake } = open('/parcels/pc1', 'Negotiator', { rows })
    fake.stepGate.extraOverrideRoles.push('Negotiator')
    await userEventModule.setup().click(await screen.findByRole('tab', { name: 'Workflow' }))

    expect(await screen.findByRole('form', { name: 'Move to another step' })).toBeInTheDocument()
  })

  it('a read-only role sees the steps and history but no controls', async () => {
    open('/parcels/pc1', 'Appraiser')
    await userEventModule.setup().click(await screen.findByRole('tab', { name: 'Workflow' }))

    expect(await screen.findByRole('list', { name: 'Workflow steps' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /^Advance to/ })).not.toBeInTheDocument()
    expect(screen.queryByRole('form', { name: 'Move to another step' })).not.toBeInTheDocument()
  })
})

// ── Site visits ──────────────────────────────────────────────────────────

describe('Site visits (ROW-237)', () => {
  it('records a visit with whether the owner was reached, then removes it', async () => {
    const { fake, user } = open('/parcels/pc1')
    await user.click(await screen.findByRole('tab', { name: 'Site visits & notice' }))
    expect(await screen.findByText('No site visits yet')).toBeInTheDocument()
    const form = screen.getByRole('form', { name: 'Record a site visit' })

    await user.click(within(form).getByRole('button', { name: 'Record visit' }))
    expect(await within(form).findByText('Visit date is required.')).toBeInTheDocument()

    setDate('Visit date', '2026-10-12', form)
    await user.click(within(form).getByLabelText('Owner contact confirmed'))
    await user.type(within(form).getByLabelText('Visit notes'), 'Owner showed the boundary')
    await user.click(within(form).getByRole('button', { name: 'Record visit' }))

    const row = await screen.findByRole('row', { name: /Oct 12, 2026/ })
    expect(within(row).getByText('Yes')).toBeInTheDocument()
    expect(within(row).getByText('Owner showed the boundary')).toBeInTheDocument()
    expect(fake.parcels[0].siteVisits).toHaveLength(1)

    await user.click(screen.getByRole('button', { name: 'Remove visit of Oct 12, 2026' }))
    await waitFor(() => expect(fake.parcels[0].siteVisits).toHaveLength(0))
  })
})

// ── Split ────────────────────────────────────────────────────────────────

describe('Splitting a parcel (ROW-007)', () => {
  it('shows the remnant as parts are entered and refuses parts larger than the parcel', async () => {
    const { fake, user } = open('/parcels/pc1')
    await user.click(await screen.findByRole('button', { name: 'Split' }))
    const form = screen.getByRole('form', { name: 'Split parts' })
    expect(within(form).getByText('43,560 sq ft')).toBeInTheDocument()

    await user.type(within(form).getByLabelText('Part 1 area (sq ft)'), '10000')
    await user.type(within(form).getByLabelText('Part 2 area (sq ft)'), '20000')
    expect(within(form).getByText('13,560 sq ft')).toBeInTheDocument()

    await user.clear(within(form).getByLabelText('Part 2 area (sq ft)'))
    await user.type(within(form).getByLabelText('Part 2 area (sq ft)'), '40000')
    expect(within(form).getByText(/The parts exceed the parcel by 6,440 sq ft/)).toBeInTheDocument()
    await user.click(within(form).getByRole('button', { name: 'Split parcel' }))
    expect(await within(form).findByText(/add up to more than the parcel/)).toBeInTheDocument()
    expect(fake.calls.some((c) => c.key.endsWith('/split'))).toBe(false)
  })

  it('splits into numbered parts plus a remnant and links to each new parcel', async () => {
    const { fake, user } = open('/parcels/pc1')
    await user.click(await screen.findByRole('button', { name: 'Split' }))
    const form = screen.getByRole('form', { name: 'Split parts' })

    await user.click(within(form).getByRole('button', { name: 'Split parcel' }))
    expect(await within(form).findByText('Each part needs an area greater than zero.')).toBeInTheDocument()

    await user.type(within(form).getByLabelText('Part 1 area (sq ft)'), '10000')
    await user.type(within(form).getByLabelText('Part 2 area (sq ft)'), '20000')
    await user.click(within(form).getByRole('button', { name: 'Split parcel' }))

    const result = await screen.findByRole('region', { name: 'Split result' })
    expect(within(result).getByRole('link', { name: '001-A' })).toBeInTheDocument()
    expect(within(result).getByRole('link', { name: '001-B' })).toBeInTheDocument()
    expect(within(result).getByText(/001-R/)).toBeInTheDocument()
    expect(within(result).getByText('3 new parcels created. The original stays on record.')).toBeInTheDocument()
    expect(fake.parcels.find((p) => p.id === 'pc1')!.state).toBe('Split')
    expect(await screen.findByText(/This parcel was split and is kept on record only/)).toBeInTheDocument()
  })
})

// ── Roles ────────────────────────────────────────────────────────────────

describe.each(ROLES.map((r) => [r]))('what %s can do with parcels', (role) => {
  it('sees the list, and New parcel only if the role may create', async () => {
    open('/parcels', role)
    await listTable()

    expect(!!screen.queryByRole('button', { name: 'New parcel' })).toBe(canCreate(role))
    expect(!!screen.queryByRole('checkbox', { name: 'Select parcel 002' })).toBe(canCreate(role))
  })

  it('sees the parcel, and Edit and Split only if the role may change parcels', async () => {
    open('/parcels/pc1', role)
    await screen.findByRole('heading', { name: '2026-0002 · Parcel 001' })

    expect(!!screen.queryByRole('button', { name: 'Edit parcel' })).toBe(canCreate(role))
    expect(!!screen.queryByRole('button', { name: 'Split' })).toBe(canCreate(role))
  })

  it('can remove an interest only if the role may delete', async () => {
    open('/parcels/pc1', role, { parcels: [{ ...SEED[0], interests: [{ id: 'i1', type: 'Remainder', areaSqFt: 5, notes: null }] }] })
    await userEventModule.setup().click(await screen.findByRole('tab', { name: 'Interests' }))
    await screen.findByRole('row', { name: /Remainder/ })

    expect(!!screen.queryByRole('button', { name: 'Remove Remainder interest' })).toBe(canDelete(role))
  })
})

// ── Project tab ──────────────────────────────────────────────────────────

describe('A project\'s parcels tab (ROW-002, 006, 233)', () => {
  it('rolls up the live parcels by status and group', async () => {
    const { user } = open('/projects/p-loc', 'ProjectManager', { parcels: [{ ...SEED[0], groupId: 'g1' }, SEED[1]] })
    await user.click(await screen.findByRole('tab', { name: 'Parcels' }))

    const rollup = await screen.findByRole('region', { name: 'Parcel roll-up' })
    expect(rollup).toHaveTextContent('1.5 acres')
    const byStatus = within(rollup).getByRole('table', { name: 'By status' })
    expect(within(byStatus).getByRole('row', { name: /Identified/ })).toHaveTextContent('1')
    expect(within(byStatus).getByRole('row', { name: /Negotiation/ })).toHaveTextContent('1')
    const byGroup = within(rollup).getByRole('table', { name: 'By group' })
    expect(within(byGroup).getByRole('row', { name: /Main span/ })).toHaveTextContent('1')
    expect(within(byGroup).getByRole('row', { name: /No group/ })).toHaveTextContent('1')
  })

  it('adds a group and a parent tract with its tax parcel numbers', async () => {
    const { fake, user } = open('/projects/p-loc')
    await user.click(await screen.findByRole('tab', { name: 'Parcels' }))

    const group = await screen.findByRole('form', { name: 'Add a group' })
    await user.click(within(group).getByRole('button', { name: 'Add group' }))
    expect(await screen.findByText('Group name is required.')).toBeInTheDocument()
    await user.type(within(group).getByLabelText('Group name'), 'North segment')
    await user.click(within(group).getByRole('button', { name: 'Add group' }))
    expect(await within(screen.getByRole('region', { name: 'Parcel groups' })).findByRole('row', { name: /North segment/ })).toBeInTheDocument()

    const tract = screen.getByRole('form', { name: 'Add a parent tract' })
    await user.type(within(tract).getByLabelText('Tract number'), 'T-9')
    await user.type(within(tract).getByLabelText('Tax parcel numbers'), 'APN-7, APN-8')
    await user.click(within(tract).getByRole('button', { name: 'Add tract' }))

    expect(await screen.findByText('APN-7, APN-8')).toBeInTheDocument()
    expect(fake.tracts[0].taxParcelNumbers).toEqual(['APN-7', 'APN-8'])
  })

  it('will not delete a group that still has parcels, and deletes an empty one', async () => {
    const { fake, user } = open('/projects/p-loc', 'ProjectManager', {
      parcels: [{ ...SEED[0], groupId: 'g1' }], groups: [{ id: 'g1', projectId: 'p-loc', name: 'Main span' }, { id: 'g2', projectId: 'p-loc', name: 'Spare' }],
    })
    await user.click(await screen.findByRole('tab', { name: 'Parcels' }))

    await user.click(await screen.findByRole('button', { name: 'Delete group Main span' }))
    expect(await screen.findByText("The group 'Main span' still has parcels. Move them to another group first.")).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Delete group Spare' }))
    await waitFor(() => expect(fake.groups.map((g) => g.name)).toEqual(['Main span']))
  })

  it('a read-only role sees groups and tracts but cannot add or delete them', async () => {
    const { user } = open('/projects/p-loc', 'Appraiser')
    await user.click(await screen.findByRole('tab', { name: 'Parcels' }))

    expect(await screen.findByRole('region', { name: 'Parcel roll-up' })).toBeInTheDocument()
    expect(screen.queryByRole('form', { name: 'Add a group' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /Delete group/ })).not.toBeInTheDocument()
  })
})

// ── Configuration ────────────────────────────────────────────────────────

describe('Parcel rules in Configuration (ROW-005, 011, 072, 073)', () => {
  it('renames a status and adds a new one, and refuses a duplicate name', async () => {
    const { fake, user } = open('/administration', 'Admin')
    await user.click(await screen.findByRole('button', { name: 'Edit status Negotiation' }))
    const form = screen.getByRole('form', { name: 'Edit a status' })

    await user.clear(within(form).getByLabelText(/^Status name/))
    await user.type(within(form).getByLabelText(/^Status name/), 'Offer made')
    await user.click(within(form).getByRole('button', { name: 'Save status' }))
    expect(await screen.findByText('Status saved.')).toBeInTheDocument()
    expect(fake.statuses.map((s) => s.name)).toContain('Offer made')

    await user.click(screen.getByRole('button', { name: 'Add status' }))
    const add = screen.getByRole('form', { name: 'Add a status' })
    await user.click(within(add).getByRole('button', { name: 'Save status' }))
    expect(await within(add).findByText('Status name is required.')).toBeInTheDocument()
    await user.type(within(add).getByLabelText(/^Status name/), 'Impasse')
    await user.click(within(add).getByRole('button', { name: 'Save status' }))
    expect(await screen.findByRole('row', { name: /Impasse/ })).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Add status' }))
    await user.type(within(screen.getByRole('form', { name: 'Add a status' })).getByLabelText(/^Status name/), 'closed')
    await user.click(within(screen.getByRole('form', { name: 'Add a status' })).getByRole('button', { name: 'Save status' }))
    expect(await screen.findByText("There is already a status named 'closed'.")).toBeInTheDocument()
  })

  it('shows seeded deadline periods as placeholders until the agency saves them', async () => {
    const { fake, user } = open('/administration', 'Admin')
    const card = await screen.findByRole('region', { name: 'Statutory deadlines' })
    const row = await within(card).findByRole('row', { name: /Offer within 90 days/ })
    expect(within(row).getByText('Placeholder')).toBeInTheDocument()

    await user.click(within(row).getByRole('button', { name: /Edit rule/ }))
    const form = within(card).getByRole('form', { name: 'Edit a deadline rule' })
    await user.clear(within(form).getByLabelText(/^Days allowed/))
    await user.type(within(form).getByLabelText(/^Days allowed/), '60')
    await user.click(within(form).getByRole('button', { name: 'Save rule' }))

    expect(await within(card).findByText('Rule saved.')).toBeInTheDocument()
    await waitFor(() => expect(within(card).queryByText('Placeholder')).not.toBeInTheDocument())
    expect(fake.deadlineRules[0]).toMatchObject({ days: 60, isPlaceholder: false })
  })

  it('validates a deadline rule before sending it', async () => {
    const { user } = open('/administration', 'Admin')
    const card = await screen.findByRole('region', { name: 'Statutory deadlines' })
    await user.click(await within(card).findByRole('button', { name: 'Add deadline rule' }))
    const form = within(card).getByRole('form', { name: 'Add a deadline rule' })

    await user.click(within(form).getByRole('button', { name: 'Save rule' }))

    expect(await within(form).findByText('Rule name is required.')).toBeInTheDocument()
    expect(within(form).getByText('The starting milestone is required.')).toBeInTheDocument()
    expect(within(form).getByText('Days must be a whole number, zero or more.')).toBeInTheDocument()
  })

  it('adds a list custom field, locks its key and type afterwards, and rejects a bad key', async () => {
    const { fake, user } = open('/administration', 'Admin')
    const card = await screen.findByRole('region', { name: 'Parcel custom fields' })
    await user.click(await within(card).findByRole('button', { name: 'Add custom field' }))
    const form = within(card).getByRole('form', { name: 'Add a custom field' })

    await user.type(within(form).getByLabelText(/^Field label/), 'Zoning')
    await user.type(within(form).getByLabelText(/^Field key/), 'Bad Key')
    await user.click(within(form).getByRole('button', { name: 'Save field' }))
    expect(await within(form).findByText(/The key starts with a lower-case letter/)).toBeInTheDocument()

    await user.clear(within(form).getByLabelText(/^Field key/))
    await user.type(within(form).getByLabelText(/^Field key/), 'zoning')
    await user.selectOptions(within(form).getByLabelText('Field type'), 'List')
    await user.click(within(form).getByRole('button', { name: 'Save field' }))
    expect(await within(form).findByText('A list field needs at least one option.')).toBeInTheDocument()
    await user.type(within(form).getByLabelText('List options'), 'Residential, Commercial')
    await user.click(within(form).getByRole('button', { name: 'Save field' }))

    expect(await within(card).findByText('Field saved.')).toBeInTheDocument()
    expect(fake.customFields[0]).toMatchObject({ key: 'zoning', fieldType: 'List', options: ['Residential', 'Commercial'] })

    await user.click(within(card).getByRole('button', { name: 'Edit field Zoning' }))
    const edit = within(card).getByRole('form', { name: 'Edit a custom field' })
    expect(within(edit).getByLabelText(/^Field key/)).toBeDisabled()
    expect(within(edit).getByLabelText('Field type')).toBeDisabled()
  })

  it('lets the administrator choose which extra roles may override step order', async () => {
    const { fake, user } = open('/administration', 'Admin')
    const card = await screen.findByRole('region', { name: 'Step order overrides' })
    expect(await within(card).findByText('ProjectManager, Admin')).toBeInTheDocument()

    await user.click(within(card).getByLabelText('Negotiator'))
    await user.click(within(card).getByRole('button', { name: 'Save override roles' }))

    expect(await within(card).findByText('Override roles saved.')).toBeInTheDocument()
    expect(fake.stepGate.extraOverrideRoles).toEqual(['Negotiator'])
  })

  it('lists the placeholder workflows read-only', async () => {
    open('/administration', 'Admin')

    const card = await screen.findByRole('region', { name: 'Acquisition workflows' })
    expect(await within(card).findByRole('row', { name: /Donation/ })).toHaveTextContent('Parcel set up → Title & survey')
  })
})

// ── Dashboard reminders ──────────────────────────────────────────────────

describe('Next actions on the dashboard (ROW-074)', () => {
  it('lists overdue and due-soon deadlines with how late or soon, linking to the parcel', async () => {
    open('/', 'ProjectManager', {
      parcels: [
        { ...SEED[0], milestones: withNotice('2026-05-01') }, // due Jul 30: overdue
        { ...SEED[1], milestones: withNotice('2026-07-20') }, // due Oct 18: 4 days
        { ...SEED[2], milestones: withNotice('2026-10-01') }, // due Dec 30: not listed
      ],
    })

    const card = await screen.findByRole('region', { name: 'Next actions' })
    const items = await within(card).findAllByRole('listitem')
    expect(items).toHaveLength(2)
    expect(items[0]).toHaveTextContent('2026-0002 · Parcel 001')
    expect(items[0]).toHaveTextContent('Overdue by 76 days')
    expect(within(items[0]).getByRole('link')).toHaveAttribute('href', '/parcels/pc1')
    expect(items[1]).toHaveTextContent('Due in 4 days')
  })

  it('says when nothing is due', async () => {
    open('/', 'ProjectManager')

    expect(await screen.findByText('Nothing is due. You are all caught up.')).toBeInTheDocument()
  })
})
