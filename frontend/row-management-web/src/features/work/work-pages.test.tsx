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

function open(path: string, role = 'Appraiser', extra: Partial<FakeTabsOptions> = {}) {
  const fake = fakeTabsApi({
    role, rows: permissionRowsFor(role), projects: PROJECTS,
    parcels: [{ id: 'pc1', projectId: 'p1', number: 'PN-1001', address: '1 Oak St' }, { id: 'pc2', projectId: 'p1', number: 'PN-2002', address: '9 Elm Rd' }],
    ownerships: [{ parcelId: 'pc1', ownerName: 'Kamali Trust' }, { parcelId: 'pc2', ownerName: 'Brightwater LLC' }],
    appraisals: [{ parcelId: 'pc1', id: 'ap1', number: 'APR-PN-1001-001', appraisalType: 'Full', appraiser: 'Ann', appraisalTotal: 1500 }],
    ...extra,
  })
  const user = userEventModule.setup()
  const app = renderApp({ path, fetchImpl: fake.fetch, signedIn: true })
  return { fake, user, ...app }
}

describe('Appraisal work screen (AT-AP-41)', () => {
  it('lists recent appraisals, finds one by the owner name and opens its parcel to update it', async () => {
    const { fake, user } = open('/appraisals')
    expect(await screen.findByRole('heading', { name: 'Appraisal' })).toBeInTheDocument()
    const found = await screen.findByRole('table', { name: 'Appraisals found' })
    expect(found).toHaveTextContent('APR-PN-1001-001')
    expect(found).toHaveTextContent('Kamali Trust')

    await user.type(screen.getByLabelText('Search'), 'brightwater')
    expect(await screen.findByText('No records found')).toBeInTheDocument()
    await user.clear(screen.getByLabelText('Search'))
    await user.type(screen.getByLabelText('Search'), 'kamali')
    await waitFor(() => expect(screen.getByRole('table', { name: 'Appraisals found' })).toHaveTextContent('APR-PN-1001-001'))

    await user.click(screen.getByRole('button', { name: 'Open APR-PN-1001-001' }))
    const panel = await screen.findByRole('region', { name: 'Selected parcel' })
    expect(panel).toHaveTextContent('Parcel PN-1001')
    expect(panel).toHaveTextContent('Kamali Trust')
    await user.click(await within(panel).findByRole('button', { name: 'Edit appraisal APR-PN-1001-001' }))
    const form = await within(panel).findByRole('form', { name: 'Change appraisal' })
    fireEvent.change(within(form).getByLabelText('Appraisal due date'), { target: { value: '2026-09-01' } })
    await user.click(within(form).getByRole('button', { name: 'Save changes' }))
    await waitFor(() => expect(fake.appraisals[0].dueDate).toBe('2026-09-01'))
  })

  it('starts a new appraisal for a parcel that has none, found by its parcel number', async () => {
    const { fake, user } = open('/appraisals')
    await screen.findByRole('table', { name: 'Appraisals found' })

    await user.type(screen.getByLabelText('Search'), 'PN-2002')
    await user.click(await screen.findByRole('button', { name: 'Open parcel PN-2002' }))
    const panel = await screen.findByRole('region', { name: 'Selected parcel' })
    expect(panel).toHaveTextContent('Brightwater LLC')
    const form = await within(panel).findByRole('form', { name: 'Add an appraisal' })
    await user.click(within(form).getByRole('button', { name: 'Add appraisal' }))

    await waitFor(() => expect(fake.appraisals).toHaveLength(2))
    expect(fake.appraisals[1]).toMatchObject({ parcelId: 'pc2', number: 'APR-PN-2002-001' })
    await user.click(within(panel).getByRole('button', { name: 'Close parcel' }))
    expect(screen.queryByRole('region', { name: 'Selected parcel' })).toBeNull()
  })

  it('finds a parcel by its owner name even when it has no records yet', async () => {
    const { user } = open('/appraisals', 'Appraiser', { appraisals: [] })
    await user.type(await screen.findByLabelText('Search'), 'brightwater')
    expect(await screen.findByRole('button', { name: 'Open parcel PN-2002' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Open parcel PN-1001' })).toBeNull()
  })
})

describe('Negotiation & Closing work screen (AT-NE-44)', () => {
  it('switches between negotiations and title and closing records and edits the one it opens', async () => {
    const { fake, user } = open('/negotiations', 'Negotiator', {
      negotiations: [{ parcelId: 'pc1', id: 'ng1', number: 'NEG-PN-1001', assignedTo: 'Casey Closer', totalAgreementAmount: 5000 }],
      titleClosings: [{ parcelId: 'pc2', id: 'tc1', number: 'TC-PN-2002', titleCompany: 'Lone Star Title' }],
    })
    expect(await screen.findByRole('heading', { name: 'Negotiation & Closing' })).toBeInTheDocument()
    expect(await screen.findByRole('table', { name: 'Negotiations found' })).toHaveTextContent('Casey Closer')

    await user.click(screen.getByRole('button', { name: 'Open NEG-PN-1001' }))
    const form = await screen.findByRole('form', { name: 'Negotiation' })
    await user.type(within(form).getByLabelText('Notes'), 'Called the owner.')
    await user.click(within(form).getByRole('button', { name: 'Save changes' }))
    await waitFor(() => expect(fake.negotiations[0].notes).toBe('Called the owner.'))

    await user.click(screen.getByRole('tab', { name: 'Title and closing' }))
    expect(await screen.findByRole('table', { name: 'Title and closing found' })).toHaveTextContent('Lone Star Title')
    await user.click(screen.getByRole('button', { name: 'Open TC-PN-2002' }))
    expect(await screen.findByRole('form', { name: 'Title and closing' })).toBeInTheDocument()
    expect(screen.getByRole('region', { name: 'Selected parcel' })).toHaveTextContent('Parcel PN-2002')
  })
})

describe('Title & Survey work screen (AT-TE-42)', () => {
  it('finds encumbrances and easements and lets the title researcher add to them', async () => {
    const { fake, user } = open('/title', 'TitleResearcher', {
      encumbrances: [{ parcelId: 'pc1', id: 'en1', number: 'ENC-PN-1001-001', documentTitle: 'Deed of Trust' }],
      easements: [{ parcelId: 'pc2', number: 'E-9', easementType: 'Drainage Easement' }],
    })
    expect(await screen.findByRole('heading', { name: 'Title & Survey' })).toBeInTheDocument()
    expect(await screen.findByRole('table', { name: 'Title encumbrances found' })).toHaveTextContent('Deed of Trust')

    await user.click(screen.getByRole('tab', { name: 'Easements' }))
    expect(await screen.findByRole('table', { name: 'Easements found' })).toHaveTextContent('Brightwater LLC')
    await user.click(screen.getByRole('button', { name: 'Open E-9' }))
    const panel = await screen.findByRole('region', { name: 'Selected parcel' })
    expect(await within(panel).findByRole('table', { name: 'Easements' })).toHaveTextContent('E-9')
    expect(fake.easements).toHaveLength(1)
  })
})

describe.each(ROLES.map((r) => [r]))('what %s sees on the work screens (AT-X-57)', (role) => {
  it('offers the Appraisal screen with the add form only to roles that may create appraisals', async () => {
    if (levelFor(role, 'appraisals') === null) return
    const { user } = open('/appraisals', role, { appraisals: [] })
    await screen.findByRole('heading', { name: 'Appraisal' })
    await user.type(screen.getByLabelText('Search'), 'PN-1001')
    await user.click(await screen.findByRole('button', { name: 'Open parcel PN-1001' }))
    const panel = await screen.findByRole('region', { name: 'Selected parcel' })
    await within(panel).findByRole('group', { name: 'Appraisals' })

    expect(!!within(panel).queryByRole('form', { name: 'Add an appraisal' })).toBe(can(role, 'parcelappraisal', 'create'))
  })

  it('offers the Negotiation & Closing screen with the save button following the negotiation page', async () => {
    if (levelFor(role, 'negotiations') === null) return
    const { user } = open('/negotiations', role, { appraisals: [] })
    await screen.findByRole('heading', { name: 'Negotiation & Closing' })
    await user.type(screen.getByLabelText('Search'), 'PN-1001')
    await user.click(await screen.findByRole('button', { name: 'Open parcel PN-1001' }))
    const panel = await screen.findByRole('region', { name: 'Selected parcel' })
    await within(panel).findByRole('form', { name: 'Negotiation' })

    expect(!!within(panel).queryByRole('button', { name: 'Save negotiation' })).toBe(can(role, 'parcelnegotiation', 'create'))
  })
})
