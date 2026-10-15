import { beforeEach, describe, expect, it } from 'vitest'
import { screen, within } from '@testing-library/react'
import userEventModule from '@testing-library/user-event'
import axe from 'axe-core'
import { ROLES } from '@/api/auth'
import { permissionRowsFor } from '@/test/contract'
import { fakeAuthFetch } from '@/test/fake-api'
import { fakeParcelsApi } from '@/test/fake-parcels'
import { fakeLibraryApi } from '@/test/fake-library'
import { fakePeopleApi } from '@/test/fake-people'
import { fakeAcquisitionApi } from '@/test/fake-acquisition'
import { fakeRecordsApi } from '@/test/fake-records'
import { fakeProjectsApi } from '@/test/fake-projects'
import { renderApp } from '@/test/render-app'

beforeEach(() => sessionStorage.clear())

/** Colour contrast needs real layout, which jsdom does not have; every other automated rule is checked. */
async function violations() {
  const result = await axe.run(document.body, { rules: { 'color-contrast': { enabled: false } } })
  return result.violations.map((v) => `${v.id} (${v.impact}): ${v.help} -> ${v.nodes.map((n) => n.target.join(' ')).join(' | ')}`)
}

describe('accessibility (automated axe rules)', () => {
  it('the sign-in page has no violations', async () => {
    renderApp({ path: '/login', fetchImpl: fakeAuthFetch({ readable: [] }) as typeof fetch })
    await screen.findByText(/Development sign-in/i)

    expect(await violations()).toEqual([])
  })

  it.each(ROLES.map((r) => [r]))('the signed-in shell has no violations for %s', async (role) => {
    renderApp({ path: '/', fetchImpl: fakeAuthFetch({ rows: permissionRowsFor(role), role }) as typeof fetch, signedIn: true })
    await screen.findByRole('heading', { name: 'Dashboard' })

    expect(await violations()).toEqual([])
  })

  it('the open account menu has no violations', async () => {
    const user = userEventModule.setup()
    renderApp({ path: '/', fetchImpl: fakeAuthFetch({ readable: ['dashboard'] }) as typeof fetch, signedIn: true })
    await user.click(await screen.findByRole('button', { name: 'Account' }))

    expect(await violations()).toEqual([])
  })

  it('the collapsed sidebar has no violations and every control still has a name', async () => {
    const user = userEventModule.setup()
    renderApp({ path: '/', fetchImpl: fakeAuthFetch({ rows: permissionRowsFor('Admin'), role: 'Admin' }) as typeof fetch, signedIn: true })
    await user.click(await screen.findByRole('button', { name: 'Collapse sidebar' }))

    expect(await violations()).toEqual([])
  })

  it('the no-access page has no violations', async () => {
    renderApp({ path: '/administration', fetchImpl: fakeAuthFetch({ readable: ['dashboard'] }) as typeof fetch, signedIn: true })
    await screen.findByText(/don't have access/i)

    expect(await violations()).toEqual([])
  })
})

describe('accessibility of the project screens', () => {
  const seed = [
    { id: 'p1', number: '2026-0001', name: 'SR-20 Widening', region: 'North', district: 'D1', oversightType: 'Federal' as const },
    { id: 'p2', number: '2026-0002', name: 'Maple Sidewalk', oversightType: 'Local' as const },
  ]
  function open(path: string, role = 'ProjectManager') {
    const fake = fakeProjectsApi({
      role, rows: permissionRowsFor(role), projects: seed,
      budgets: { p1: [{ id: 'b1', fiscalYear: 2027, amount: 1000, notes: null }] },
      authorizations: { p1: [{ id: 'a1', type: 'Preliminary', authorizedAmount: 5, federalSharePercent: 80, federalShareAmount: 4, authorizedOn: '2026-01-01', referenceNumber: null, programmingConfirmed: true, notes: null }] },
    })
    renderApp({ path, fetchImpl: fake.fetch, signedIn: true })
  }

  it.each(ROLES.map((r) => [r]))('the project list has no violations for %s', async (role) => {
    open('/projects', role)
    await screen.findByRole('table', { name: 'Projects' })

    expect(await violations()).toEqual([])
  })

  it('the new-project form and the import panel have no violations', async () => {
    const user = userEventModule.setup()
    open('/projects')
    await user.click(await screen.findByRole('button', { name: 'New project' }))
    await screen.findByRole('form', { name: 'New project' })
    expect(await violations()).toEqual([])

    await user.click(screen.getByRole('button', { name: 'Create project' }))
    await screen.findByText('Name is required.')
    expect(await violations(), 'with validation errors showing').toEqual([])

    await user.click(screen.getByRole('button', { name: 'Cancel' }))
    await user.click(screen.getByRole('button', { name: 'Import CSV' }))
    await screen.findByLabelText('CSV file')
    expect(await violations()).toEqual([])
  })

  it.each([['Overview'], ['Cost estimates'], ['Budget'], ['ROW authorizations'], ['Clearance']])('the project detail "%s" tab has no violations', async (tab) => {
    const user = userEventModule.setup()
    open('/projects/p1')
    await user.click(await screen.findByRole('tab', { name: tab }))
    await screen.findByRole('tabpanel')
    await screen.findAllByText(/./)
    // let the tab's own data finish loading
    await new Promise((r) => setTimeout(r, 100))

    expect(await violations()).toEqual([])
  })

  it('the project detail for a read-only role has no violations', async () => {
    open('/projects/p1', 'Finance')
    await screen.findByRole('heading', { name: /SR-20 Widening/ })

    expect(await violations()).toEqual([])
  })

  it('the project number settings have no violations', async () => {
    open('/administration', 'Admin')
    await screen.findByLabelText('Number pattern')

    expect(await violations()).toEqual([])
  })
})

describe('accessibility of the parcel screens', () => {
  const projects = [{ id: 'p1', number: '2026-0001', name: 'Maple Sidewalk', oversightType: 'Local' as const }]
  const parcels = [
    { id: 'pc1', projectId: 'p1', number: '001', address: '12 Oak St', statusId: 's-ident', groupId: 'g1', parentTractId: 't1' },
    { id: 'pc2', projectId: 'p1', number: '002', address: '14 Oak St', statusId: 's-neg' },
  ]
  function open(path: string, role = 'ProjectManager') {
    const fake = fakeParcelsApi({
      role, rows: permissionRowsFor(role), projects, parcels,
      groups: [{ id: 'g1', projectId: 'p1', name: 'Main span' }], tracts: [{ id: 't1', projectId: 'p1', number: 'T-1', taxParcelNumbers: ['APN-1'] }],
      customFields: [{ key: 'zoning', label: 'Zoning', fieldType: 'List', options: ['Residential', 'Commercial'], isRequired: true }],
    })
    renderApp({ path, fetchImpl: fake.fetch, signedIn: true })
  }
  const settle = () => new Promise((r) => setTimeout(r, 100))

  it.each(ROLES.map((r) => [r]))('the parcel list has no violations for %s', async (role) => {
    open('/parcels', role)
    await screen.findByRole('table', { name: 'Parcels' })

    expect(await violations()).toEqual([])
  })

  it('the board, the new-parcel form (with errors) and the selection bar have no violations', async () => {
    const user = userEventModule.setup()
    open('/parcels')
    await screen.findByRole('table', { name: 'Parcels' })

    await user.click(screen.getByRole('checkbox', { name: 'Select parcel 001' }))
    await screen.findByRole('region', { name: 'Selected parcels' })
    expect(await violations(), 'with a selection').toEqual([])

    await user.click(screen.getByRole('button', { name: 'Board' }))
    await screen.findByRole('region', { name: 'Parcel board' })
    expect(await violations(), 'board').toEqual([])

    await user.click(screen.getByRole('button', { name: 'New parcel' }))
    const form = await screen.findByRole('form', { name: 'New parcel' })
    expect(await violations(), 'new parcel form').toEqual([])
    await user.click(within(form).getByRole('button', { name: 'Create parcel' }))
    await screen.findByText('Project is required.')
    expect(await violations(), 'with validation errors showing').toEqual([])
  })

  it.each([['Summary'], ['Interests'], ['Milestones & deadlines'], ['Workflow'], ['Site visits & notice']])('the parcel detail "%s" tab has no violations', async (tab) => {
    const user = userEventModule.setup()
    open('/parcels/pc1')
    await user.click(await screen.findByRole('tab', { name: tab }))
    await screen.findByRole('tabpanel')
    await settle()

    expect(await violations()).toEqual([])
  })

  it('the edit and split panels have no violations', async () => {
    const user = userEventModule.setup()
    open('/parcels/pc1')
    await user.click(await screen.findByRole('button', { name: 'Edit parcel' }))
    await screen.findByRole('form', { name: 'Edit parcel' })
    await settle()
    expect(await violations(), 'edit').toEqual([])

    await user.click(screen.getByRole('button', { name: 'Split' }))
    await screen.findByRole('form', { name: 'Split parts' })
    expect(await violations(), 'split').toEqual([])
  })

  it('the parcel detail for a read-only role has no violations', async () => {
    open('/parcels/pc1', 'Appraiser')
    await screen.findByRole('heading', { name: /Parcel 001/ })
    await settle()

    expect(await violations()).toEqual([])
  })

  it('the project parcels tab has no violations', async () => {
    const user = userEventModule.setup()
    open('/projects/p1')
    await user.click(await screen.findByRole('tab', { name: 'Parcels' }))
    await screen.findByRole('region', { name: 'Parcel roll-up' })
    await settle()

    expect(await violations()).toEqual([])
  })

  it('the parcel rules on the Configuration page have no violations, including an open editor', async () => {
    const user = userEventModule.setup()
    open('/administration', 'Admin')
    await screen.findByRole('region', { name: 'Acquisition workflows' })
    await within(await screen.findByRole('region', { name: 'Statutory deadlines' })).findByRole('table', { name: 'Deadline rules' })
    await settle()
    expect(await violations()).toEqual([])

    await user.click(screen.getByRole('button', { name: 'Edit status Identified' }))
    await user.click(screen.getByRole('button', { name: 'Add custom field' }))
    await settle()
    expect(await violations(), 'with editors open').toEqual([])
  })

  it('the time zone card and the project time zone section have no violations', async () => {
    open('/administration', 'Admin')
    await within(await screen.findByRole('region', { name: 'Time zone' })).findByRole('combobox', { name: 'Agency time zone' })
    await settle()
    expect(await violations(), 'configuration').toEqual([])
  })

  it('the project time zone section has no violations', async () => {
    open('/projects/p1')
    await within(await screen.findByRole('region', { name: 'Project time zone' })).findByRole('form', { name: 'Set the project time zone' })
    await settle()
    expect(await violations()).toEqual([])
  })

  it('the dashboard next-actions card has no violations', async () => {
    open('/')
    await screen.findByRole('region', { name: 'Next actions' })
    await settle()

    expect(await violations()).toEqual([])
  })
})

describe('accessibility of the parties screens (ROW-014..018)', () => {
  const settle = () => new Promise((r) => setTimeout(r, 50))
  function openParties(path: string, role = 'ProjectManager') {
    const fake = fakePeopleApi({
      role, rows: permissionRowsFor(role), projects: [{ id: 'p1', number: '2026-0001', name: 'Harbor Bridge' }],
      tracts: [{ id: 'tr1', projectId: 'p1', number: 'T-1' }],
      parcels: [{ id: 'pc1', projectId: 'p1', number: '001', parentTractId: 'tr1' }, { id: 'pc2', projectId: 'p1', number: '002' }],
      parties: [{ id: 'a', name: 'Pat Owner', hasTaxId: true, taxIdType: 'Ssn', taxIdMasked: '***-**-6789' }, { id: 'b', name: 'First Bank', kind: 'Organization' }],
      links: [{ partyId: 'a', tractId: 'tr1', role: 'FeeOwner', sharePercent: 100 }, { partyId: 'b', parcelId: 'pc1', role: 'Bank' }, { partyId: 'a', parcelId: 'pc2', role: 'Tenant', leaseOnFile: true }],
    })
    renderApp({ path, fetchImpl: fake.fetch, signedIn: true })
    return fake
  }

  it('the party list and its create form have no violations', async () => {
    const user = userEventModule.setup()
    openParties('/parties')
    await screen.findByRole('table', { name: 'Parties' })
    await settle()
    expect(await violations(), 'list').toEqual([])

    await user.click(screen.getByRole('button', { name: 'New party' }))
    await settle()
    expect(await violations(), 'with the form open').toEqual([])
  })

  it('a party page with its tax information, editor and usage has no violations', async () => {
    const user = userEventModule.setup()
    openParties('/parties/a')
    await screen.findByRole('form', { name: 'Tax information' })
    await screen.findByRole('table', { name: 'Where this party appears' })
    await settle()
    expect(await violations()).toEqual([])

    await user.click(screen.getByRole('button', { name: 'Edit details' }))
    await settle()
    expect(await violations(), 'with the editor open').toEqual([])
  })

  it('the parcel parties tab has no violations, including the add and edit forms', async () => {
    const user = userEventModule.setup()
    openParties('/parcels/pc2')
    await user.click(await screen.findByRole('tab', { name: 'Parties' }))
    await screen.findByRole('region', { name: 'Tenants and occupants' })
    await settle()
    expect(await violations(), 'tab').toEqual([])

    await user.click(screen.getByRole('button', { name: 'Add a person or organization' }))
    await settle()
    expect(await violations(), 'add form (existing party)').toEqual([])
    await user.click(screen.getByLabelText('A new person or organization'))
    await settle()
    expect(await violations(), 'add form (new party)').toEqual([])
    await user.click(screen.getByRole('button', { name: 'Cancel' }))
    await user.click(screen.getByRole('button', { name: 'Edit Pat Owner, Tenant' }))
    await settle()
    expect(await violations(), 'edit form').toEqual([])
  })

  it('a project tract owners panel has no violations', async () => {
    const user = userEventModule.setup()
    openParties('/projects/p1')
    await user.click(await screen.findByRole('tab', { name: 'Parcels' }))
    await user.click(await screen.findByRole('button', { name: 'Owners and parties of tract T-1' }))
    await screen.findByRole('region', { name: 'Owners and interests' })
    await settle()
    expect(await violations()).toEqual([])
  })
})

describe('accessibility of the Library', () => {
  const settle = () => new Promise((r) => setTimeout(r, 50))

  it('the Library has no violations for a list, a text field and with every form open', async () => {
    const user = userEventModule.setup()
    const fake = fakeLibraryApi({ role: 'Admin', rows: permissionRowsFor('Admin') })
    renderApp({ path: '/library', fetchImpl: fake.fetch, signedIn: true })
    await screen.findByRole('table', { name: 'Parcel status values' })
    await settle()
    expect(await violations(), 'parcel status list').toEqual([])

    await user.click(screen.getByRole('button', { name: 'New' }))
    await user.click(screen.getByRole('button', { name: 'Edit field' }))
    await user.click(within(screen.getByRole('table', { name: 'Parcel status values' })).getByRole('button', { name: 'Rename In Design' }))
    await settle()
    expect(await violations(), 'with the add, edit and rename forms open').toEqual([])

    await user.click(screen.getByRole('button', { name: /CAD number/ }))
    await screen.findByText(/people type it in on forms/)
    await settle()
    expect(await violations(), 'text field').toEqual([])

    await user.click(screen.getByRole('button', { name: 'New field' }))
    await settle()
    expect(await violations(), 'new field form').toEqual([])
  })
})

describe('accessibility of the parcel team, diary and notes', () => {
  const settle = () => new Promise((r) => setTimeout(r, 50))

  it('each tab has no violations, with its forms and an editor open', async () => {
    const user = userEventModule.setup()
    const fake = fakeRecordsApi({
      role: 'Admin', rows: permissionRowsFor('Admin'), projects: [{ id: 'p1', number: '2026-0001', name: 'Harbor Bridge' }],
      parcels: [{ id: 'pc1', projectId: 'p1', number: '001' }],
      team: [{ parcelId: 'pc1', name: 'Riley Okafor', role: 'Negotiator' }], diary: [{ parcelId: 'pc1' }], notes: [{ parcelId: 'pc1', text: 'A note' }],
    })
    renderApp({ path: '/parcels/pc1', fetchImpl: fake.fetch, signedIn: true })

    await user.click(await screen.findByRole('tab', { name: 'Team' }))
    await screen.findByRole('table', { name: 'Parcel team' })
    await settle()
    expect(await violations(), 'team').toEqual([])
    await user.click(screen.getByRole('button', { name: 'Edit Riley Okafor, Negotiator' }))
    await settle()
    expect(await violations(), 'team editor').toEqual([])

    await user.click(screen.getByRole('tab', { name: 'Diary' }))
    await screen.findByRole('table', { name: 'Negotiation diary' })
    await settle()
    expect(await violations(), 'diary').toEqual([])

    await user.click(screen.getByRole('tab', { name: 'Notes' }))
    await screen.findByText('A note')
    await settle()
    expect(await violations(), 'notes').toEqual([])
  })
})

describe('accessibility of rights of entry, reviews, reports, requests and the plan (ROW-077, 080, 239, 240, 241)', () => {
  const settle = () => new Promise((r) => setTimeout(r, 50))
  const fake = () => fakeAcquisitionApi({
    role: 'Admin', rows: permissionRowsFor('Admin'), projects: [{ id: 'p1', number: '2026-0001', name: 'Harbor Bridge' }],
    parcels: [{ id: 'pc1', projectId: 'p1', number: '001' }], gateEnabled: true,
    roes: [{ parcelId: 'pc1', status: 'Granted', grantedOn: '2026-09-01', expiresOn: '2026-12-01', grantedBy: 'Alex' }],
    reviews: [{ parcelId: 'pc1', discipline: 'Engineer' }],
    reports: [{ parcelId: 'pc1', kind: 'StaffReport', status: 'Submitted', submittedByMe: false }, { parcelId: 'pc1', kind: 'CertificateOfNecessity', status: 'Draft' }],
    requests: [{ property: 'Lot behind 112 Harbor Rd', submittedByMe: false }, { property: 'Old depot', status: 'Approved', decisionNote: 'Funded' }],
  })

  it('the parcel tabs have no violations, with a form and an editor open', async () => {
    const user = userEventModule.setup()
    renderApp({ path: '/parcels/pc1', fetchImpl: fake().fetch, signedIn: true })

    await user.click(await screen.findByRole('tab', { name: 'Right of entry' }))
    await screen.findByRole('table', { name: 'Rights of entry' })
    await settle()
    expect(await violations(), 'right of entry').toEqual([])
    await user.click(screen.getByRole('button', { name: /^Edit the granted right of entry/ }))
    await settle()
    expect(await violations(), 'right of entry editor').toEqual([])

    await user.click(screen.getByRole('tab', { name: 'Reviews & reports' }))
    await screen.findByRole('article', { name: 'Staff report' })
    await settle()
    expect(await violations(), 'reviews and reports').toEqual([])
    await user.click(within(screen.getByRole('article', { name: 'Certificate of necessity' })).getByRole('button', { name: /^Change the certificate/ }))
    await settle()
    expect(await violations(), 'report editor').toEqual([])
  })

  it('the rights of entry report and the requests page have no violations', async () => {
    const f = fake()
    const user = userEventModule.setup()
    const { router } = renderApp({ path: '/rights-of-entry', fetchImpl: f.fetch, signedIn: true })
    await screen.findByRole('table', { name: 'Rights of entry' })
    await settle()
    expect(await violations(), 'report').toEqual([])

    await router.navigate({ to: '/requests' })
    await screen.findByRole('list', { name: 'Requests' })
    await settle()
    expect(await violations(), 'requests').toEqual([])
    await user.click(within(screen.getByRole('article', { name: 'Request for Lot behind 112 Harbor Rd' })).getByRole('button', { name: 'Reject' }))
    await settle()
    expect(await violations(), 'requests with an error').toEqual([])
  })

  it('the project plan and the gate setting have no violations', async () => {
    const f = fake()
    const user = userEventModule.setup()
    const { router } = renderApp({ path: '/projects/p1', fetchImpl: f.fetch, signedIn: true })
    await user.click(await screen.findByRole('tab', { name: 'Plan' }))
    await screen.findByText('No parcels in this project yet')
    await settle()
    expect(await violations(), 'plan').toEqual([])

    await router.navigate({ to: '/administration' })
    await screen.findByRole('checkbox', { name: 'Require engineer and surveyor plan reviews' })
    await settle()
    expect(await violations(), 'administration').toEqual([])
  })
})
