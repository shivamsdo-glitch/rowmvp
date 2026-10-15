import { beforeEach, describe, expect, it } from 'vitest'
import { fireEvent, screen, waitFor, within } from '@testing-library/react'
import userEventModule from '@testing-library/user-event'
import { ROLES } from '@/api/auth'
import type { Project } from '@/api/projects'
import { levelFor, permissionRowsFor } from '@/test/contract'
import { fakePeopleApi, type FakePeopleOptions } from '@/test/fake-people'
import { renderApp } from '@/test/render-app'

beforeEach(() => sessionStorage.clear())

const canCreate = (role: string) => ['S', 'P'].includes(levelFor(role, 'parties') ?? '')
const canDelete = (role: string) => levelFor(role, 'parties') === 'P'
const canReadParties = (role: string) => levelFor(role, 'parties') !== null

const PROJECTS: Partial<Project>[] = [{ id: 'p1', number: '2026-0001', name: 'Harbor Bridge', oversightType: 'Local' }]

const OWNER = { id: 'party-pat', name: 'Pat Owner', city: 'Springfield', state: 'IL', phone: '555-0100', email: 'pat@example.com' }
const BANK = { id: 'party-bank', name: 'First Bank', kind: 'Organization' as const, city: 'Chicago', state: 'IL' }

function open(path: string, role = 'ProjectManager', extra: Partial<FakePeopleOptions> = {}) {
  const fake = fakePeopleApi({
    role, rows: permissionRowsFor(role), projects: PROJECTS,
    tracts: [{ id: 'tr1', projectId: 'p1', number: 'T-1' }],
    parcels: [
      { id: 'pc-tract', projectId: 'p1', number: '001', address: '1 Oak St', parentTractId: 'tr1' },
      { id: 'pc-loose', projectId: 'p1', number: '002', address: '2 Oak St' },
    ],
    parties: [OWNER, BANK],
    links: [
      { id: 'l-owner', partyId: 'party-pat', tractId: 'tr1', role: 'FeeOwner', sharePercent: 60 },
      { id: 'l-bank', partyId: 'party-bank', tractId: 'tr1', role: 'Lienholder' },
      { id: 'l-attorney', partyId: 'party-pat', parcelId: 'pc-tract', role: 'Attorney', notes: 'Closing counsel' },
    ],
    ...extra,
  })
  const user = userEventModule.setup()
  const app = renderApp({ path, fetchImpl: fake.fetch, signedIn: true })
  return { fake, user, ...app }
}

const partiesTable = () => screen.findByRole('table', { name: 'Parties' })

// ── The parties list ────────────────────────────────────────────────────

describe('Parties list (ROW-014, 018)', () => {
  it('lists parties with where they are used and whether a tax ID is on file, and links to each', async () => {
    open('/parties', 'ProjectManager', { parties: [{ ...OWNER, hasTaxId: true, taxIdType: 'Ssn', taxIdMasked: '***-**-6789' }, BANK] })

    const table = await partiesTable()
    const row = within(table).getByRole('row', { name: /Pat Owner/ })
    expect(within(row).getByRole('link', { name: 'Pat Owner' })).toHaveAttribute('href', '/parties/party-pat')
    expect(within(row).getByText('Springfield, IL')).toBeInTheDocument()
    expect(within(row).getByText('2 records')).toBeInTheDocument()
    expect(within(row).getByText('On file')).toBeInTheDocument()
    expect(within(within(table).getByRole('row', { name: /First Bank/ })).getByText('None')).toBeInTheDocument()
    expect(table.textContent).not.toMatch(/\d{3}-\d{2}-\d{4}/)
  })

  it('searches by name and filters by type', async () => {
    const { fake, user } = open('/parties')
    await partiesTable()

    await user.type(screen.getByLabelText('Search parties'), 'bank')
    await waitFor(() => expect(screen.queryByText('Pat Owner')).not.toBeInTheDocument())
    expect(screen.getByText('First Bank')).toBeInTheDocument()
    expect(fake.calls.some((c) => c.key.includes('search=bank'))).toBe(true)

    await user.clear(screen.getByLabelText('Search parties'))
    await user.selectOptions(screen.getByLabelText('Type'), 'Person')
    await waitFor(() => expect(screen.queryByText('First Bank')).not.toBeInTheDocument())
    expect(await screen.findByText('Pat Owner')).toBeInTheDocument()
  })

  it('says so when nothing matches and when there are no parties at all', async () => {
    const { user, fake } = open('/parties')
    await partiesTable()
    await user.type(screen.getByLabelText('Search parties'), 'zzzz')
    expect(await screen.findByText('No parties match your search')).toBeInTheDocument()
    expect(fake.parties).toHaveLength(2)
  })

  it('shows the empty state for an agency with no parties', async () => {
    open('/parties', 'ProjectManager', { parties: [], links: [] })
    expect(await screen.findByText('No parties yet')).toBeInTheDocument()
  })

  it('shows a retryable error when the list cannot be loaded', async () => {
    open('/parties', 'ProjectManager', { listFails: true })
    expect(await screen.findByText('Parties could not be loaded')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Try again' })).toBeInTheDocument()
  })

  it.each(ROLES.map((r) => [r]))('%s sees New party only when the parties page lets them create (both directions)', async (role) => {
    open('/parties', role)
    await partiesTable()
    const button = screen.queryByRole('button', { name: 'New party' })
    if (canCreate(role)) expect(button).toBeInTheDocument()
    else expect(button).not.toBeInTheDocument()
  })

  it('creates a party and opens it', async () => {
    const { fake, user, router } = open('/parties')
    await partiesTable()

    await user.click(screen.getByRole('button', { name: 'New party' }))
    const form = screen.getByRole('form', { name: 'Create a party' })
    await user.type(within(form).getByLabelText(/^Name/), 'Sam Seller')
    await user.type(within(form).getByLabelText(/^State/), 'il')
    await user.click(within(form).getByRole('button', { name: 'Create party' }))

    await waitFor(() => expect(router.state.location.pathname).toMatch(/^\/parties\/party/))
    expect(fake.parties.find((p) => p.name === 'Sam Seller')?.state).toBe('IL')
    expect(await screen.findByRole('heading', { name: 'Sam Seller' })).toBeInTheDocument()
  })

  it('refuses a missing name and shows the server\'s complaint about a bad email beside the field', async () => {
    const { user, fake } = open('/parties')
    await partiesTable()
    await user.click(screen.getByRole('button', { name: 'New party' }))
    const form = screen.getByRole('form', { name: 'Create a party' })

    await user.click(within(form).getByRole('button', { name: 'Create party' }))
    expect(await within(form).findByText('Name is required.')).toBeInTheDocument()
    expect(fake.calls.some((c) => c.key === 'POST /api/v1/parties')).toBe(false)

    await user.type(within(form).getByLabelText(/^Name/), 'Sam')
    await user.type(within(form).getByLabelText(/^Email/), 'nope')
    await user.click(within(form).getByRole('button', { name: 'Create party' }))
    expect(await within(form).findByText('Enter a valid email address.')).toBeInTheDocument()
    expect(within(form).getByLabelText(/^Email/)).toHaveAttribute('aria-invalid', 'true')
  })
})

// ── One party, and its tax information ───────────────────────────────────

describe('Party detail and W-9 tax information (ROW-018)', () => {
  const taxForm = () => screen.findByRole('form', { name: 'Tax information' })

  it('shows the masked number only and never offers the stored one', async () => {
    open('/parties/party-pat', 'ProjectManager', { parties: [{ ...OWNER, hasTaxId: true, taxIdType: 'Ssn', taxIdMasked: '***-**-6789', w9ReceivedOn: '2026-01-15', reportable1099: true }] })

    expect(await screen.findByTestId('tax-id-masked')).toHaveTextContent('***-**-6789')
    expect(screen.getByText('Jan 15, 2026')).toBeInTheDocument()
    const form = await taxForm()
    const number = within(form).getByLabelText(/Replace tax ID number/)
    expect(number).toHaveAttribute('type', 'password')
    expect(number).toHaveValue('')
    expect(number).toHaveAttribute('autocomplete', 'off')
  })

  it('saves a number: it is sent once, the field empties, and only the mask is shown afterwards', async () => {
    const { fake, user } = open('/parties/party-pat')
    const form = await taxForm()

    await user.selectOptions(within(form).getByLabelText('Type of tax ID'), 'Ssn')
    await user.type(within(form).getByLabelText(/^Tax ID number/), '123-45-6789')
    await user.click(within(form).getByLabelText('Reportable on a 1099'))
    await user.click(within(form).getByRole('button', { name: 'Save tax information' }))

    expect(await screen.findByTestId('tax-id-masked')).toHaveTextContent('***-**-6789')
    expect(await screen.findByText('Tax information saved.')).toBeInTheDocument()
    expect(fake.receivedTaxIds).toEqual([{ partyId: 'party-pat', taxId: '123-45-6789' }])
    expect(within(await taxForm()).getByLabelText(/Replace tax ID number/)).toHaveValue('')
    expect(document.body.textContent).not.toContain('123-45-6789')
    expect(document.body.textContent).not.toContain('123456789')
    expect((document.body.innerHTML.match(/6789/g) ?? []).length).toBeGreaterThan(0)
    expect(document.body.innerHTML).not.toContain('123-45')
  })

  it('refuses a bad number with the server\'s reason, never repeats it, and keeps the form usable', async () => {
    const { fake, user } = open('/parties/party-pat')
    const form = await taxForm()

    await user.type(within(form).getByLabelText(/^Tax ID number/), '666-12-3456')
    await user.click(within(form).getByRole('button', { name: 'Save tax information' }))

    expect(await within(form).findByText('That is not a valid Social Security number.')).toBeInTheDocument()
    expect(document.body.textContent).not.toContain('666-12-3456')
    expect(fake.parties.find((p) => p.id === 'party-pat')?.hasTaxId).toBe(false)
    expect(within(form).getByRole('button', { name: 'Save tax information' })).toBeEnabled()
  })

  it('keeps the number on file when only the W-9 date changes, and removes it on request', async () => {
    const { fake, user } = open('/parties/party-pat', 'ProjectManager', { parties: [{ ...OWNER, hasTaxId: true, taxIdType: 'Ein', taxIdMasked: '**-***4321' }] })
    const form = await taxForm()

    fireEvent.change(within(form).getByLabelText('W-9 received on'), { target: { value: '2026-02-01' } })
    await user.click(within(form).getByRole('button', { name: 'Save tax information' }))
    await waitFor(() => expect(fake.parties[0].w9ReceivedOn).toBe('2026-02-01'))
    expect(screen.getByTestId('tax-id-masked')).toHaveTextContent('**-***4321')
    expect(fake.receivedTaxIds).toEqual([])

    await user.click(within(await taxForm()).getByRole('button', { name: 'Remove tax ID number' }))
    await waitFor(() => expect(screen.queryByTestId('tax-id-masked')).not.toBeInTheDocument())
    expect(fake.parties[0].hasTaxId).toBe(false)
  })

  it.each(ROLES.map((r) => [r]))('%s: tax information is editable only with update rights, and the page is open to read for %s', async (role) => {
    open('/parties/party-pat', role)
    await screen.findByRole('heading', { name: 'Pat Owner' })
    const canEdit = ['S', 'P'].includes(levelFor(role, 'parties') ?? '')
    if (canEdit) expect(screen.getByRole('form', { name: 'Tax information' })).toBeInTheDocument()
    else {
      expect(screen.queryByRole('form', { name: 'Tax information' })).not.toBeInTheDocument()
      expect(screen.getByText(/can see that a tax ID is on file but not change it/)).toBeInTheDocument()
    }
    expect(screen.queryByRole('button', { name: 'Edit details' }) !== null).toBe(canEdit)
  })

  it('edits the details', async () => {
    const { fake, user } = open('/parties/party-pat')
    await user.click(await screen.findByRole('button', { name: 'Edit details' }))
    const form = screen.getByRole('form', { name: 'Edit party details' })

    await user.clear(within(form).getByLabelText(/^Name/))
    await user.type(within(form).getByLabelText(/^Name/), 'Patricia Owner')
    await user.click(within(form).getByRole('button', { name: 'Save details' }))

    expect(await screen.findByRole('heading', { name: 'Patricia Owner' })).toBeInTheDocument()
    expect(fake.parties[0].name).toBe('Patricia Owner')
  })

  it('lists where the party appears, linking to parcels', async () => {
    open('/parties/party-pat')
    const table = await screen.findByRole('table', { name: 'Where this party appears' })

    expect(within(table).getByRole('link', { name: 'Parcel 001' })).toHaveAttribute('href', '/parcels/pc-tract')
    expect(within(table).getByText('Tract T-1')).toBeInTheDocument()
    expect(within(table).getByText('Fee owner')).toBeInTheDocument()
    expect(within(table).getByText('Attorney')).toBeInTheDocument()
  })

  it('will not delete a party that is in use, and deletes one that is not', async () => {
    const { fake, user, router } = open('/parties/party-pat')
    expect(await screen.findByText(/linked to 2 records/)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Delete party' })).toBeDisabled()

    const free = open('/parties/party-free', 'ProjectManager', { parties: [{ id: 'party-free', name: 'Free Agent' }], links: [] })
    expect(fake.parties).toHaveLength(2)
    expect(await screen.findAllByText(/Nothing uses this party/)).not.toHaveLength(0)
    await user.click(screen.getAllByRole('button', { name: 'Delete party' }).find((b) => !(b as HTMLButtonElement).disabled)!)
    await waitFor(() => expect(free.fake.parties).toHaveLength(0))
    expect(router.state.location.pathname).toBeDefined()
  })

  it.each(ROLES.map((r) => [r]))('%s: the delete section shows only for roles that may delete', async (role) => {
    open('/parties/party-pat', role)
    await screen.findByRole('heading', { name: 'Pat Owner' })
    expect(screen.queryByRole('region', { name: 'Delete this party' }) !== null).toBe(canDelete(role))
  })

  it('says when a party cannot be found', async () => {
    open('/parties/nobody')
    expect(await screen.findByText('This party could not be opened')).toBeInTheDocument()
  })
})

// ── The Parties tab of a parcel ──────────────────────────────────────────

describe('Parcel parties tab (ROW-014..017)', () => {
  const openTab = async (parcel: string, role = 'ProjectManager', extra: Partial<FakePeopleOptions> = {}) => {
    const ctx = open(`/parcels/${parcel}`, role, extra)
    await ctx.user.click(await screen.findByRole('tab', { name: 'Parties' }))
    return ctx
  }

  it('shows the four lists, with the tract\'s owners marked as coming from the tract and not editable', async () => {
    await openTab('pc-tract')

    expect(await screen.findByRole('note')).toHaveTextContent('part of tract T-1')
    const owners = screen.getByRole('region', { name: 'Owners and interests' })
    const feeRow = within(owners).getByRole('row', { name: /Pat Owner/ })
    expect(within(feeRow).getByText('60%')).toBeInTheDocument()
    expect(within(feeRow).getByText('Tract T-1')).toBeInTheDocument()
    expect(within(feeRow).queryByRole('button')).not.toBeInTheDocument()
    expect(within(owners).getByRole('row', { name: /First Bank/ })).toBeInTheDocument()

    const contacts = screen.getByRole('region', { name: 'Contacts' })
    const row = within(contacts).getByRole('row', { name: /Pat Owner/ })
    expect(within(row).getByText('This parcel')).toBeInTheDocument()
    expect(within(row).getByText('Closing counsel')).toBeInTheDocument()
    expect(within(row).getByRole('button', { name: 'Remove Pat Owner, Attorney' })).toBeInTheDocument()
    expect(screen.getByText('No tenants or occupants recorded.')).toBeInTheDocument()
    expect(screen.getByText('No abutters or interested parties recorded.')).toBeInTheDocument()
  })

  it('does not offer owner roles on a parcel that belongs to a tract', async () => {
    const { user } = await openTab('pc-tract')
    await user.click(await screen.findByRole('button', { name: 'Add a person or organization' }))
    const form = screen.getByRole('form', { name: 'Add a person or organization' })

    const roles = within(within(form).getByLabelText(/^Role(?! notes)/)).getAllByRole('option').map((o) => o.textContent)
    expect(roles).not.toContain('Fee owner')
    expect(roles).toEqual(expect.arrayContaining(['Attorney', 'Tenant', 'Abutter']))
    expect(within(form).getByText(/added on the tract/)).toBeInTheDocument()
  })

  it('adds a new tenant with lease and insurance details to a parcel with no tract', async () => {
    const { user, fake } = await openTab('pc-loose')
    await user.click(await screen.findByRole('button', { name: 'Add a person or organization' }))
    const form = screen.getByRole('form', { name: 'Add a person or organization' })

    await user.click(within(form).getByLabelText('A new person or organization'))
    await user.type(within(form).getByLabelText(/^New party Name/), 'Tina Tenant')
    await user.selectOptions(within(form).getByLabelText(/^Role(?! notes)/), 'Tenant')
    await user.click(within(form).getByLabelText('Lease on file'))
    await user.type(within(form).getByLabelText('Lease details'), 'Month to month')
    await user.type(within(form).getByLabelText('Insurance details'), 'Renter policy 55')
    await user.click(within(form).getByRole('button', { name: 'Add' }))

    const occupants = await screen.findByRole('region', { name: 'Tenants and occupants' })
    const row = await within(occupants).findByRole('row', { name: /Tina Tenant/ })
    expect(within(row).getByText(/Lease on file: Month to month/)).toBeInTheDocument()
    expect(within(row).getByText(/Insurance: Renter policy 55/)).toBeInTheDocument()
    expect(fake.parties.some((p) => p.name === 'Tina Tenant')).toBe(true)
    expect(screen.queryByRole('form', { name: 'Add a person or organization' })).not.toBeInTheDocument()
  })

  it('adds someone already in the system, found by search, and refuses the same person in the same role twice', async () => {
    const { user } = await openTab('pc-loose')
    await user.click(await screen.findByRole('button', { name: 'Add a person or organization' }))
    const form = screen.getByRole('form', { name: 'Add a person or organization' })

    await user.type(within(form).getByLabelText('Find a party'), 'bank')
    await waitFor(() => expect(within(within(form).getByLabelText(/^Party/)).getAllByRole('option')).toHaveLength(2))
    await user.selectOptions(within(form).getByLabelText(/^Party/), 'party-bank')
    await user.selectOptions(within(form).getByLabelText(/^Role(?! notes)/), 'Bank')
    await user.click(within(form).getByRole('button', { name: 'Add' }))
    expect(await within(await screen.findByRole('region', { name: 'Contacts' })).findByRole('row', { name: /First Bank/ })).toBeInTheDocument()

    await user.click(await screen.findByRole('button', { name: 'Add a person or organization' }))
    const again = screen.getByRole('form', { name: 'Add a person or organization' })
    await user.type(within(again).getByLabelText('Find a party'), 'bank')
    await waitFor(() => expect(within(within(again).getByLabelText(/^Party/)).getAllByRole('option')).toHaveLength(2))
    await user.selectOptions(within(again).getByLabelText(/^Party/), 'party-bank')
    await user.selectOptions(within(again).getByLabelText(/^Role(?! notes)/), 'Bank')
    await user.click(within(again).getByRole('button', { name: 'Add' }))
    expect(await within(again).findByText(/already recorded here as bank or lender/i)).toBeInTheDocument()
  })

  it('asks for a party and shows the server\'s per-field complaints for a new one', async () => {
    const { user } = await openTab('pc-loose')
    await user.click(await screen.findByRole('button', { name: 'Add a person or organization' }))
    const form = screen.getByRole('form', { name: 'Add a person or organization' })

    await user.click(within(form).getByRole('button', { name: 'Add' }))
    expect(await within(form).findByText('Choose a person or organization, or enter a new one.')).toBeInTheDocument()

    await user.click(within(form).getByLabelText('A new person or organization'))
    await user.type(within(form).getByLabelText(/^New party Name/), 'Bad Email')
    await user.type(within(form).getByLabelText(/^New party Email/), 'nope')
    await user.click(within(form).getByRole('button', { name: 'Add' }))
    expect(await within(form).findByText('Enter a valid email address.')).toBeInTheDocument()
  })

  it('removes a link the parcel owns and keeps the party', async () => {
    const { user, fake } = await openTab('pc-tract')
    await user.click(await screen.findByRole('button', { name: 'Remove Pat Owner, Attorney' }))

    await waitFor(() => expect(screen.queryByRole('button', { name: 'Remove Pat Owner, Attorney' })).not.toBeInTheDocument())
    expect(fake.links.some((l) => l.id === 'l-attorney')).toBe(false)
    expect(fake.parties.some((p) => p.id === 'party-pat')).toBe(true)
  })

  it('edits a link\'s notes', async () => {
    const { user, fake } = await openTab('pc-tract')
    await user.click(await screen.findByRole('button', { name: 'Edit Pat Owner, Attorney' }))
    const form = screen.getByRole('form', { name: 'Edit Pat Owner, Attorney' })

    await user.clear(within(form).getByLabelText('Notes'))
    await user.type(within(form).getByLabelText('Notes'), 'Retained for the taking')
    await user.click(within(form).getByRole('button', { name: 'Save changes' }))

    expect(await screen.findByText('Retained for the taking')).toBeInTheDocument()
    expect(fake.links.find((l) => l.id === 'l-attorney')?.notes).toBe('Retained for the taking')
  })

  it.each(ROLES.map((r) => [r]))('%s: add, edit and remove controls follow the parties page (both directions)', async (role) => {
    open('/parcels/pc-tract', role)
    await screen.findByRole('heading', { name: /Parcel 001/ })
    if (!canReadParties(role)) return
    await userEventModule.setup().click(await screen.findByRole('tab', { name: 'Parties' }))
    await screen.findByRole('region', { name: 'Contacts' })

    expect(screen.queryByRole('button', { name: 'Add a person or organization' }) !== null).toBe(canCreate(role))
    expect(screen.queryByRole('button', { name: 'Remove Pat Owner, Attorney' }) !== null).toBe(canDelete(role))
    expect(screen.queryByRole('button', { name: 'Edit Pat Owner, Attorney' }) !== null).toBe(canCreate(role))
  })

  it('cannot change a parcel that was split or combined', async () => {
    const { user } = open('/parcels/pc-gone', 'ProjectManager', {
      parcels: [{ id: 'pc-gone', projectId: 'p1', number: '009', state: 'Split' }],
    })
    await user.click(await screen.findByRole('tab', { name: 'Parties' }))
    await screen.findByRole('region', { name: 'Contacts' })
    expect(screen.queryByRole('button', { name: 'Add a person or organization' })).not.toBeInTheDocument()
  })
})

// ── Owners on a project's tract ──────────────────────────────────────────

describe('Tract owners and shares (ROW-014)', () => {
  const openTract = async (extra: Partial<FakePeopleOptions> = {}) => {
    const ctx = open('/projects/p1', 'ProjectManager', extra)
    await ctx.user.click(await screen.findByRole('tab', { name: 'Parcels' }))
    await ctx.user.click(await screen.findByRole('button', { name: 'Owners and parties of tract T-1' }))
    return ctx
  }

  it('opens a tract\'s owners from the project\'s Parcels tab', async () => {
    await openTract()

    const owners = await screen.findByRole('region', { name: 'Owners and interests' })
    expect(within(owners).getByRole('row', { name: /Pat Owner/ })).toBeInTheDocument()
    expect(within(owners).getByRole('button', { name: 'Edit Pat Owner, Fee owner' })).toBeInTheDocument()
  })

  it('adds an owner with a share, and refuses shares that pass 100 percent with the unclaimed amount', async () => {
    const { user, fake } = await openTract()
    await user.click(await screen.findByRole('button', { name: 'Add a person or organization' }))
    const form = screen.getByRole('form', { name: 'Add a person or organization' })

    await user.click(within(form).getByLabelText('A new person or organization'))
    await user.type(within(form).getByLabelText(/^New party Name/), 'Sam Co-owner')
    await user.type(within(form).getByLabelText('Share (%)'), '50')
    await user.click(within(form).getByRole('button', { name: 'Add' }))
    expect(await within(form).findByText(/add up to more than 100 percent\. 40 percent is unclaimed/)).toBeInTheDocument()
    expect(fake.parties.some((p) => p.name === 'Sam Co-owner')).toBe(false)

    await user.clear(within(form).getByLabelText('Share (%)'))
    await user.type(within(form).getByLabelText('Share (%)'), '40')
    await user.click(within(form).getByRole('button', { name: 'Add' }))
    const owners = await screen.findByRole('region', { name: 'Owners and interests' })
    expect(await within(owners).findByRole('row', { name: /Sam Co-owner/ })).toBeInTheDocument()
    expect(within(within(owners).getByRole('row', { name: /Sam Co-owner/ })).getByText('40%')).toBeInTheDocument()
  })

  it('changes a share within the whole and refuses one beyond it', async () => {
    const { user } = await openTract()
    await user.click(await screen.findByRole('button', { name: 'Edit Pat Owner, Fee owner' }))
    const form = screen.getByRole('form', { name: 'Edit Pat Owner, Fee owner' })

    await user.clear(within(form).getByLabelText('Share (%)'))
    await user.type(within(form).getByLabelText('Share (%)'), '101')
    await user.click(within(form).getByRole('button', { name: 'Save changes' }))
    expect(await within(form).findByText(/more than 0 and at most 100|more than 100 percent/)).toBeInTheDocument()

    await user.clear(within(form).getByLabelText('Share (%)'))
    await user.type(within(form).getByLabelText('Share (%)'), '75')
    await user.click(within(form).getByRole('button', { name: 'Save changes' }))
    expect(await screen.findByText('75%')).toBeInTheDocument()
  })

  it('does not show the owners button to a role that cannot see parties', async () => {
    open('/projects/p1', 'ProjectManager', { rows: permissionRowsFor('ProjectManager').map((r) => (r.pageKey === 'parties' ? { ...r, canRead: false } : r)) })
    await userEventModule.setup().click(await screen.findByRole('tab', { name: 'Parcels' }))
    await screen.findByRole('table', { name: 'Parent tracts' })
    expect(screen.queryByRole('button', { name: 'Owners and parties of tract T-1' })).not.toBeInTheDocument()
  })
})

// ── Navigation ───────────────────────────────────────────────────────────

describe('Navigation to parties', () => {
  it.each(ROLES.map((r) => [r]))('%s sees the Parties link exactly when the page is readable', async (role) => {
    open('/', role)
    await screen.findByRole('heading', { name: 'Dashboard' })
    const link = screen.queryByRole('link', { name: /Parties/ })
    if (canReadParties(role)) expect(link).toHaveAttribute('href', '/parties')
    else expect(link).not.toBeInTheDocument()
  })

  it('sends someone without access to the no-access page', async () => {
    open('/parties', 'ProjectManager', { rows: permissionRowsFor('ProjectManager').map((r) => (r.pageKey === 'parties' ? { ...r, canRead: false } : r)) })
    expect(await screen.findByText(/don't have access/i)).toBeInTheDocument()
  })
})
