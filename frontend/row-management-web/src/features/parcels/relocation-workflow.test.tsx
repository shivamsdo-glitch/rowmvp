import { beforeEach, describe, expect, it } from 'vitest'
import { screen, waitFor, within } from '@testing-library/react'
import userEventModule from '@testing-library/user-event'
import type { RelocationPayment } from '@/api/parcel-tabs'
import type { Project } from '@/api/projects'
import { permissionRowsFor } from '@/test/contract'
import { fakeTabsApi, type FakeTabsOptions } from '@/test/fake-tabs'
import { renderApp } from '@/test/render-app'

beforeEach(() => sessionStorage.clear())

const PROJECTS: Partial<Project>[] = [{ id: 'p1', number: 'ALR-PPhase 1-029', name: 'Harbor Bridge', oversightType: 'Local' }]

const payment = (over: Partial<RelocationPayment> = {}): RelocationPayment => ({
  id: 'pay1', paymentType: 'Moving expense', amount: 1500, isAdvance: false, taxable: false, notes: null, status: 'Requested', requiredRoles: ['ProjectManager'], approvals: [],
  nextApprover: 'ProjectManager', decisionNote: null, paidAt: null, paymentReference: null, requestedAt: '2026-10-01T00:00:00Z', requestedByMe: false, ...over,
})

function open(path: string, role: string, extra: Partial<FakeTabsOptions> = {}) {
  const fake = fakeTabsApi({
    role, rows: permissionRowsFor(role), projects: PROJECTS,
    parcels: [{ id: 'pc1', projectId: 'p1', number: 'PN-1001', address: '1 Oak St' }, { id: 'pc2', projectId: 'p1', number: 'PN-2002', address: '9 Elm Rd' }],
    parties: [{ id: 'pt1', name: 'Kamali Trust' }, { id: 'pt2', name: 'Brightwater LLC' }],
    links: [{ partyId: 'pt1', parcelId: 'pc1', role: 'FeeOwner', displacementRequired: true }, { partyId: 'pt2', parcelId: 'pc2', role: 'FeeOwner' }],
    ...extra,
  })
  const user = userEventModule.setup()
  const app = renderApp({ path, fetchImpl: fake.fetch, signedIn: true })
  return { fake, user, ...app }
}

const tab = async (user: ReturnType<typeof userEventModule.setup>, name: string) => user.click(await screen.findByRole('tab', { name }))

describe('AT-RA-42 a relocation is for one ownership interest, and the parcel shows how far they have got', () => {
  it('will not add a relocation until an ownership interest is chosen, then fills the displacee name from it', async () => {
    const { fake, user } = open('/parcels/pc1', 'RelocationAgent')
    await tab(user, 'Relocation')
    const form = await screen.findByRole('form', { name: 'Add a relocation' })

    await user.type(within(form).getByLabelText(/^Relocation number/), 'R-9')
    await user.selectOptions(within(form).getByLabelText(/^Relocation type/), 'Residential')
    await user.click(within(form).getByRole('button', { name: 'Add relocation' }))
    expect(await within(form).findByText('Choose the ownership interest this relocation is for.')).toBeInTheDocument()
    expect(fake.relocations).toHaveLength(0)

    const interest = within(form).getByLabelText(/^Ownership interest/)
    expect(within(interest).getByRole('option', { name: /Kamali Trust \(Fee owner, displacement required\)/ })).toBeInTheDocument()
    expect(within(interest).queryByRole('option', { name: /Brightwater/ })).toBeNull()
    await user.selectOptions(interest, within(interest).getByRole('option', { name: /Kamali Trust/ }))
    expect(within(form).getByLabelText(/^Displacee name/)).toHaveValue('Kamali Trust')
    await user.click(within(form).getByRole('button', { name: 'Add relocation' }))

    await waitFor(() => expect(fake.relocations).toHaveLength(1))
    expect(fake.relocations[0].details).toMatchObject({ relocationNumber: 'R-9', displaceeName: 'Kamali Trust', partyLinkId: expect.any(String) })
  })

  it('shows how many displaced interests have been relocated and that the parcel moves to Acquired by itself', async () => {
    const { user } = open('/parcels/pc1', 'RelocationAgent', { progress: { displacedInterests: 3, completedInterests: 2 } })
    await tab(user, 'Relocation')
    const banner = await screen.findByRole('status', { name: 'Relocation progress' })
    expect(banner).toHaveTextContent('2 of 3 displaced interests relocated')
    expect(banner).toHaveTextContent('moves to Acquired automatically')
    expect(banner).not.toHaveTextContent('no active')
  })

  it('warns when the agency has no Acquired status, and shows nothing when no interest is displaced', async () => {
    const warned = open('/parcels/pc1', 'RelocationAgent', { progress: { displacedInterests: 1, completedInterests: 0, acquiredStatusAvailable: false } })
    await tab(warned.user, 'Relocation')
    expect(await screen.findByRole('status', { name: 'Relocation progress' })).toHaveTextContent(/no active .Acquired. parcel status/)
  })

  it('shows no progress banner for a parcel with no displaced interests', async () => {
    const { user } = open('/parcels/pc1', 'RelocationAgent')
    await tab(user, 'Relocation')
    await screen.findByRole('form', { name: 'Add a relocation' })
    expect(screen.queryByRole('status', { name: 'Relocation progress' })).toBeNull()
  })

  it('marks an ownership interest as displacement required from the Ownership interests tab', async () => {
    const { fake, user } = open('/parcels/pc2', 'Admin')
    await tab(user, 'Ownership interests')
    await user.click(await screen.findByRole('button', { name: 'Edit Brightwater LLC, Fee owner' }))
    const form = await screen.findByRole('form', { name: 'Edit Brightwater LLC, Fee owner' })
    await user.click(within(form).getByRole('checkbox', { name: 'Displacement required' }))
    await user.click(within(form).getByRole('button', { name: 'Save changes' }))
    await waitFor(() => expect(fake.links.find((l) => l.parcelId === 'pc2')?.displacementRequired).toBe(true))
    expect(await screen.findByText('Displacement required')).toBeInTheDocument()
  })
})

describe('AT-RA-43 a relocation payment goes through the approval chain', () => {
  const seed = (p: RelocationPayment[]) => ({ relocations: [{ parcelId: 'pc1', id: 'rl1', payments: p }] })

  it('lets the relocation agent request a payment with a typed amount and shows who must sign', async () => {
    const { fake, user } = open('/parcels/pc1', 'RelocationAgent', seed([]))
    await tab(user, 'Relocation')
    await user.click(await screen.findByRole('button', { name: 'Payments for relocation R-1' }))
    const form = await screen.findByRole('form', { name: 'Request a payment' })

    await user.click(within(form).getByRole('button', { name: 'Request payment' }))
    expect(await within(form).findByText('Choose the type of payment.')).toBeInTheDocument()
    expect(within(form).getByText('Enter an amount above zero.')).toBeInTheDocument()

    await user.selectOptions(within(form).getByLabelText(/^Payment type/), 'Moving expense')
    await user.type(within(form).getByLabelText(/^Payment amount/), '1500')
    await user.click(within(form).getByRole('button', { name: 'Request payment' }))

    await waitFor(() => expect(fake.relocations[0].payments).toHaveLength(1))
    expect(fake.relocations[0].payments![0]).toMatchObject({ paymentType: 'Moving expense', amount: 1500, status: 'Requested', requiredRoles: ['ProjectManager'] })
    const table = await screen.findByRole('table', { name: 'Relocation payments' })
    expect(table).toHaveTextContent('$1,500.00')
    expect(table).toHaveTextContent('Chain: ProjectManager')
    // You cannot sign what you requested.
    expect(within(table).getByRole('button', { name: /Approve payment Moving expense/ })).toBeDisabled()
    expect(within(table).getByText('You requested this payment, so someone else has to sign it.')).toBeInTheDocument()
  })

  it('shows Approve and Reject to the role that signs next, and a rejection needs a reason', async () => {
    const { fake, user } = open('/parcels/pc1', 'ProjectManager', seed([payment({ id: 'pay1' }), payment({ id: 'pay2', amount: 40, paymentType: 'Fixed payment' })]))
    await tab(user, 'Relocation')
    await user.click(await screen.findByRole('button', { name: 'Payments for relocation R-1' }))
    const table = await screen.findByRole('table', { name: 'Relocation payments' })

    await user.click(within(table).getByRole('button', { name: /Reject payment Fixed payment/ }))
    expect(await screen.findByText('Say why the payment is rejected.')).toBeInTheDocument()
    expect(fake.relocations[0].payments![1].status).toBe('Requested')
    await user.type(screen.getByLabelText('Decision note'), 'No receipt')
    await user.click(within(table).getByRole('button', { name: /Reject payment Fixed payment/ }))
    await waitFor(() => expect(fake.relocations[0].payments![1]).toMatchObject({ status: 'Rejected', decisionNote: 'No receipt' }))

    await user.click(within(table).getByRole('button', { name: /Approve payment Moving expense/ }))
    await waitFor(() => expect(fake.relocations[0].payments![0].status).toBe('Approved'))
    expect(await within(table).findByText(/Waiting for Finance to pay/)).toBeInTheDocument()
  })

  it('disables signing for a role that is not next and says who is', async () => {
    const { user } = open('/parcels/pc1', 'Finance', seed([payment({ requiredRoles: ['ProjectManager', 'Finance'] })]))
    await tab(user, 'Relocation')
    await user.click(await screen.findByRole('button', { name: 'Payments for relocation R-1' }))
    const table = await screen.findByRole('table', { name: 'Relocation payments' })
    expect(within(table).getByRole('button', { name: /Approve payment/ })).toBeDisabled()
    expect(within(table).getByText('Waiting for the ProjectManager to sign.')).toBeInTheDocument()
  })

  it('lets Finance mark an approved payment paid with a reference, and shows others it is waiting for Finance', async () => {
    const approved = payment({ status: 'Approved', nextApprover: null, approvals: [{ role: 'ProjectManager', approvedAt: '2026-10-02T00:00:00Z' }] })
    const finance = open('/parcels/pc1', 'Finance', seed([approved]))
    await tab(finance.user, 'Relocation')
    await finance.user.click(await screen.findByRole('button', { name: 'Payments for relocation R-1' }))
    await finance.user.type(await screen.findByLabelText('Payment reference'), 'EFT-77')
    await finance.user.click(screen.getByRole('button', { name: /Mark paid Moving expense/ }))
    await waitFor(() => expect(finance.fake.relocations[0].payments![0]).toMatchObject({ status: 'Paid', paymentReference: 'EFT-77' }))
    expect(await screen.findByText('Ref EFT-77')).toBeInTheDocument()
  })

  it('tells a role without payment access that the payment is waiting for Finance', async () => {
    const approved = payment({ status: 'Approved', nextApprover: null })
    const { user } = open('/parcels/pc1', 'Designer', seed([approved]))
    await tab(user, 'Relocation')
    await user.click(await screen.findByRole('button', { name: 'Payments for relocation R-1' }))
    expect(await screen.findByText('Waiting for Finance to pay.')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /Mark paid/ })).toBeNull()
    expect(screen.queryByRole('form', { name: 'Request a payment' })).toBeNull()
  })
})

describe('AT-RA-44 the approval bands on the Configuration page', () => {
  it('shows the placeholder default bands, lets an administrator add an approver and saves', async () => {
    const { fake, user } = open('/administration', 'Admin')
    const form = await screen.findByRole('form', { name: 'Relocation payment approval bands' })
    expect(screen.getByText(/placeholder defaults until saved/)).toBeInTheDocument()
    expect(within(form).getByLabelText('Band 1 minimum')).toHaveValue(0)
    expect(within(form).getByLabelText('Band 1 maximum')).toHaveValue(25000)
    expect(within(form).getByLabelText('Band 2 maximum')).toHaveValue(null)

    await user.selectOptions(within(form).getByLabelText('Add approver to band 1'), 'Finance')
    await user.click(within(form).getAllByRole('button', { name: 'Add' })[0])
    expect(within(form).getByRole('list', { name: 'Band 1 approvers' })).toHaveTextContent('Finance')
    await user.click(within(form).getByRole('button', { name: 'Save approval bands' }))

    await waitFor(() => expect(fake.bands.isDefault).toBe(false))
    expect(fake.bands.bands[0].roles).toEqual(['ProjectManager', 'Finance'])
    expect(await screen.findByText(/New payment requests use these bands/)).toBeInTheDocument()
  })

  it('shows the reason when the server refuses a broken chain', async () => {
    const { fake, user } = open('/administration', 'Admin')
    const form = await screen.findByRole('form', { name: 'Relocation payment approval bands' })
    await user.type(within(form).getByLabelText('Band 2 maximum'), '90000')
    await user.click(within(form).getByRole('button', { name: 'Save approval bands' }))
    expect(await screen.findByText(/no upper limit|Validation failed/i)).toBeInTheDocument()
    expect(fake.bands.isDefault).toBe(true)
  })
})

describe('AT-X-64 the Relocations work screen', () => {
  const rel = (over: object = {}) => ({
    parcelId: 'pc1', id: 'rl1', stage: 'Awaiting move', claimDeadline: '2027-11-01',
    details: { relocationNumber: 'RW-77', displaceeName: 'Quillfeather', relocationType: 'Residential', assignedTo: 'Casey', targetVacateDate: '2026-09-01' }, ...over,
  })

  it('lists cases with their stage and finds one by the displacee, then opens its parcel to work it', async () => {
    const { user } = open('/relocations', 'RelocationAgent', { relocations: [rel(), rel({ id: 'rl2', parcelId: 'pc2', details: { relocationNumber: 'RW-88', displaceeName: 'Brightwater', relocationType: 'Residential' } })] })
    expect(await screen.findByRole('heading', { name: 'Relocations' })).toBeInTheDocument()
    const found = await screen.findByRole('table', { name: 'Relocations found' })
    expect(found).toHaveTextContent('RW-77')
    expect(found).toHaveTextContent('Awaiting move')
    expect(found).toHaveTextContent('Quillfeather')

    await user.type(screen.getByLabelText('Search'), 'quillfeather')
    await waitFor(() => expect(screen.getByRole('table', { name: 'Relocations found' })).not.toHaveTextContent('RW-88'))
    await user.click(screen.getByRole('link', { name: 'Open RW-77' }))
    // It opens inside the parcel's Relocation tab: the parcel and its tab strip stay.
    expect(await screen.findByRole('heading', { name: /^Relocation RW-77/ })).toBeInTheDocument()
    expect(await screen.findByRole('tab', { name: 'Relocation', selected: true })).toBeInTheDocument()
    expect(await screen.findByRole('form', { name: 'Change relocation' })).toHaveTextContent('Parcel numberPN-1001')
    expect(screen.queryByRole('region', { name: 'Selected parcel' })).toBeNull()
  })

  it('AT-RA-45 opens a case on a page of its own, read-only for a role that cannot change it', async () => {
    const { user } = open('/parcels/pc1?tab=parcelrelocation&record=rl1', 'Designer', { relocations: [rel()] })
    expect(await screen.findByRole('heading', { name: /^Relocation RW-77/ })).toBeInTheDocument()
    const form = await screen.findByRole('form', { name: 'Change relocation' })
    expect(form).toHaveTextContent('Parcel numberPN-1001')
    expect(within(form).getByLabelText(/^Displacee name/)).toBeDisabled()
    expect(within(form).queryByRole('button', { name: 'Save changes' })).toBeNull()
    expect(screen.queryByRole('table', { name: 'Relocations' })).toBeNull()
    await user.click(screen.getByRole('button', { name: 'Back to relocations' }))
    expect(await screen.findByRole('table', { name: 'Relocations' })).toBeInTheDocument()
  })

  it('is in the menu for every role, because every role reads the page', async () => {
    open('/', 'Designer')
    expect(await screen.findByRole('link', { name: 'Relocations' })).toBeInTheDocument()
  })
})
