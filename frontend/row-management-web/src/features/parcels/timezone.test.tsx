import { beforeEach, describe, expect, it } from 'vitest'
import { screen, waitFor, within } from '@testing-library/react'
import userEventModule from '@testing-library/user-event'
import type { Project } from '@/api/projects'
import { permissionRowsFor } from '@/test/contract'
import { fakeParcelsApi, type FakeParcelsOptions } from '@/test/fake-parcels'
import { renderApp } from '@/test/render-app'

beforeEach(() => sessionStorage.clear())

const PROJECTS: Partial<Project>[] = [
  { id: 'p-loc', number: '2026-0002', name: 'Maple Sidewalk', oversightType: 'Local' },
  { id: 'p-guam', number: '2026-0003', name: 'Harbor Road', oversightType: 'Local', timeZoneId: 'Pacific/Guam' },
]

function open(path: string, role = 'ProjectManager', extra: Partial<FakeParcelsOptions> = {}) {
  const fake = fakeParcelsApi({
    role, rows: permissionRowsFor(role), projects: PROJECTS, agencyZone: 'America/New_York',
    parcels: [
      { id: 'pc1', projectId: 'p-loc', number: '001', address: '12 Oak St' },
      { id: 'pc2', projectId: 'p-guam', number: '001', address: '1 Pier Rd' },
    ],
    ...extra,
  })
  const user = userEventModule.setup()
  const app = renderApp({ path, fetchImpl: fake.fetch, signedIn: true })
  return { fake, user, ...app }
}

describe('The agency time zone in Configuration (ROW-073)', () => {
  it('shows the current zone and today there, saves a new one and says so', async () => {
    const { fake, user } = open('/administration', 'Admin')
    const card = await screen.findByRole('region', { name: 'Time zone' })
    const select = await within(card).findByRole('combobox', { name: 'Agency time zone' })
    expect(select).toHaveValue('America/New_York')
    expect(within(card).getByText('Oct 14, 2026')).toBeInTheDocument()
    expect(within(select).getAllByRole('option').map((o) => o.textContent)).toContain('Chamorro Time (Guam, Northern Mariana Islands)')

    await user.selectOptions(select, 'America/Chicago')
    await user.click(within(card).getByRole('button', { name: 'Save time zone' }))

    expect(await within(card).findByText('Saved.')).toBeInTheDocument()
    expect(fake.agencyZone).toBe('America/Chicago')
    expect(fake.calls.find((c) => c.key === 'PUT /api/v1/agency/time-zone')!.body).toEqual({ timeZoneId: 'America/Chicago' })
  })

  it('the save button stays out of reach of a role that cannot change settings', async () => {
    // Only the administrator opens Configuration, but the card itself must not offer an edit without the right.
    const rows = permissionRowsFor('Admin').map((r) => (r.pageKey === 'administration' ? { ...r, canUpdate: false, canCreate: false, canDelete: false } : r))
    open('/administration', 'Admin', { rows })
    const card = await screen.findByRole('region', { name: 'Time zone' })

    expect(await within(card).findByRole('combobox', { name: 'Agency time zone' })).toBeDisabled()
    expect(within(card).queryByRole('button', { name: 'Save time zone' })).not.toBeInTheDocument()
  })
})

describe("A project's time zone on its Overview tab (ROW-073)", () => {
  it('a project with no zone of its own says it follows the agency and names the zone', async () => {
    open('/projects/p-loc')

    const section = await screen.findByRole('region', { name: 'Project time zone' })
    expect(await within(section).findByText('Eastern Time (New York)', { selector: 'strong' })).toBeInTheDocument()
    expect(within(section).getByText(/follows the agency/)).toBeInTheDocument()
  })

  it('gives a project its own zone, then lets it follow the agency again', async () => {
    const { fake, user } = open('/projects/p-loc')
    const form = await screen.findByRole('form', { name: 'Set the project time zone' })
    const select = await within(form).findByLabelText('Project time zone')
    expect(within(select).getByRole('option', { name: 'Follow the agency (Eastern Time (New York))' })).toBeInTheDocument()

    await user.selectOptions(select, 'Pacific/Guam')
    await user.click(within(form).getByRole('button', { name: 'Save time zone' }))

    expect(await screen.findByText('Time zone saved.')).toBeInTheDocument()
    expect(fake.projects.find((p) => p.id === 'p-loc')!.timeZoneId).toBe('Pacific/Guam')
    const section = screen.getByRole('region', { name: 'Project time zone' })
    await waitFor(() => expect(within(section).getByText(/set for this project/)).toBeInTheDocument())
    expect(within(section).getByText('Chamorro Time (Guam, Northern Mariana Islands)', { selector: 'strong' })).toBeInTheDocument()

    await user.selectOptions(within(form).getByLabelText('Project time zone'), '')
    await user.click(within(form).getByRole('button', { name: 'Save time zone' }))
    await waitFor(() => expect(fake.projects.find((p) => p.id === 'p-loc')!.timeZoneId).toBeNull())
    await waitFor(() => expect(within(section).getByText(/follows the agency/)).toBeInTheDocument())
    expect(fake.calls.filter((c) => c.key === 'PUT /api/v1/projects/p-loc/time-zone').map((c) => c.body)).toEqual([{ timeZoneId: 'Pacific/Guam' }, { timeZoneId: null }])
  })

  it('a read-only role sees the zone but cannot change it', async () => {
    open('/projects/p-guam', 'Appraiser')

    const section = await screen.findByRole('region', { name: 'Project time zone' })
    expect(await within(section).findByText('Chamorro Time (Guam, Northern Mariana Islands)', { selector: 'strong' })).toBeInTheDocument()
    expect(screen.queryByRole('form', { name: 'Set the project time zone' })).not.toBeInTheDocument()
  })
})

describe('A parcel says which calendar its dates use (ROW-073)', () => {
  it("shows its project's zone, or the agency's when the project has none", async () => {
    const first = open('/parcels/pc1')
    const synopsis = await screen.findByRole('region', { name: 'Parcel synopsis' })
    expect(await within(synopsis).findByText(/Eastern (Daylight|Standard) Time/)).toBeInTheDocument()
    first.unmount()

    open('/parcels/pc2')
    expect(await within(await screen.findByRole('region', { name: 'Parcel synopsis' })).findByText('Chamorro Standard Time')).toBeInTheDocument()
  })

  it('repeats it beside the deadlines so nobody has to guess how days were counted', async () => {
    const { user } = open('/parcels/pc2')
    await user.click(await screen.findByRole('tab', { name: 'Workflow & milestones' }))

    expect(await screen.findByText(/Days are counted in Chamorro Standard Time\./)).toBeInTheDocument()
  })
})
