import { beforeEach, describe, expect, it } from 'vitest'
import { screen, waitFor, within } from '@testing-library/react'
import userEventModule from '@testing-library/user-event'
import { ROLES } from '@/api/auth'
import type { Project } from '@/api/projects'
import { levelFor, permissionRowsFor } from '@/test/contract'
import { fakeProjectsApi } from '@/test/fake-projects'
import { renderApp } from '@/test/render-app'

beforeEach(() => sessionStorage.clear())

const canCreateParcel = (role: string) => ['S', 'P'].includes(levelFor(role, 'parcels') ?? '')
const form = (name: string) => screen.getByRole('form', { name })

const SEED: Partial<Project>[] = [
  {
    id: 'p-det', number: 'ALR-PPhase 1-029', name: 'Premium Hub', region: 'South', district: 'D3', oversightType: 'Federal',
    phase: 'Phase 1', location: 'Bangalore', projectManagerUserId: 'u-pm', projectManagerName: 'ROW Project Manager',
    documentFolderStructure: 'TEST', documentProperties: 'MTRDOCM', constructionProjectManager: 'ritz',
    lettingDate: '2026-05-01', rightOfWayClearDate: '2027-05-31', constructionStartDate: '2026-05-01', constructionEndDate: '2028-05-23',
    linkedSource: 'Build', linkedProjectNumber: 'BLD-2025-017', linkedProjectName: 'Harbor Bridge Replacement',
    fundingAmount: 477770.44, designEstimate: 474776.99, boardApprovedFunds: 477770.44, currentEstimate: 474776.99, actualCost: 6647.33,
  },
]

function open(path: string, role = 'ProjectManager') {
  const fake = fakeProjectsApi({ role, rows: permissionRowsFor(role), projects: SEED })
  const user = userEventModule.setup()
  const app = renderApp({ path, fetchImpl: fake.fetch, signedIn: true })
  return { fake, user, ...app }
}

describe('ROW project details (AT-PM-40, 41)', () => {
  it('shows the details, the funding information and the construction details of a project', async () => {
    open('/projects/p-det')

    const details = await screen.findByRole('region', { name: 'Project details' })
    for (const [k, v] of [['Phase', 'Phase 1'], ['Location', 'Bangalore'], ['Project manager', 'ROW Project Manager'], ['Document folder structure', 'TEST'],
      ['Document properties', 'MTRDOCM'], ['Linked project', 'Build: BLD-2025-017 · Harbor Bridge Replacement']])
      expect(within(details).getByText(k).closest('div')).toHaveTextContent(v)
    const funding = screen.getByRole('region', { name: 'Funding information' })
    expect(funding).toHaveTextContent('Board approved funds')
    expect(funding).toHaveTextContent('$477,770')
    expect(funding).toHaveTextContent('Actual cost')
    expect(funding).toHaveTextContent('$6,647')
    const construction = screen.getByRole('region', { name: 'Construction details' })
    expect(construction).toHaveTextContent('ritz')
    expect(construction).toHaveTextContent('Right of way clear date')
  })

  it('asks for a phase on a new project (started at Phase 1), offers the agency people as manager and saves the details', async () => {
    const { fake, user } = open('/projects')
    await user.click(await screen.findByRole('button', { name: 'New project' }))
    const f = form('New project')

    expect(within(f).getByLabelText(/^Phase/)).toHaveValue('Phase 1')
    await user.clear(within(f).getByLabelText(/^Phase/))
    await user.type(within(f).getByLabelText(/^Name/), 'Detail Project')
    await user.click(screen.getByRole('button', { name: 'Create project' }))
    expect(await within(f).findByText('Phase is required.')).toBeInTheDocument()

    await user.type(within(f).getByLabelText(/^Phase/), 'Phase 2')
    await user.type(within(f).getByLabelText(/^Location/), 'Harbor Rd')
    await waitFor(() => expect(within(within(f).getByLabelText(/^Project manager/)).getAllByRole('option').map((o) => o.textContent)).toEqual(['Select one', 'ROW Project Manager', 'ROW Designer']))
    await user.selectOptions(within(f).getByLabelText(/^Project manager/), 'ROW Designer')
    await user.type(within(f).getByLabelText(/^Construction project manager/), 'ritz')
    await user.type(within(f).getByLabelText(/^Right of way clear date/), '2027-05-31')
    await user.click(screen.getByRole('button', { name: 'Create project' }))

    expect(await screen.findByRole('heading', { name: /Detail Project/ })).toBeInTheDocument()
    expect(fake.calls.find((c) => c.key === 'POST /api/v1/projects')!.body).toMatchObject({
      phase: 'Phase 2', location: 'Harbor Rd', projectManagerUserId: 'u-design', constructionProjectManager: 'ritz', rightOfWayClearDate: '2027-05-31',
      linkedSource: null, linkedProjectNumber: null,
    })
    expect(within(await screen.findByRole('region', { name: 'Project details' })).getByText('Project manager').closest('div')).toHaveTextContent('ROW Designer')
  })

  it('links a project to a Planned or Build project from the other module and shows its name', async () => {
    const { fake, user } = open('/projects')
    await user.click(await screen.findByRole('button', { name: 'New project' }))
    const f = form('New project')
    await user.type(within(f).getByLabelText(/^Name/), 'Linked Project')

    expect(within(f).queryByLabelText(/^Linked project/)).not.toBeInTheDocument()
    await user.selectOptions(within(f).getByLabelText(/^Link to/), 'Planned')
    await user.click(screen.getByRole('button', { name: 'Create project' }))
    expect(await within(f).findByText('Choose the project to link to.')).toBeInTheDocument()

    await user.type(within(f).getByLabelText(/^Search linked projects/), 'maple')
    await waitFor(() => expect(within(within(f).getByLabelText(/^Linked project/)).getAllByRole('option').map((o) => o.textContent)).toEqual(['Select one', 'PLN-2026-002 · Maple Avenue Extension']))
    await user.selectOptions(within(f).getByLabelText(/^Linked project/), 'PLN-2026-002')
    await user.click(screen.getByRole('button', { name: 'Create project' }))

    expect(await screen.findByRole('heading', { name: /Linked Project/ })).toBeInTheDocument()
    expect(fake.calls.find((c) => c.key === 'POST /api/v1/projects')!.body).toMatchObject({ linkedSource: 'Planned', linkedProjectNumber: 'PLN-2026-002' })
    expect(within(await screen.findByRole('region', { name: 'Project details' })).getByText('Linked project').closest('div')).toHaveTextContent('Planned: PLN-2026-002 · Maple Avenue Extension')
  })

  it('changing the source clears the chosen linked project, and Not linked sends no link', async () => {
    const { fake, user } = open('/projects/p-det')
    await user.click(await screen.findByRole('button', { name: 'Edit project' }))
    const f = form('Edit project')

    expect(within(f).getByLabelText(/^Link to/)).toHaveValue('Build')
    await user.selectOptions(within(f).getByLabelText(/^Link to/), 'Planned')
    expect(within(f).getByLabelText(/^Linked project/)).toHaveValue('')
    await user.selectOptions(within(f).getByLabelText(/^Link to/), '')
    await user.click(screen.getByRole('button', { name: 'Save changes' }))

    await waitFor(() => expect(fake.calls.some((c) => c.key.startsWith('PUT /api/v1/projects/p-det'))).toBe(true))
    expect(fake.calls.find((c) => c.key.startsWith('PUT /api/v1/projects/p-det'))!.body).toMatchObject({ linkedSource: null, linkedProjectNumber: null })
  })

  it('editing a project made before the details existed does not demand a phase', async () => {
    const { fake, user } = open('/projects/p-det')
    await user.click(await screen.findByRole('button', { name: 'Edit project' }))
    await user.clear(within(form('Edit project')).getByLabelText(/^Phase/))
    await user.click(screen.getByRole('button', { name: 'Save changes' }))

    await waitFor(() => expect(fake.calls.some((c) => c.key.startsWith('PUT /api/v1/projects/p-det'))).toBe(true))
    expect(screen.queryByText('Phase is required.')).not.toBeInTheDocument()
  })
})

describe('Add parcel from a project (AT-PM-42)', () => {
  it.each(ROLES.map((r) => [r]))('%s: the Add parcel button follows the Parcels permission', async (role) => {
    open('/projects/p-det', role)
    await screen.findByRole('heading', { name: /Premium Hub/ })

    expect(!!screen.queryByRole('button', { name: 'Add parcel' })).toBe(canCreateParcel(role))
  })

  it('opens the parcel form for this project, pre-filled, and can be cancelled', async () => {
    const { user } = open('/projects/p-det')
    await user.click(await screen.findByRole('button', { name: 'Add parcel' }))

    const panel = screen.getByRole('region', { name: 'Add a parcel to this project' })
    expect(panel).toHaveTextContent('Add a parcel to ALR-PPhase 1-029')
    const project = within(panel).getByLabelText(/^Project/) as HTMLSelectElement
    await waitFor(() => expect(project.value).toBe('p-det'))
    await user.click(within(panel).getByRole('button', { name: 'Cancel' }))
    expect(screen.queryByRole('region', { name: 'Add a parcel to this project' })).not.toBeInTheDocument()
  })
})
