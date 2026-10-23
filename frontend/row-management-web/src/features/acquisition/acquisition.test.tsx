import { beforeEach, describe, expect, it } from 'vitest'
import { screen, waitFor, within } from '@testing-library/react'
import userEventModule from '@testing-library/user-event'
import { ROLES } from '@/api/auth'
import type { AcquisitionPlan } from '@/api/acquisition'
import type { Project } from '@/api/projects'
import { levelFor, permissionRowsFor } from '@/test/contract'
import { fakeAcquisitionApi, type FakeAcquisitionOptions } from '@/test/fake-acquisition'
import { renderApp } from '@/test/render-app'
import { planToCsv } from '../projects/PlanTab'

beforeEach(() => sessionStorage.clear())

const can = (role: string, page: string, action: 'read' | 'create' | 'update' | 'delete') => {
  const level = levelFor(role, page)
  if (action === 'read') return level !== null
  if (action === 'delete') return level === 'P'
  if (action === 'update') return level === 'U' || level === 'S' || level === 'P'
  return level === 'S' || level === 'P'
}

const PROJECTS: Partial<Project>[] = [{ id: 'p1', number: '2026-0001', name: 'Harbor Bridge', oversightType: 'Local' }]

function open(path: string, role = 'ProjectManager', extra: Partial<FakeAcquisitionOptions> = {}) {
  const fake = fakeAcquisitionApi({
    role, rows: permissionRowsFor(role), projects: PROJECTS, today: '2026-10-14',
    parcels: [{ id: 'pc1', projectId: 'p1', number: '001', address: '1 Oak St' }, { id: 'pc-gone', projectId: 'p1', number: '009', state: 'Split' }],
    ...extra,
  })
  const user = userEventModule.setup()
  const app = renderApp({ path, fetchImpl: fake.fetch, signedIn: true })
  return { fake, user, ...app }
}

const tab = async (user: ReturnType<typeof userEventModule.setup>, name: string) => user.click(await screen.findByRole('tab', { name }))

// ── right of entry on a parcel (ROW-077) ────────────────────────────────

describe('Right of entry on a parcel (ROW-077)', () => {
  const seed = [
    { parcelId: 'pc1', id: 'r1', status: 'Granted' as const, grantedOn: '2026-09-01', expiresOn: '2026-12-01', grantedBy: 'Alex Marlowe', purpose: 'Survey' },
    { parcelId: 'pc1', id: 'r2', status: 'Granted' as const, grantedOn: '2026-05-01', expiresOn: '2026-06-01', grantedBy: 'Jordan Reyes' },
  ]

  it('lists rights of entry and shows a granted one past its end date as expired', async () => {
    const { user } = open('/parcels/pc1', 'ProjectManager', { roes: seed })
    await tab(user, 'Negotiation')

    const table = await screen.findByRole('table', { name: 'Rights of entry' })
    const rows = within(table).getAllByRole('row').slice(1)
    expect(rows[0]).toHaveTextContent('Granted')
    expect(rows[0]).toHaveTextContent('Alex Marlowe')
    expect(rows[1]).toHaveTextContent('Expired')
  })

  it('refuses a granted right without the date it was given, then records it', async () => {
    const { user, fake } = open('/parcels/pc1', 'Negotiator')
    await tab(user, 'Negotiation')
    const form = await screen.findByRole('form', { name: 'Record a right of entry' })

    await user.selectOptions(within(form).getByLabelText(/^Status/), 'Granted')
    await user.click(within(form).getByRole('button', { name: 'Record' }))
    expect(await within(form).findByText('Enter the date permission was given.')).toBeInTheDocument()
    expect(fake.roes).toHaveLength(0)

    await user.type(within(form).getByLabelText(/^Permission given on/), '2026-10-01')
    await user.type(within(form).getByLabelText(/^Letter mailed on/), '2026-09-20')
    await user.type(within(form).getByLabelText(/^Letter delivered on/), '2026-09-10')
    await user.click(within(form).getByRole('button', { name: 'Record' }))
    expect(await within(form).findByText('A letter cannot be delivered before it is mailed.')).toBeInTheDocument()
    await user.clear(within(form).getByLabelText(/^Letter delivered on/))
    await user.type(within(form).getByLabelText(/^Letter delivered on/), '2026-09-22')
    await user.type(within(form).getByLabelText(/^Permission ends on/), '2026-09-01')
    await user.click(within(form).getByRole('button', { name: 'Record' }))
    expect(await within(form).findByText('Permission cannot end before it was given.')).toBeInTheDocument()

    await user.clear(within(form).getByLabelText(/^Permission ends on/))
    await user.type(within(form).getByLabelText(/^Permission ends on/), '2026-12-31')
    await user.type(within(form).getByLabelText(/^Given by/), '  Alex Marlowe ')
    await user.click(within(form).getByRole('button', { name: 'Record' }))
    expect(await screen.findByRole('row', { name: /Alex Marlowe/ })).toBeInTheDocument()
    expect(fake.roes[0]).toMatchObject({ status: 'Granted', grantedOn: '2026-10-01', expiresOn: '2026-12-31', grantedBy: 'Alex Marlowe', letterMailedDate: '2026-09-20', letterDeliveredDate: '2026-09-22' })
  })

  it('shows the server’s refusal beside the field', async () => {
    const { user } = open('/parcels/pc1', 'Negotiator')
    await tab(user, 'Negotiation')
    const form = await screen.findByRole('form', { name: 'Record a right of entry' })

    await user.type(within(form).getByLabelText(/^Requested on/), '2026-11-30')
    await user.click(within(form).getByRole('button', { name: 'Record' }))
    expect(await within(form).findByText('The request date cannot be in the future.')).toBeInTheDocument()
  })

  it('changes and removes a right of entry', async () => {
    const { user, fake } = open('/parcels/pc1', 'Negotiator', { roes: seed })
    await tab(user, 'Negotiation')
    await user.click((await screen.findAllByRole('button', { name: /^Edit the granted right of entry/ }))[0])
    const form = screen.getByRole('form', { name: 'Change right of entry' })

    await user.selectOptions(within(form).getByLabelText(/^Status/), 'Revoked')
    await user.click(within(form).getByRole('button', { name: 'Save changes' }))
    await waitFor(() => expect(fake.roes.find((r) => r.id === 'r1')!.status).toBe('Revoked'))
    await user.click(await screen.findByRole('button', { name: 'Remove the revoked right of entry' }))
    await waitFor(() => expect(fake.roes.some((r) => r.id === 'r1')).toBe(false))
  })

  it('keeps a split parcel’s rights for reading with no form', async () => {
    const { user } = open('/parcels/pc-gone', 'ProjectManager', { roes: [{ parcelId: 'pc-gone', status: 'Requested' }] })
    await tab(user, 'Negotiation')
    expect(await screen.findByRole('table', { name: 'Rights of entry' })).toBeInTheDocument()
    expect(screen.queryByRole('form', { name: 'Record a right of entry' })).not.toBeInTheDocument()
  })

  it('says so when a load fails and offers to try again', async () => {
    const fake = fakeAcquisitionApi({ role: 'ProjectManager', rows: permissionRowsFor('ProjectManager'), projects: PROJECTS, parcels: [{ id: 'pc1', projectId: 'p1', number: '001' }] })
    let fail = true
    const fetchImpl = ((input: RequestInfo | URL, init?: RequestInit) =>
      fail && String(input).endsWith('/rights-of-entry') ? Promise.resolve(new Response(JSON.stringify({ title: 'server_error', detail: 'Boom.' }), { status: 500, headers: { 'Content-Type': 'application/json' } })) : fake.fetch(input, init)) as typeof fetch
    const user = userEventModule.setup()
    renderApp({ path: '/parcels/pc1', fetchImpl, signedIn: true })
    await tab(user, 'Negotiation')
    expect(await screen.findByText('Rights of entry could not be loaded')).toBeInTheDocument()
    fail = false
    await user.click(screen.getByRole('button', { name: 'Try again' }))
    expect(await screen.findByText('No rights of entry')).toBeInTheDocument()
  })

  it.each(ROLES)('%s sees the right of entry form on the Negotiation tab only when that page allows it', async (role) => {
    if (!can(role, 'parcels', 'read') || !can(role, 'parcelnegotiation', 'read')) return
    const { user } = open('/parcels/pc1', role, { roes: seed })
    await tab(user, 'Negotiation')
    expect(await screen.findByRole('table', { name: 'Rights of entry' })).toBeInTheDocument()
    expect(!!screen.queryByRole('form', { name: 'Record a right of entry' })).toBe(can(role, 'parcelnegotiation', 'create'))
    expect(screen.queryAllByRole('button', { name: /^Edit the/ }).length > 0).toBe(can(role, 'parcelnegotiation', 'update'))
    expect(screen.queryAllByRole('button', { name: /^Remove the/ }).length > 0).toBe(can(role, 'parcelnegotiation', 'delete'))
  })
})

// ── plan reviews and staff reports (ROW-239, 240) ───────────────────────

describe('Plan reviews (ROW-239)', () => {
  it('says the parcel is held while the gate is on and a review is missing, and releases it when both approve', async () => {
    const { user } = open('/parcels/pc1', 'ProjectManager', { gateEnabled: true })
    await tab(user, 'Reviews & site visits')

    const banner = await screen.findByRole('status', { name: 'Plan review gate' })
    expect(banner).toHaveTextContent('held at Title & survey')
    expect(banner).toHaveTextContent('engineer and surveyor')

    const form = screen.getByRole('form', { name: 'Record a plan review' })
    await user.type(within(form).getByLabelText(/^Date reviewed/), '2026-10-10')
    await user.type(within(form).getByLabelText(/^Reviewer/), 'Morgan Ellis, P.E.')
    await user.click(within(form).getByRole('button', { name: 'Record review' }))
    await waitFor(() => expect(screen.getByRole('status', { name: 'Plan review gate' })).toHaveTextContent('review by the surveyor'))

    await user.selectOptions(within(form).getByLabelText(/^Review by/), 'Surveyor')
    await user.type(within(form).getByLabelText(/^Date reviewed/), '2026-10-11')
    await user.type(within(form).getByLabelText(/^Reviewer/), 'Sam Ortiz, PLS')
    await user.click(within(form).getByRole('button', { name: 'Record review' }))
    await waitFor(() => expect(screen.getByRole('status', { name: 'Plan review gate' })).toHaveTextContent('Plan reviews complete'))
  })

  it('says reviews are only kept for the file when the gate is off', async () => {
    const { user } = open('/parcels/pc1')
    await tab(user, 'Reviews & site visits')
    expect(await screen.findByRole('status', { name: 'Plan review gate' })).toHaveTextContent('does not require plan reviews')
  })

  it('needs notes when changes are required, a reviewer and a date', async () => {
    const { user, fake } = open('/parcels/pc1')
    await tab(user, 'Reviews & site visits')
    const form = await screen.findByRole('form', { name: 'Record a plan review' })

    await user.click(within(form).getByRole('button', { name: 'Record review' }))
    expect(await within(form).findByText('Choose the review date.')).toBeInTheDocument()
    expect(within(form).getByText('Enter who reviewed the plans.')).toBeInTheDocument()

    await user.selectOptions(within(form).getByLabelText(/^Outcome/), 'ChangesRequired')
    await user.type(within(form).getByLabelText(/^Date reviewed/), '2026-10-10')
    await user.type(within(form).getByLabelText(/^Reviewer/), 'Morgan')
    await user.click(within(form).getByRole('button', { name: 'Record review' }))
    expect(await within(form).findByText('Say what has to change.')).toBeInTheDocument()
    expect(fake.reviews).toHaveLength(0)
  })

  it('removes a review', async () => {
    const { user, fake } = open('/parcels/pc1', 'Admin', { reviews: [{ parcelId: 'pc1', id: 'pr-x', discipline: 'Surveyor', reviewedOn: '2026-10-01' }] })
    await tab(user, 'Reviews & site visits')
    await user.click(await screen.findByRole('button', { name: /^Remove the surveyor review/ }))
    await waitFor(() => expect(fake.reviews).toHaveLength(0))
  })
})

describe('Staff report and certificate of necessity (ROW-240)', () => {
  it('writes, changes and submits a staff report', async () => {
    const { user, fake } = open('/parcels/pc1')
    await tab(user, 'Reviews & site visits')
    const card = await screen.findByRole('article', { name: 'Staff report' })

    await user.click(within(card).getByRole('button', { name: /^Write the staff report/ }))
    const form = screen.getByRole('form', { name: 'Write the staff report' })
    await user.clear(within(form).getByLabelText(/^Title/))
    await user.click(within(form).getByRole('button', { name: 'Save' }))
    expect(await within(form).findByText('Enter a title.')).toBeInTheDocument()
    expect(within(form).getByText('Write the report.')).toBeInTheDocument()

    await user.type(within(form).getByLabelText(/^Title/), 'Staff report 001')
    await user.type(within(form).getByLabelText(/^Report/), 'Needed for the approach.')
    await user.click(within(form).getByRole('button', { name: 'Save' }))
    expect(await within(await screen.findByRole('article', { name: 'Staff report' })).findByText('Needed for the approach.')).toBeInTheDocument()
    expect(fake.reports[0]).toMatchObject({ kind: 'StaffReport', status: 'Draft', title: 'Staff report 001' })

    await user.click(within(screen.getByRole('article', { name: 'Staff report' })).getByRole('button', { name: 'Submit for approval' }))
    await waitFor(() => expect(fake.reports[0].status).toBe('Submitted'))
    const after = await screen.findByRole('article', { name: 'Staff report' })
    expect(within(after).getByText('Submitted')).toBeInTheDocument()
    expect(within(after).queryByRole('button', { name: 'Submit for approval' })).not.toBeInTheDocument()
    expect(within(after).getByText(/someone else has to approve or reject it/)).toBeInTheDocument()
    expect(within(after).queryByRole('button', { name: 'Approve' })).not.toBeInTheDocument()
  })

  it('lets someone else approve, and rejecting needs a reason', async () => {
    const { user, fake } = open('/parcels/pc1', 'Finance', { reports: [{ parcelId: 'pc1', id: 'sr1', kind: 'CertificateOfNecessity', status: 'Submitted', title: 'Certificate', body: 'Necessary.', submittedByMe: false }] })
    await tab(user, 'Reviews & site visits')
    const card = await screen.findByRole('article', { name: 'Certificate of necessity' })

    await user.click(within(card).getByRole('button', { name: 'Reject' }))
    expect(await within(card).findByText('Say why the report is rejected.')).toBeInTheDocument()
    await user.type(within(card).getByLabelText(/^Note/), 'Needs the cost basis.')
    await user.click(within(card).getByRole('button', { name: 'Reject' }))
    await waitFor(() => expect(fake.reports[0]).toMatchObject({ status: 'Rejected', decisionNote: 'Needs the cost basis.' }))
    expect(await screen.findByText('Decision note: Needs the cost basis.')).toBeInTheDocument()
    const after = screen.getByRole('article', { name: 'Certificate of necessity' })
    expect(within(after).queryByRole('button', { name: 'Reject' })).not.toBeInTheDocument()
    expect(within(after).queryByRole('button', { name: /^Change the certificate/ })).not.toBeInTheDocument() // Finance cannot edit parcels
  })

  it('approves a submitted report', async () => {
    const { user, fake } = open('/parcels/pc1', 'Appraiser', { reports: [{ parcelId: 'pc1', id: 'sr1', kind: 'StaffReport', status: 'Submitted', submittedByMe: false }] })
    await tab(user, 'Reviews & site visits')
    await user.click(await screen.findByRole('button', { name: 'Approve' }))
    await waitFor(() => expect(fake.reports[0].status).toBe('Approved'))
  })

  it('shows the server’s refusal when two live reports of a kind are attempted', async () => {
    const { user } = open('/parcels/pc1', 'ProjectManager', { reports: [{ parcelId: 'pc1', id: 'sr1', kind: 'StaffReport', status: 'Approved', canEdit: false }] })
    await tab(user, 'Reviews & site visits')
    const card = await screen.findByRole('article', { name: 'Staff report' })
    expect(within(card).queryByRole('button', { name: /^Change the staff report/ })).not.toBeInTheDocument()
    expect(within(card).getByText('Approved')).toBeInTheDocument()
  })

  it.each(ROLES)('%s sees the controls the Parcels and Approvals pages allow', async (role) => {
    if (!can(role, 'parcels', 'read')) return
    const { user } = open('/parcels/pc1', role, {
      reports: [{ parcelId: 'pc1', id: 'sr1', kind: 'StaffReport', status: 'Submitted', submittedByMe: false }, { parcelId: 'pc1', id: 'sr2', kind: 'CertificateOfNecessity', status: 'Draft' }],
    })
    await tab(user, 'Reviews & site visits')
    await screen.findByRole('article', { name: 'Staff report' })
    expect(screen.queryAllByRole('button', { name: 'Approve' }).length > 0).toBe(can(role, 'approvals', 'update'))
    expect(screen.queryAllByRole('button', { name: 'Submit for approval' }).length > 0).toBe(can(role, 'parcels', 'update'))
    expect(!!screen.queryByRole('form', { name: 'Record a plan review' })).toBe(can(role, 'parcels', 'update'))
  })
})

// ── the report of all rights of entry (ROW-077) ─────────────────────────

describe('Rights of entry report (ROW-077)', () => {
  const roes = [
    { parcelId: 'pc1', id: 'r1', status: 'Granted' as const, grantedOn: '2026-09-01', expiresOn: '2026-10-20', grantedBy: 'Alex Marlowe' },
    { parcelId: 'pc1', id: 'r2', status: 'Granted' as const, grantedOn: '2026-05-01', expiresOn: '2026-06-01' },
    { parcelId: 'pc1', id: 'r3', status: 'Requested' as const, requestedOn: '2026-10-01' },
  ]

  it('lists every right with the parcel linked and says how soon one ends', async () => {
    open('/rights-of-entry', 'Negotiator', { roes })
    const table = await screen.findByRole('table', { name: 'Rights of entry' })
    expect(within(table).getAllByRole('row')).toHaveLength(4)
    expect(within(table).getAllByRole('link', { name: '001' })[0]).toHaveAttribute('href', '/parcels/pc1')
    expect(within(table).getByText('(in 6 days)')).toBeInTheDocument()
    expect(within(table).getByText('Expired')).toBeInTheDocument()
  })

  it('filters by status and by when they end', async () => {
    const { user } = open('/rights-of-entry', 'ProjectManager', { roes })
    await screen.findByRole('table', { name: 'Rights of entry' })

    await user.selectOptions(screen.getByLabelText('Status'), 'Expired')
    await waitFor(() => expect(within(screen.getByRole('table', { name: 'Rights of entry' })).getAllByRole('row')).toHaveLength(2))

    await user.selectOptions(screen.getByLabelText('Status'), '')
    await user.selectOptions(screen.getByLabelText('Ending'), '30')
    await waitFor(() => expect(within(screen.getByRole('table', { name: 'Rights of entry' })).getAllByRole('row')).toHaveLength(2))
    expect(screen.getByText('Alex Marlowe')).toBeInTheDocument()
  })

  it('says when nothing matches and when there are none at all', async () => {
    const { user } = open('/rights-of-entry', 'ProjectManager', { roes: [{ parcelId: 'pc1', status: 'Requested' }] })
    await screen.findByRole('table', { name: 'Rights of entry' })
    await user.selectOptions(screen.getByLabelText('Status'), 'Denied')
    expect(await screen.findByText('No rights of entry match')).toBeInTheDocument()
  })

  it('offers a retry when the report fails', async () => {
    open('/rights-of-entry', 'ProjectManager', { reportFails: true })
    expect(await screen.findByText('Rights of entry could not be loaded')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Try again' })).toBeInTheDocument()
  })

  it.each(ROLES)('%s opens the report only when the Parcels page can be read', async (role) => {
    const { router } = open('/rights-of-entry', role, { roes })
    if (can(role, 'parcels', 'read')) {
      expect(await screen.findByRole('table', { name: 'Rights of entry' })).toBeInTheDocument()
      expect(screen.getByRole('link', { name: 'Rights of entry' })).toBeInTheDocument()
    } else {
      await waitFor(() => expect(router.state.location.pathname).toBe('/forbidden'))
    }
  })
})

// ── acquisition requests (ROW-080) ──────────────────────────────────────

describe('Acquisition requests (ROW-080)', () => {
  const requests = [
    { id: 'q1', property: 'Lot behind 112 Harbor Rd', requesterName: 'Design team', submittedByMe: false },
    { id: 'q2', property: 'North quay parcel', requesterName: 'Harbor Port Authority', requesterType: 'External' as const, requesterEmail: 'land@port.example', submittedByMe: true },
    { id: 'q3', property: 'Old depot', status: 'Approved' as const, decisionNote: 'Funded' },
  ]

  it('lists requests with who asked, and approves one that someone else entered', async () => {
    const { user, fake } = open('/requests', 'Finance', { requests })
    const first = await screen.findByRole('article', { name: 'Request for Lot behind 112 Harbor Rd' })
    expect(first).toHaveTextContent('From Design team (inside the agency)')
    expect(screen.getByRole('article', { name: 'Request for North quay parcel' })).toHaveTextContent('outside the agency')

    await user.click(within(first).getByRole('button', { name: 'Approve' }))
    await waitFor(() => expect(fake.requests.find((r) => r.id === 'q1')!.status).toBe('Approved'))
  })

  it('does not offer to approve a request the signed-in person entered, and offers to withdraw it', async () => {
    const { user, fake } = open('/requests', 'Admin', { requests })
    const mine = await screen.findByRole('article', { name: 'Request for North quay parcel' })
    expect(within(mine).queryByRole('button', { name: 'Approve' })).not.toBeInTheDocument()

    await user.click(within(mine).getByRole('button', { name: 'Withdraw this request' }))
    await waitFor(() => expect(fake.requests.find((r) => r.id === 'q2')!.status).toBe('Withdrawn'))
  })

  it('needs a reason to reject', async () => {
    const { user, fake } = open('/requests', 'Finance', { requests })
    const card = await screen.findByRole('article', { name: 'Request for Lot behind 112 Harbor Rd' })
    await user.click(within(card).getByRole('button', { name: 'Reject' }))
    expect(await within(card).findByText('Say why the request is rejected.')).toBeInTheDocument()
    await user.type(within(card).getByLabelText(/^Note/), 'Outside the corridor.')
    await user.click(within(card).getByRole('button', { name: 'Reject' }))
    await waitFor(() => expect(fake.requests.find((r) => r.id === 'q1')).toMatchObject({ status: 'Rejected', decisionNote: 'Outside the corridor.' }))
  })

  it('filters by status', async () => {
    const { user } = open('/requests', 'Finance', { requests })
    await screen.findByRole('list', { name: 'Requests' })
    await user.selectOptions(screen.getByLabelText('Status'), 'Approved')
    await waitFor(() => expect(within(screen.getByRole('list', { name: 'Requests' })).getAllByRole('article')).toHaveLength(1))
    expect(screen.getByText('Decision note: Funded')).toBeInTheDocument()
  })

  it('enters a request, refusing the blanks and a bad email', async () => {
    const { user, fake } = open('/requests', 'ProjectManager')
    const form = await screen.findByRole('form', { name: 'Enter a request' })

    await user.click(within(form).getByRole('button', { name: 'Submit request' }))
    expect(await within(form).findByText('Enter who is asking.')).toBeInTheDocument()
    expect(within(form).getByText('Say which property is wanted.')).toBeInTheDocument()

    await user.type(within(form).getByLabelText(/^Who is asking/), 'Design team')
    await user.type(within(form).getByLabelText(/^Property wanted/), 'Lot behind 112 Harbor Rd')
    await user.type(within(form).getByLabelText(/^Their email/), 'not an email')
    await user.click(within(form).getByRole('button', { name: 'Submit request' }))
    expect(await within(form).findByText('Enter a valid email address.')).toBeInTheDocument()

    await user.clear(within(form).getByLabelText(/^Their email/))
    await user.selectOptions(within(form).getByLabelText(/^Project/), 'p1')
    await user.click(within(form).getByRole('button', { name: 'Submit request' }))
    expect(await within(form).findByText(/Request entered/)).toBeInTheDocument()
    expect(fake.requests[0]).toMatchObject({ requesterName: 'Design team', projectId: 'p1', status: 'Submitted' })
    expect(await screen.findByRole('article', { name: 'Request for Lot behind 112 Harbor Rd' })).toBeInTheDocument()
  })

  it.each(ROLES)('%s opens the page only with the Approvals page, and sees the form and buttons the pages allow', async (role) => {
    const { router } = open('/requests', role, { requests })
    if (!can(role, 'approvals', 'read')) {
      await waitFor(() => expect(router.state.location.pathname).toBe('/forbidden'))
      return
    }
    await screen.findByRole('list', { name: 'Requests' })
    expect(!!screen.queryByRole('form', { name: 'Enter a request' })).toBe(can(role, 'projects', 'create'))
    expect(screen.queryAllByRole('button', { name: 'Approve' }).length > 0).toBe(can(role, 'approvals', 'update'))
  })
})

// ── administration and the plan ─────────────────────────────────────────

describe('Plan review gate setting (ROW-239)', () => {
  it('lets an administrator turn the gate on and off', async () => {
    const { user, fake } = open('/administration', 'Admin')
    const box = await screen.findByRole('checkbox', { name: 'Require engineer and surveyor plan reviews' })
    expect(box).not.toBeChecked()
    await user.click(box)
    await waitFor(() => expect(fake.gate.enabled).toBe(true))
    await waitFor(() => expect(screen.getByRole('checkbox', { name: 'Require engineer and surveyor plan reviews' })).toBeChecked())
    await user.click(screen.getByRole('checkbox', { name: 'Require engineer and surveyor plan reviews' }))
    await waitFor(() => expect(fake.gate.enabled).toBe(false))
  })

  it.each(ROLES)('%s cannot change the gate unless the Administration page allows it', async (role) => {
    if (!can(role, 'administration', 'read')) return
    open('/administration', role)
    const box = await screen.findByRole('checkbox', { name: 'Require engineer and surveyor plan reviews' })
    expect(!(box as HTMLInputElement).disabled).toBe(can(role, 'administration', 'update'))
  })
})

describe('Project plan (ROW-241)', () => {
  const plan: AcquisitionPlan['plan'] = {
    parcelCount: 2, areaSqFt: 12000, closedCount: 1, overdueCount: 1, firstDueOn: '2026-10-01', lastPlannedCloseOn: '2027-03-01',
    budget: { totalBudget: 1_000_000, currentEstimate: 1_200_000, authorizedTotal: 700_000, variance: -200_000, overBudget: true },
    parcels: [
      { id: 'a', number: '001', address: '1 Oak St', areaSqFt: 7000, statusName: 'Active', stepLabel: 'Appraisal', nextMilestone: 'Offer delivered', nextDueOn: '2026-10-01', isOverdue: true, plannedCloseOn: '2027-03-01', isClosed: false, milestonesDone: 1, milestonesTotal: 5 },
      { id: 'b', number: '002', address: null, areaSqFt: 5000, statusName: 'Closed', stepLabel: 'Closing', nextMilestone: null, nextDueOn: null, isOverdue: false, plannedCloseOn: '2026-08-01', isClosed: true, milestonesDone: 5, milestonesTotal: 5 },
    ],
  }

  it('shows the schedule and flags the budget when the estimate is over it', async () => {
    const { user } = open('/projects/p1', 'Finance', { plans: { p1: plan } })
    await tab(user, 'Plan')

    const summary = await screen.findByRole('list', { name: 'Plan summary' }).catch(() => screen.getByLabelText('Plan summary'))
    expect(summary).toHaveTextContent('Closed1 of 2')
    const budget = screen.getByRole('status', { name: 'Budget against estimate' })
    expect(budget).toHaveTextContent('Over budget by')
    expect(budget).toHaveTextContent('$200,000')
    expect(budget).toHaveTextContent('The estimate is over the budget.')

    const table = screen.getByRole('table', { name: 'Parcel schedule' })
    const rows = within(table).getAllByRole('row').slice(1)
    expect(rows[0]).toHaveTextContent('Offer delivered')
    expect(rows[0]).toHaveTextContent('Overdue')
    expect(rows[1]).toHaveTextContent('All done')
  })

  it('says what is missing when there is no budget or estimate', async () => {
    const { user } = open('/projects/p1', 'ProjectManager')
    await tab(user, 'Plan')
    expect(await screen.findByText(/Enter a budget and a cost estimate/)).toBeInTheDocument()
    expect(screen.getByText('No parcels in this project yet')).toBeInTheDocument()
  })

  it.each(ROLES)('%s sees the Plan tab only when the Parcels page can be read', async (role) => {
    const { user } = open('/projects/p1', role, { plans: { p1: plan } })
    if (!can(role, 'projects', 'read')) return
    await screen.findByRole('tablist', { name: 'Project sections' })
    expect(!!screen.queryByRole('tab', { name: 'Plan' })).toBe(can(role, 'parcels', 'read'))
    void user
  })

  it('turns the plan into a spreadsheet file with quoting and formula protection', () => {
    const csv = planToCsv({
      projectId: 'p1', projectNumber: '2026-0001', projectName: 'Harbor, "East" Bridge', today: '2026-10-14',
      plan: { ...plan, parcels: [{ ...plan.parcels[0], address: '=HYPERLINK("x")' }, plan.parcels[1]] },
    })
    const lines = csv.trimEnd().split('\r\n')
    expect(lines[0]).toBe('Project,2026-0001,"Harbor, ""East"" Bridge"')
    expect(lines).toContain('Budget less estimate,-200000')
    expect(lines.find((l) => l.startsWith('001'))).toContain(`"'=HYPERLINK(""x"")"`)
    expect(lines.find((l) => l.startsWith('002'))).toBe('002,,5000,Closed,Closing,,,false,2026-08-01,true,5,5')
  })
})
