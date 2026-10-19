import { beforeEach, describe, expect, it } from 'vitest'
import { screen, waitFor, within } from '@testing-library/react'
import userEventModule from '@testing-library/user-event'
import { PAGE_KEYS, ROLES } from '@/api/auth'
import { levelFor, permissionRowsFor } from '@/test/contract'
import { fakeSettingsApi } from '@/test/fake-settings'
import { renderApp } from '@/test/render-app'

beforeEach(() => sessionStorage.clear())

function open(role = 'Admin') {
  const fake = fakeSettingsApi({ role, rows: permissionRowsFor(role) })
  const user = userEventModule.setup()
  const app = renderApp({ path: '/settings', fetchImpl: fake.fetch, signedIn: true })
  return { fake, user, ...app }
}

describe('Settings access (AT-AD-30)', () => {
  it.each(ROLES.map((r) => [r]))('%s %s open Settings', async (role) => {
    open(role)
    if (levelFor(role, 'administration') !== null) {
      expect(await screen.findByRole('heading', { name: 'Settings' })).toBeInTheDocument()
    } else {
      expect(await screen.findByText("You don't have access to that page")).toBeInTheDocument()
      expect(screen.queryByRole('heading', { name: 'Settings' })).not.toBeInTheDocument()
    }
  })
})

describe('Users & Roles (AT-AD-31, 32, 34, 35, 36)', () => {
  it('lists the users and the eleven roles with who is in each', async () => {
    open()

    const users = await screen.findByRole('table', { name: 'Users' })
    expect(within(users).getAllByRole('row')).toHaveLength(ROLES.length + 1)
    expect(within(users).getByRole('row', { name: /designapprover/i })).toBeInTheDocument()
    const roles = await screen.findByRole('table', { name: 'Roles' })
    expect(within(roles).getAllByRole('row')).toHaveLength(ROLES.length + 1)
    expect(within(roles).getByRole('row', { name: /Admin/ })).toHaveTextContent('Locked')
    expect(within(roles).getByRole('row', { name: /Finance/ })).toHaveTextContent('Built-in')
  })

  it('creates a role that starts with no access, then it shows up in the grid', async () => {
    const { user, fake } = open()
    await user.click(await screen.findByRole('button', { name: 'New role' }))
    const form = screen.getByRole('form', { name: 'Create a role' })
    await user.click(within(form).getByRole('button', { name: 'Create role' }))
    expect(within(form).getByText('Role name is required.')).toBeInTheDocument()

    await user.type(within(form).getByLabelText(/Role name/), 'Field Reviewer')
    await user.click(within(form).getByRole('button', { name: 'Create role' }))

    expect(await within(await screen.findByRole('table', { name: 'Roles' })).findByRole('row', { name: /Field Reviewer/ })).toHaveTextContent('Custom')
    expect(fake.roles.find((r) => r.name === 'Field Reviewer')!.permissions.every((p) => !p.canRead)).toBe(true)

    await user.click(screen.getByRole('tab', { name: 'Permissions' }))
    expect(await screen.findByRole('checkbox', { name: 'Field Reviewer Read Parcels' })).not.toBeChecked()
  })

  it('says why a duplicate role name is refused', async () => {
    const { user } = open()
    await user.click(await screen.findByRole('button', { name: 'New role' }))
    const form = screen.getByRole('form', { name: 'Create a role' })
    await user.type(within(form).getByLabelText(/Role name/), 'finance')
    await user.click(within(form).getByRole('button', { name: 'Create role' }))

    expect(await within(form).findByRole('alert')).toHaveTextContent("A role named 'finance' already exists.")
  })

  it('offers Delete only on custom roles and refuses one that still has people', async () => {
    const { user, fake } = open()
    const roles = await screen.findByRole('table', { name: 'Roles' })
    expect(within(roles).queryByRole('button', { name: /Delete role Finance/ })).not.toBeInTheDocument()
    expect(within(roles).queryByRole('button', { name: /Edit role Admin/ })).not.toBeInTheDocument()

    await user.click(await screen.findByRole('button', { name: 'New role' }))
    await user.type(within(screen.getByRole('form', { name: 'Create a role' })).getByLabelText(/Role name/), 'Temp')
    await user.click(screen.getByRole('button', { name: 'Create role' }))
    const temp = fake.roles.find((r) => r.name === 'Temp')!
    fake.users.push({ id: 'u-temp', username: 'tempuser', firstName: 'T', lastName: 'U', email: 't@x.test', isActive: true, roleId: temp.id, roleName: 'Temp' })

    await user.click(await screen.findByRole('button', { name: 'Delete role Temp' }))
    expect(await screen.findByRole('alert')).toHaveTextContent("'Temp' still has users")
  })

  it('creates a user with a role, a username and a password, and validates the form first', async () => {
    const { user, fake } = open()
    await user.click(await screen.findByRole('button', { name: 'New user' }))
    const form = screen.getByRole('form', { name: 'Create a user' })
    await user.click(within(form).getByRole('button', { name: 'Create user' }))
    for (const msg of ['Username is required.', 'First name is required.', 'Last name is required.', 'Email is required.', 'Role is required.', 'Password is required.'])
      expect(within(form).getByText(msg)).toBeInTheDocument()

    await user.type(within(form).getByLabelText(/^Username/), 'jsmith')
    await user.type(within(form).getByLabelText(/^First name/), 'Jo')
    await user.type(within(form).getByLabelText(/^Last name/), 'Smith')
    await user.type(within(form).getByLabelText(/^Email/), 'jo@example.test')
    await user.type(within(form).getByLabelText(/^Password/), 'Passw0rd-abc')
    await user.selectOptions(within(form).getByLabelText(/^Role/), 'Negotiator')
    await user.click(within(form).getByRole('button', { name: 'Create user' }))

    expect(await within(await screen.findByRole('table', { name: 'Users' })).findByRole('row', { name: /jsmith/ })).toHaveTextContent('Negotiator')
    expect(fake.calls.some((c) => c.key === 'POST /api/v1/users' && c.body.username === 'jsmith' && c.body.password === 'Passw0rd-abc')).toBe(true)
  })

  it('shows a taken username and a weak password from the server on the form', async () => {
    const { user } = open()
    await user.click(await screen.findByRole('button', { name: 'New user' }))
    const form = screen.getByRole('form', { name: 'Create a user' })
    await user.type(within(form).getByLabelText(/^Username/), 'admin')
    await user.type(within(form).getByLabelText(/^First name/), 'A')
    await user.type(within(form).getByLabelText(/^Last name/), 'B')
    await user.type(within(form).getByLabelText(/^Email/), 'a@example.test')
    await user.type(within(form).getByLabelText(/^Password/), 'Passw0rd-abc')
    await user.selectOptions(within(form).getByLabelText(/^Role/), 'Finance')
    await user.click(within(form).getByRole('button', { name: 'Create user' }))

    expect(await within(form).findByRole('alert')).toHaveTextContent("The username 'admin' is taken.")
  })

  it('edits a person: new role, deactivated, and a new password', async () => {
    const { user, fake } = open()
    await user.click(await screen.findByRole('button', { name: 'Edit finance' }))
    const form = screen.getByRole('form', { name: 'Edit user' })
    await user.selectOptions(within(form).getByLabelText(/^Role/), 'Appraiser')
    await user.click(within(form).getByLabelText('Active'))
    await user.type(within(form).getByLabelText(/New password/), 'Brand-New-1')
    await user.click(within(form).getByRole('button', { name: 'Save user' }))

    await waitFor(() => expect(fake.users.find((u) => u.username === 'finance')!.roleName).toBe('Appraiser'))
    expect(fake.users.find((u) => u.username === 'finance')!.isActive).toBe(false)
    expect(fake.calls.some((c) => c.key.endsWith('/reset-password') && c.body.password === 'Brand-New-1')).toBe(true)
  })
})

describe('Permissions grid (AT-AD-33)', () => {
  async function grid(user: ReturnType<typeof userEventModule.setup>) {
    await user.click(await screen.findByRole('tab', { name: 'Permissions' }))
    return await screen.findByRole('table', { name: 'Permissions by role and module' })
  }

  it('has a row per role and Create, Read, Update, Delete boxes per module, showing the agreed defaults', async () => {
    const { user } = open()
    const table = await grid(user)

    expect(within(table).getAllByRole('checkbox')).toHaveLength(ROLES.length * PAGE_KEYS.length * 4)
    expect(within(table).getByRole('checkbox', { name: 'Surveyor Delete Survey' })).toBeChecked()
    expect(within(table).getByRole('checkbox', { name: 'Surveyor Create Projects' })).not.toBeChecked()
    expect(within(table).getByRole('checkbox', { name: 'Surveyor Read Projects' })).toBeChecked()
    expect(within(table).getByRole('checkbox', { name: 'Finance Delete Payments' })).toBeChecked()
  })

  it('locks the Admin role at full access', async () => {
    const { user } = open()
    const table = await grid(user)

    const box = within(table).getByRole('checkbox', { name: 'Admin Delete Administration' })
    expect(box).toBeChecked()
    expect(box).toBeDisabled()
  })

  it('ticking create or delete ticks read, and unticking read clears the rest', async () => {
    const { user } = open()
    const table = await grid(user)

    await user.click(within(table).getByRole('checkbox', { name: 'Designer Create Payments' }))
    expect(within(table).getByRole('checkbox', { name: 'Designer Read Payments' })).toBeChecked()
    await user.click(within(table).getByRole('checkbox', { name: 'Designer Update Payments' }))
    await user.click(within(table).getByRole('checkbox', { name: 'Designer Read Payments' }))
    for (const a of ['Create', 'Read', 'Update', 'Delete']) expect(within(table).getByRole('checkbox', { name: `Designer ${a} Payments` })).not.toBeChecked()
  })

  it('holds changes until Save, saves only the changed roles, and can discard', async () => {
    const { user, fake } = open()
    const table = await grid(user)
    const save = screen.getByRole('button', { name: 'Save permissions' })
    expect(save).toBeDisabled()

    await user.click(within(table).getByRole('checkbox', { name: 'Appraiser Read Payments' }))
    expect(save).toBeEnabled()
    await user.click(screen.getByRole('button', { name: 'Discard changes' }))
    expect(within(table).getByRole('checkbox', { name: 'Appraiser Read Payments' })).not.toBeChecked()
    expect(save).toBeDisabled()

    await user.click(within(table).getByRole('checkbox', { name: 'Appraiser Read Payments' }))
    await user.click(within(table).getByRole('checkbox', { name: 'Negotiator Delete Parcels' }))
    await user.click(save)

    expect(await screen.findByRole('status')).toHaveTextContent('Saved 2 roles')
    const puts = fake.calls.filter((c) => c.key.startsWith('PUT') && c.key.endsWith('/permissions'))
    expect(puts.map((c) => c.key).sort()).toEqual(['PUT /api/v1/roles/role-Appraiser/permissions', 'PUT /api/v1/roles/role-Negotiator/permissions'])
    expect(fake.roles.find((r) => r.name === 'Appraiser')!.permissions.find((p) => p.pageKey === 'payments')!.canRead).toBe(true)
  })
})
