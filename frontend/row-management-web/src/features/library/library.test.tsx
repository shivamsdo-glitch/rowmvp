import { beforeEach, describe, expect, it } from 'vitest'
import { fireEvent, screen, waitFor, within } from '@testing-library/react'
import userEventModule from '@testing-library/user-event'
import { ROLES } from '@/api/auth'
import { parseValues, valuesToCsv } from '@/api/library'
import { levelFor, permissionRowsFor } from '@/test/contract'
import { fakeLibraryApi, type FakeLibraryOptions } from '@/test/fake-library'
import { renderApp } from '@/test/render-app'

beforeEach(() => sessionStorage.clear())

const canRead = (role: string) => levelFor(role, 'administration') !== null

function open(role = 'Admin', extra: Partial<FakeLibraryOptions> = {}) {
  const fake = fakeLibraryApi({ role, rows: permissionRowsFor(role), ...extra })
  const user = userEventModule.setup()
  const app = renderApp({ path: '/library', fetchImpl: fake.fetch, signedIn: true })
  return { fake, user, ...app }
}

const fieldButton = (name: RegExp | string) => screen.findByRole('button', { name })
const valuesTable = (label: string) => screen.findByRole('table', { name: `${label} values` })

describe('Library (defaults, values and fields)', () => {
  it('lists the fields with their value counts and opens the first one', async () => {
    open()

    const nav = await screen.findByRole('navigation', { name: 'Library fields' })
    expect(within(nav).getByRole('button', { name: /Parcel status/ })).toHaveTextContent('2/2')
    expect(within(nav).getByRole('button', { name: /Possession type/ })).toHaveTextContent('3/4')
    expect(within(nav).getByRole('button', { name: /CAD number/ })).toHaveTextContent('Text')
    const table = await valuesTable('Parcel status')
    expect(within(table).getByRole('row', { name: /Inactive/ })).toHaveTextContent('Default')
  })

  it('shows another field\'s values, with inactive ones unticked', async () => {
    const { user } = open()
    await user.click(await fieldButton(/Possession type/))

    const table = await valuesTable('Possession type')
    expect(within(table).getAllByRole('row')).toHaveLength(5)
    expect(within(table).getByRole('checkbox', { name: 'PUA is active' })).toBeChecked()
    expect(within(table).getByRole('checkbox', { name: 'Judgement is active' })).not.toBeChecked()
  })

  it('says a free-input field has no values to choose', async () => {
    const { user } = open()
    await user.click(await fieldButton(/CAD number/))

    expect(await screen.findByText(/people type it in on forms/)).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'New' })).not.toBeInTheDocument()
  })

  it('adds a value, and refuses a duplicate ignoring case with the server\'s message', async () => {
    const { user, fake } = open()
    await user.click(await fieldButton(/Possession type/))
    await valuesTable('Possession type')

    await user.click(screen.getByRole('button', { name: 'New' }))
    const form = screen.getByRole('form', { name: 'Add a value' })
    await user.type(within(form).getByLabelText('New value'), 'Deed')
    await user.click(within(form).getByRole('button', { name: 'Add value' }))
    expect(await screen.findByRole('row', { name: /Deed/ })).toBeInTheDocument()
    expect(fake.fields.find((f) => f.key === 'possession_type')!.values.map((v) => v.value)).toContain('Deed')

    await user.click(screen.getByRole('button', { name: 'New' }))
    const again = screen.getByRole('form', { name: 'Add a value' })
    await user.type(within(again).getByLabelText('New value'), 'closing')
    await user.click(within(again).getByRole('button', { name: 'Add value' }))
    expect(await screen.findByText("'closing' is already in Possession type.")).toBeInTheDocument()
  })

  it('refuses an empty value without asking the server', async () => {
    const { user, fake } = open()
    await user.click(await fieldButton(/Possession type/))
    await valuesTable('Possession type')
    await user.click(screen.getByRole('button', { name: 'New' }))
    await user.click(within(screen.getByRole('form', { name: 'Add a value' })).getByRole('button', { name: 'Add value' }))

    expect(await screen.findByText('Value is required.')).toBeInTheDocument()
    expect(fake.calls.some((c) => c.key.startsWith('POST') && c.key.endsWith('/values'))).toBe(false)
  })

  it('switches a value off and on, and renames one', async () => {
    const { user, fake } = open()
    await user.click(await fieldButton(/Possession type/))
    const table = await valuesTable('Possession type')

    await user.click(within(table).getByRole('checkbox', { name: 'PUA is active' }))
    await waitFor(() => expect(fake.fields.find((f) => f.key === 'possession_type')!.values[0].isActive).toBe(false))
    await user.click(within(table).getByRole('checkbox', { name: 'Judgement is active' }))
    await waitFor(() => expect(fake.fields.find((f) => f.key === 'possession_type')!.values[3].isActive).toBe(true))

    await user.click(within(table).getByRole('button', { name: 'Rename Closing' }))
    const input = within(table).getByLabelText('Value for Closing')
    await user.clear(input)
    await user.type(input, 'Closing at title')
    await user.click(within(table).getByRole('button', { name: 'Save' }))
    expect(await within(table).findByText('Closing at title')).toBeInTheDocument()
  })

  it('will not switch off the default parcel status and says why', async () => {
    const { user } = open()
    const table = await valuesTable('Parcel status')

    await user.click(within(table).getByRole('checkbox', { name: 'Inactive is active' }))

    expect(await screen.findByText('The initial status must be active.')).toBeInTheDocument()
    expect(within(table).getByRole('checkbox', { name: 'Inactive is active' })).toBeChecked()
  })

  it('filters the values as you type', async () => {
    const { user } = open()
    await user.click(await fieldButton(/Possession type/))
    const table = await valuesTable('Possession type')

    await user.type(screen.getByLabelText('Filter Possession type'), 'dep')

    expect(within(table).getAllByRole('row')).toHaveLength(2)
    expect(within(table).getByText('Notice of Deposit')).toBeInTheDocument()
    await user.clear(screen.getByLabelText('Filter Possession type'))
    await user.type(screen.getByLabelText('Filter Possession type'), 'zzz')
    expect(await screen.findByText('No value matches your filter.')).toBeInTheDocument()
  })

  it('imports values from a file and reports what was added and what was already there', async () => {
    const { user, fake } = open()
    await user.click(await fieldButton(/Possession type/))
    await valuesTable('Possession type')

    const file = new File(['Possession type\r\npua\r\n"Deed, recorded"\r\nLease\r\n'], 'values.csv', { type: 'text/csv' })
    fireEvent.change(screen.getByLabelText('File to import'), { target: { files: [file] } })

    expect(await screen.findByText('2 added, 1 already there.')).toBeInTheDocument()
    expect(fake.fields.find((f) => f.key === 'possession_type')!.values.map((v) => v.value)).toEqual(expect.arrayContaining(['Deed, recorded', 'Lease']))
    expect(fake.calls.find((c) => c.key.endsWith('/import'))!.body.values).toEqual(['pua', 'Deed, recorded', 'Lease'])
  })

  it('refuses an empty import file', async () => {
    const { user, fake } = open()
    await user.click(await fieldButton(/Possession type/))
    await valuesTable('Possession type')

    fireEvent.change(screen.getByLabelText('File to import'), { target: { files: [new File(['Possession type\r\n\r\n'], 'empty.csv')] } })

    expect(await screen.findByText('That file has no values to import.')).toBeInTheDocument()
    expect(fake.calls.some((c) => c.key.endsWith('/import'))).toBe(false)
  })

  it('defines a new field and opens it', async () => {
    const { user, fake } = open()
    await user.click(await fieldButton('New field'))
    const form = screen.getByRole('form', { name: 'Define a field' })

    await user.click(within(form).getByRole('button', { name: 'Create field' }))
    expect(await within(form).findByText('Name is required.')).toBeInTheDocument()
    await user.type(within(form).getByLabelText(/^Field name/), 'Zone')
    await user.click(within(form).getByRole('button', { name: 'Create field' }))

    expect(await screen.findByRole('region', { name: 'Zone' })).toBeInTheDocument()
    expect(screen.getByText('No values yet')).toBeInTheDocument()
    expect(fake.fields.some((f) => f.label === 'Zone' && f.fieldType === 'List')).toBe(true)
  })

  it('refuses a field name already used', async () => {
    const { user } = open()
    await user.click(await fieldButton('New field'))
    const form = screen.getByRole('form', { name: 'Define a field' })
    await user.type(within(form).getByLabelText(/^Field name/), 'possession TYPE')
    await user.click(within(form).getByRole('button', { name: 'Create field' }))

    expect(await within(form).findByText("There is already a field named 'possession TYPE'.")).toBeInTheDocument()
  })

  it('renames a field, and locks the type of one the product uses', async () => {
    const { user, fake } = open()
    await user.click(await fieldButton(/Possession type/))
    await valuesTable('Possession type')
    await user.click(screen.getByRole('button', { name: 'Edit field' }))
    const form = screen.getByRole('form', { name: 'Edit field' })

    expect(within(form).getByLabelText(/^Field type/)).toBeDisabled()
    expect(within(form).getByText(/fixed because the product uses this field/)).toBeInTheDocument()
    await user.clear(within(form).getByLabelText(/^Field name/))
    await user.type(within(form).getByLabelText(/^Field name/), 'Possession kind')
    await user.click(within(form).getByRole('button', { name: 'Save field' }))

    expect(await screen.findByRole('region', { name: 'Possession kind' })).toBeInTheDocument()
    expect(fake.fields.find((f) => f.key === 'possession_type')!.label).toBe('Possession kind')
  })

  it('exports the values as a file', async () => {
    const { user } = open()
    await user.click(await fieldButton(/Possession type/))
    await valuesTable('Possession type')
    const created: string[] = []
    const realCreate = URL.createObjectURL
    URL.createObjectURL = (b: Blob | MediaSource) => { created.push(String((b as Blob).size)); return 'blob:x' }
    URL.revokeObjectURL = () => {}
    try {
      await user.click(screen.getByRole('button', { name: /Export/ }))
    } finally {
      URL.createObjectURL = realCreate
    }
    expect(created).toHaveLength(1)
    expect(Number(created[0])).toBeGreaterThan(20)
  })

  it('shows a retryable error when the library cannot be loaded', async () => {
    open('Admin', { listFails: true })
    expect(await screen.findByText('The library could not be loaded')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Try again' })).toBeInTheDocument()
  })

  it.each(ROLES.map((r) => [r]))('%s: the Library opens only for roles with Administration access, and the sidebar link agrees', async (role) => {
    open(role)
    if (canRead(role)) {
      expect(await screen.findByRole('heading', { name: 'Library' })).toBeInTheDocument()
      expect(screen.getByRole('link', { name: /Library/ })).toHaveAttribute('href', '/library')
      expect(await screen.findByRole('button', { name: 'New field' })).toBeInTheDocument()
    } else {
      expect(await screen.findByText(/don't have access/i)).toBeInTheDocument()
      expect(screen.queryByRole('link', { name: /Library/ })).not.toBeInTheDocument()
    }
  })

  it('a role that can read but not change sees no editing controls', async () => {
    const rows = permissionRowsFor('Admin').map((r) => (r.pageKey === 'administration' ? { ...r, canCreate: false, canUpdate: false, canDelete: false } : r))
    const { user } = open('Admin', { rows })
    await user.click(await fieldButton(/Possession type/))
    const table = await valuesTable('Possession type')

    expect(screen.queryByRole('button', { name: 'New field' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'New' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Edit field' })).not.toBeInTheDocument()
    expect(screen.queryByLabelText('File to import')).not.toBeInTheDocument()
    expect(within(table).getByRole('checkbox', { name: 'PUA is active' })).toBeDisabled()
    expect(within(table).queryByRole('button', { name: /Rename/ })).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: /Export/ })).toBeInTheDocument()
  })
})

describe('Library file helpers', () => {
  it('reads the first column, skips a heading and blanks, and handles quotes and a byte-order mark', () => {
    expect(parseValues('﻿Interest type,Is active\r\nPartial Fee,Yes\r\n\r\n"Full Fee",No\n"Easement, drainage",Yes\n', 'Interest type'))
      .toEqual(['Partial Fee', 'Full Fee', 'Easement, drainage'])
    expect(parseValues('Value\nA\nB', 'x')).toEqual(['A', 'B'])
    expect(parseValues('A\nValue', 'x')).toEqual(['A', 'Value'])
    expect(parseValues('', 'x')).toEqual([])
  })

  it('writes a quoted CSV that reads back to the same values', () => {
    const values = [{ value: 'Easement, "drainage"', isActive: true }, { value: 'Full Fee', isActive: false }]
    const csv = valuesToCsv('Interest type', values)
    expect(csv).toBe('"Interest type","Is active"\r\n"Easement, ""drainage""",Yes\r\n"Full Fee",No\r\n')
    expect(parseValues(csv, 'Interest type')).toEqual(['Easement, "drainage"', 'Full Fee'])
  })
})
