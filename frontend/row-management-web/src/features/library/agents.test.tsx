import { beforeEach, describe, expect, it } from 'vitest'
import { screen, waitFor, within } from '@testing-library/react'
import userEventModule from '@testing-library/user-event'
import { ROLES } from '@/api/auth'
import { levelFor, permissionRowsFor } from '@/test/contract'
import { fakeLibraryApi } from '@/test/fake-library'
import { renderApp } from '@/test/render-app'

beforeEach(() => sessionStorage.clear())

const canChange = (role: string) => ['S', 'P'].includes(levelFor(role, 'library') ?? '')

function open(role = 'Admin') {
  const fake = fakeLibraryApi({ role, rows: permissionRowsFor(role) })
  const user = userEventModule.setup()
  const app = renderApp({ path: '/library', fetchImpl: fake.fetch, signedIn: true })
  return { fake, user, ...app }
}

describe('Library field form: library-driven (AT-AD-38)', () => {
  it('asks whether the field is library-driven; ticked makes a dropdown list, unticked offers the typed-in types', async () => {
    const { user, fake } = open()
    await user.click(await screen.findByRole('button', { name: 'New field' }))
    const form = screen.getByRole('form', { name: 'Define a field' })

    expect(within(form).getByRole('checkbox', { name: /Library-driven/ })).toBeChecked()
    expect(within(form).queryByLabelText('Field type')).not.toBeInTheDocument()
    await user.type(within(form).getByLabelText('Field name'), 'Hearing room')
    await user.click(within(form).getByRole('button', { name: 'Create field' }))
    await waitFor(() => expect(fake.fields.find((f) => f.label === 'Hearing room')?.fieldType).toBe('List'))
    expect(await screen.findByText('Library-driven dropdown')).toBeInTheDocument()

    await user.click(await screen.findByRole('button', { name: 'New field' }))
    const second = screen.getByRole('form', { name: 'Define a field' })
    await user.click(within(second).getByRole('checkbox', { name: /Library-driven/ }))
    const types = within(within(second).getByLabelText('Field type')).getAllByRole('option').map((o) => o.textContent)
    expect(types).not.toContain('List')
    expect(types).toContain('Text')
    await user.type(within(second).getByLabelText('Field name'), 'Docket number')
    await user.click(within(second).getByRole('button', { name: 'Create field' }))
    await waitFor(() => expect(fake.fields.find((f) => f.label === 'Docket number')?.fieldType).toBe('Text'))
  })
})

describe('Library agents (AT-AD-38)', () => {
  async function openAgents(role = 'Admin') {
    const app = open(role)
    await app.user.click(await screen.findByRole('button', { name: 'Agents' }))
    return app
  }

  it('lists the agents with their job, company, phone, email and whether they are active', async () => {
    await openAgents()

    const table = await screen.findByRole('table', { name: 'Agents' })
    expect(within(table).getAllByRole('row')).toHaveLength(4)
    expect(within(table).getByRole('row', { name: /Casey Surveyor/ })).toHaveTextContent('Acme Survey')
    expect(within(table).getByRole('row', { name: /Casey Surveyor/ })).toHaveTextContent('555-0100')
    expect(within(table).getByRole('checkbox', { name: 'Old Hand, Retired Job is active' })).not.toBeChecked()
  })

  it('filters by name text and by job', async () => {
    const { user } = await openAgents()
    const table = await screen.findByRole('table', { name: 'Agents' })

    await user.selectOptions(screen.getByLabelText('Agent type filter'), 'Negotiator')
    expect(within(table).queryByRole('row', { name: /Casey Surveyor/ })).not.toBeInTheDocument()
    expect(within(table).getByRole('row', { name: /Dana Negotiator/ })).toBeInTheDocument()

    await user.selectOptions(screen.getByLabelText('Agent type filter'), '')
    await user.type(screen.getByLabelText('Filter agents'), 'acme')
    expect(within(table).getAllByRole('row')).toHaveLength(2)
  })

  it('adds an agent whose job comes from the Agent type list (switched-off jobs are not offered)', async () => {
    const { user, fake } = await openAgents()
    await user.click(await screen.findByRole('button', { name: 'New agent' }))
    const form = screen.getByRole('form', { name: 'Add an agent' })
    await user.click(within(form).getByRole('button', { name: 'Add agent' }))
    expect(within(form).getByText('Agent name is required.')).toBeInTheDocument()
    expect(within(form).getByText('Agent type is required.')).toBeInTheDocument()
    const jobs = within(within(form).getByLabelText(/^Agent type/)).getAllByRole('option').map((o) => o.textContent)
    expect(jobs).toEqual(['Select one', 'Surveyor', 'Negotiator'])

    await user.type(within(form).getByLabelText(/^Agent name/), 'Riley New')
    await user.selectOptions(within(form).getByLabelText(/^Agent type/), 'Surveyor')
    await user.type(within(form).getByLabelText(/^Company/), 'Acme Survey')
    await user.click(within(form).getByRole('button', { name: 'Add agent' }))

    expect(await within(await screen.findByRole('table', { name: 'Agents' })).findByRole('row', { name: /Riley New/ })).toHaveTextContent('Surveyor')
    expect(fake.calls.some((c) => c.key === 'POST /api/v1/library/agents' && c.body.name === 'Riley New')).toBe(true)
  })

  it('says when the same person is already listed for that job and company', async () => {
    const { user } = await openAgents()
    await user.click(await screen.findByRole('button', { name: 'New agent' }))
    const form = screen.getByRole('form', { name: 'Add an agent' })
    await user.type(within(form).getByLabelText(/^Agent name/), 'casey surveyor')
    await user.selectOptions(within(form).getByLabelText(/^Agent type/), 'Surveyor')
    await user.type(within(form).getByLabelText(/^Company/), 'ACME SURVEY')
    await user.click(within(form).getByRole('button', { name: 'Add agent' }))

    expect(await within(form).findByRole('alert')).toHaveTextContent('is already listed as Surveyor')
  })

  it('switches an agent off and back on, and edits one whose job has since been switched off', async () => {
    const { user, fake } = await openAgents()
    const table = await screen.findByRole('table', { name: 'Agents' })

    await user.click(within(table).getByRole('checkbox', { name: 'Dana Negotiator, Negotiator is active' }))
    await waitFor(() => expect(fake.agents.find((a) => a.id === 'ag2')!.isActive).toBe(false))
    await user.click(within(table).getByRole('checkbox', { name: 'Dana Negotiator, Negotiator is active' }))
    await waitFor(() => expect(fake.agents.find((a) => a.id === 'ag2')!.isActive).toBe(true))

    await user.click(within(table).getByRole('button', { name: 'Edit Old Hand, Retired Job' }))
    const form = screen.getByRole('form', { name: 'Edit agent' })
    expect(within(form).getByLabelText(/^Agent type/)).toHaveValue('Retired Job')
    await user.type(within(form).getByLabelText(/^Phone number/), '555-0000')
    await user.click(within(form).getByRole('button', { name: 'Save agent' }))
    await waitFor(() => expect(fake.agents.find((a) => a.id === 'ag3')!.phone).toBe('555-0000'))
  })

  it.each(ROLES.map((r) => [r]))('%s: reads the agents; add, edit and switch-off controls follow the Library permission', async (role) => {
    await openAgents(role)
    const table = await screen.findByRole('table', { name: 'Agents' })

    if (canChange(role)) {
      expect(screen.getByRole('button', { name: 'New agent' })).toBeInTheDocument()
      expect(within(table).getByRole('button', { name: 'Edit Casey Surveyor, Surveyor' })).toBeInTheDocument()
      expect(within(table).getByRole('checkbox', { name: 'Casey Surveyor, Surveyor is active' })).toBeEnabled()
    } else {
      expect(screen.queryByRole('button', { name: 'New agent' })).not.toBeInTheDocument()
      expect(within(table).queryByRole('button', { name: /^Edit / })).not.toBeInTheDocument()
      expect(within(table).getByRole('checkbox', { name: 'Casey Surveyor, Surveyor is active' })).toBeDisabled()
    }
  })
})
