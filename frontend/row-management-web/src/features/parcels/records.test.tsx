import { beforeEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, screen, waitFor, within } from '@testing-library/react'
import userEventModule from '@testing-library/user-event'
import { ROLES } from '@/api/auth'
import type { Project } from '@/api/projects'
import { levelFor, permissionRowsFor } from '@/test/contract'
import { fakeRecordsApi, type FakeRecordsOptions } from '@/test/fake-records'
import { renderApp } from '@/test/render-app'

beforeEach(() => sessionStorage.clear())

const can = (role: string, page: string, action: 'read' | 'create' | 'delete') => {
  const level = levelFor(role, page)
  if (action === 'read') return level !== null
  if (action === 'create') return level === 'S' || level === 'P'
  return level === 'P'
}

const PROJECTS: Partial<Project>[] = [{ id: 'p1', number: '2026-0001', name: 'Harbor Bridge', oversightType: 'Local' }]

function open(role = 'ProjectManager', extra: Partial<FakeRecordsOptions> = {}, parcel = 'pc1') {
  const fake = fakeRecordsApi({
    role, rows: permissionRowsFor(role), projects: PROJECTS,
    parcels: [{ id: 'pc1', projectId: 'p1', number: '001', address: '1 Oak St' }, { id: 'pc-gone', projectId: 'p1', number: '009', state: 'Split' }],
    team: [{ parcelId: 'pc1', id: 'tm-lead', role: 'Project Lead', agentId: 'ag4', name: 'Dana Whitfield' }, { parcelId: 'pc1', id: 'tm-neg', role: 'Negotiator', agentId: 'ag1', name: 'Casey Closer', organization: 'Stateside ROW' }],
    diary: [{ parcelId: 'pc1', id: 'de1', occurredOn: '2026-10-01', contactName: 'Alex Marlowe', summary: 'Introduced the project.', outcome: 'Will call back' }],
    notes: [{ parcelId: 'pc1', id: 'nt1', text: 'Prefers email.' }],
    ...extra,
  })
  const user = userEventModule.setup()
  const app = renderApp({ path: `/parcels/${parcel}`, fetchImpl: fake.fetch, signedIn: true })
  return { fake, user, ...app }
}

const tab = async (user: ReturnType<typeof userEventModule.setup>, name: string) => user.click(await screen.findByRole('tab', { name }))

describe('Parcel team (ROW-019)', () => {
  it('lists the team by type with the Library company and contact details', async () => {
    const { user } = open()
    await tab(user, 'Summary')

    const table = await screen.findByRole('table', { name: 'Parcel team' })
    const rows = within(table).getAllByRole('row').slice(1)
    expect(rows[0]).toHaveTextContent('Negotiator')
    expect(rows[0]).toHaveTextContent('Casey Closer')
    expect(rows[0]).toHaveTextContent('Stateside ROW')
    expect(rows[1]).toHaveTextContent('Project Lead')
  })

  it('only offers the Library: a type, then the people of that type, with no name to type', async () => {
    const { user } = open()
    await tab(user, 'Summary')
    const form = await screen.findByRole('form', { name: 'Assign a team member' })

    expect(within(form).queryByLabelText(/^Name/)).not.toBeInTheDocument()
    expect(within(form).getByLabelText(/^Person/)).toBeDisabled()
    await waitFor(() => expect(within(within(form).getByLabelText(/^Type/)).getAllByRole('option').map((o) => o.textContent)).toContain('Negotiator'))
    await user.selectOptions(within(form).getByLabelText(/^Type/), 'Negotiator')
    expect(within(within(form).getByLabelText(/^Person/)).getAllByRole('option').map((o) => o.textContent)).toEqual(['Select one', 'Casey Closer (Stateside ROW)'])
  })

  it('assigns a person from the Library and refuses leaving it blank and the same person twice', async () => {
    const { user, fake } = open()
    await tab(user, 'Summary')
    const form = await screen.findByRole('form', { name: 'Assign a team member' })

    await user.click(within(form).getByRole('button', { name: 'Assign' }))
    expect(await within(form).findByText('Choose the type of person.')).toBeInTheDocument()
    await waitFor(() => expect(within(within(form).getByLabelText(/^Type/)).getAllByRole('option').map((o) => o.textContent)).toContain('Appraiser'))
    await user.selectOptions(within(form).getByLabelText(/^Type/), 'Appraiser')
    await user.click(within(form).getByRole('button', { name: 'Assign' }))
    expect(await within(form).findByText('Choose a person from the Library.')).toBeInTheDocument()
    await user.selectOptions(within(form).getByLabelText(/^Person/), 'ag5')
    await user.click(within(form).getByRole('button', { name: 'Assign' }))
    expect(await screen.findByRole('row', { name: /Allison Jackson/ })).toBeInTheDocument()
    expect(fake.team.some((t) => t.name === 'Allison Jackson' && t.role === 'Appraiser' && t.organization === 'CBRE, Inc.')).toBe(true)

    await user.selectOptions(within(form).getByLabelText(/^Type/), 'Negotiator')
    await user.selectOptions(within(form).getByLabelText(/^Person/), 'ag1')
    await user.click(within(form).getByRole('button', { name: 'Assign' }))
    expect(await within(form).findByText(/is already on this parcel as negotiator/)).toBeInTheDocument()
  })

  it('changes and removes a team member', async () => {
    const { user, fake } = open()
    await tab(user, 'Summary')
    await user.click(await screen.findByRole('button', { name: 'Edit Casey Closer, Negotiator' }))
    const form = screen.getByRole('form', { name: 'Change team member' })

    await user.type(within(form).getByLabelText(/^Notes/), 'Lead negotiator')
    await user.click(within(form).getByRole('button', { name: 'Save changes' }))
    await waitFor(() => expect(fake.team.find((t) => t.id === 'tm-neg')!.notes).toBe('Lead negotiator'))
    await user.click(await screen.findByRole('button', { name: 'Remove Casey Closer, Negotiator' }))
    await waitFor(() => expect(fake.team.some((t) => t.id === 'tm-neg')).toBe(false))
  })

  it('shows a retryable error when the team cannot be loaded', async () => {
    const { user } = open('ProjectManager', { teamFails: true })
    await tab(user, 'Summary')
    expect(await screen.findByText('The team could not be loaded')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Try again' })).toBeInTheDocument()
  })
})

describe('Negotiation diary (ROW-020)', () => {
  const diaryTab = async (role = 'Negotiator', extra: Partial<FakeRecordsOptions> = {}) => {
    const ctx = open(role, extra)
    await tab(ctx.user, 'Negotiation')
    return ctx
  }

  it('shows the entries with who, how, what was said and what was promised', async () => {
    await diaryTab()
    const table = await screen.findByRole('table', { name: 'Negotiation diary' })
    const row = within(table).getByRole('row', { name: /Alex Marlowe/ })
    expect(row).toHaveTextContent('Oct 1, 2026')
    expect(row).toHaveTextContent('Call (out)')
    expect(row).toHaveTextContent('Introduced the project.')
    expect(row).toHaveTextContent('Will call back')
    expect(row).toHaveTextContent('Dev User')
  })

  it('logs a contact, refuses missing fields, and a future date with the server\'s reason', async () => {
    const { user, fake } = await diaryTab()
    const form = await screen.findByRole('form', { name: 'Log a diary contact' })

    await user.click(within(form).getByRole('button', { name: 'Add to diary' }))
    expect(await within(form).findByText('Choose the date.')).toBeInTheDocument()
    expect(within(form).getByText('Say who was contacted.')).toBeInTheDocument()
    expect(within(form).getByText('Summary is required.')).toBeInTheDocument()

    fireEvent.change(within(form).getByLabelText(/^Date of contact/), { target: { value: '2026-12-25' } })
    await user.type(within(form).getByLabelText(/^Who was contacted/), 'Jordan Reyes')
    await user.type(within(form).getByLabelText(/^What was said/), 'Walked the frontage.')
    await user.click(within(form).getByRole('button', { name: 'Add to diary' }))
    expect(await within(form).findByText('A diary entry cannot be dated in the future.')).toBeInTheDocument()

    fireEvent.change(within(form).getByLabelText(/^Date of contact/), { target: { value: '2026-10-14' } })
    await user.selectOptions(within(form).getByLabelText(/^How/), 'Meeting')
    await user.click(within(form).getByRole('button', { name: 'Add to diary' }))
    expect(await screen.findByRole('row', { name: /Jordan Reyes/ })).toHaveTextContent('Meeting (out)')
    expect(fake.diary.some((d) => d.contactName === 'Jordan Reyes')).toBe(true)
  })

  it('removes an entry', async () => {
    const { user, fake } = await diaryTab()
    await user.click(await screen.findByRole('button', { name: /Remove the Oct 1, 2026 entry with Alex Marlowe/ }))
    await waitFor(() => expect(fake.diary).toHaveLength(0))
    expect(await screen.findByText('No contacts logged')).toBeInTheDocument()
  })

  it('prints the diary', async () => {
    const print = vi.spyOn(window, 'print').mockImplementation(() => undefined)
    const { user } = await diaryTab()
    await user.click(await screen.findByRole('button', { name: /Print diary/ }))
    expect(print).toHaveBeenCalledOnce()
    print.mockRestore()
  })

  it('is read-only for a project manager, and hidden from roles with no Negotiations access', async () => {
    const pm = await diaryTab('ProjectManager')
    await screen.findByRole('table', { name: 'Negotiation diary' })
    expect(screen.queryByRole('form', { name: 'Log a diary contact' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /Remove the/ })).not.toBeInTheDocument()
    pm.unmount()
    sessionStorage.clear()
    await diaryTab('TitleResearcher')
    expect(await screen.findByText('Your role cannot see the negotiation diary.')).toBeInTheDocument()
  })
})

describe('Parcel notes (ROW-021)', () => {
  it('lists notes with who made them and when, adds one, refuses an empty one and deletes one', async () => {
    const { user, fake } = open()
    await tab(user, 'Summary')
    expect(await screen.findByText('Prefers email.')).toBeInTheDocument()
    expect(screen.getByText(/Made by Dev User on/)).toBeInTheDocument()

    const form = screen.getByRole('form', { name: 'Add a note' })
    await user.click(within(form).getByRole('button', { name: 'Add note' }))
    expect(await within(form).findByText('Write the note first.')).toBeInTheDocument()
    await user.type(within(form).getByLabelText(/^Note/), 'Gate code is 4412.')
    await user.click(within(form).getByRole('button', { name: 'Add note' }))
    expect(await screen.findByText('Gate code is 4412.')).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: /Delete the note "Prefers email."/ }))
    await waitFor(() => expect(fake.notes.some((n) => n.id === 'nt1')).toBe(false))
  })
})

describe('Team, diary and notes follow the access spec (both directions)', () => {
  it.each(ROLES.map((r) => [r]))('%s', async (role) => {
    const { user } = open(role)
    await screen.findByRole('heading', { name: /Parcel 001/ })

    await tab(user, 'Summary')
    await screen.findByRole('table', { name: 'Parcel team' })
    expect(screen.queryByRole('form', { name: 'Assign a team member' }) !== null).toBe(can(role, 'parcels', 'create'))
    expect(screen.queryByRole('button', { name: 'Remove Casey Closer, Negotiator' }) !== null).toBe(can(role, 'parcels', 'delete'))

    await tab(user, 'Summary')
    await screen.findByText('Prefers email.')
    expect(screen.queryByRole('form', { name: 'Add a note' }) !== null).toBe(can(role, 'parcels', 'create'))
    expect(screen.queryByRole('button', { name: /Delete the note/ }) !== null).toBe(can(role, 'parcels', 'delete'))

    await tab(user, 'Negotiation')
    if (!can(role, 'negotiations', 'read')) {
      expect(await screen.findByText('Your role cannot see the negotiation diary.')).toBeInTheDocument()
      return
    }
    await screen.findByRole('table', { name: 'Negotiation diary' })
    expect(screen.queryByRole('form', { name: 'Log a diary contact' }) !== null).toBe(can(role, 'negotiations', 'create'))
    expect(screen.queryByRole('button', { name: /Remove the Oct 1/ }) !== null).toBe(can(role, 'negotiations', 'delete'))
  })

  it('a split parcel keeps its records for reading and offers no way to add or remove', async () => {
    const { user } = open('Admin', { team: [{ parcelId: 'pc-gone', name: 'Old Hand', role: 'Counsel' }], notes: [{ parcelId: 'pc-gone', text: 'Before the split.' }] }, 'pc-gone')
    await tab(user, 'Summary')
    await screen.findByRole('table', { name: 'Parcel team' })
    expect(screen.queryByRole('form', { name: 'Assign a team member' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /^Remove/ })).not.toBeInTheDocument()
    await tab(user, 'Summary')
    expect(await screen.findByText('Before the split.')).toBeInTheDocument()
    expect(screen.queryByRole('form', { name: 'Add a note' })).not.toBeInTheDocument()
    await tab(user, 'Negotiation')
    await screen.findByText('No contacts logged')
    expect(screen.queryByRole('form', { name: 'Log a diary contact' })).not.toBeInTheDocument()
  })
})
