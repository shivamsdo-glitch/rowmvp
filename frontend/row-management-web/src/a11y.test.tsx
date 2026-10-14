import { beforeEach, describe, expect, it } from 'vitest'
import { screen, within } from '@testing-library/react'
import userEventModule from '@testing-library/user-event'
import axe from 'axe-core'
import { ROLES } from '@/api/auth'
import { permissionRowsFor } from '@/test/contract'
import { fakeAuthFetch } from '@/test/fake-api'
import { fakeParcelsApi } from '@/test/fake-parcels'
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

  it('the dashboard next-actions card has no violations', async () => {
    open('/')
    await screen.findByRole('region', { name: 'Next actions' })
    await settle()

    expect(await violations()).toEqual([])
  })
})
