import { beforeEach, describe, expect, it } from 'vitest'
import { screen, waitFor, within } from '@testing-library/react'
import userEventModule from '@testing-library/user-event'
import { ROLES } from '@/api/auth'
import type { Parcel } from '@/api/parcels'
import type { Project } from '@/api/projects'
import { levelFor, permissionRowsFor } from '@/test/contract'
import { fakeParcelsApi } from '@/test/fake-parcels'
import { renderApp } from '@/test/render-app'

beforeEach(() => sessionStorage.clear())

const canCreate = (role: string) => ['S', 'P'].includes(levelFor(role, 'parcels') ?? '')
const canUpdate = (role: string) => ['U', 'S', 'P'].includes(levelFor(role, 'parcels') ?? '')

const PROJECTS: Partial<Project>[] = [{ id: 'p1', number: 'ALR-PPhase 1-029', name: 'Harbor Bridge', oversightType: 'Local' }]
const PARCELS: Partial<Parcel>[] = [{
  id: 'pc1', projectId: 'p1', number: 'PN-1001', address: '1 Oak St', areaSqFt: 87120,
  cadNumber: 'CAD-77', acquisitionInterestType: 'Permanent Easement', possessionType: 'PUA', possessionDate: '2026-06-01',
  proposedPossessionDate: '2026-05-01', segment: 'Seg A', priority: 'High', parcelLocation: 'North side of Main St', isActiveParcel: true,
  interests: [{ id: 'i1', type: 'PermanentEasement', areaSqFt: 43560, notes: null }],
}]

function open(path: string, role = 'Designer') {
  const fake = fakeParcelsApi({ role, rows: permissionRowsFor(role), projects: PROJECTS, parcels: PARCELS })
  const user = userEventModule.setup()
  const app = renderApp({ path, fetchImpl: fake.fetch, signedIn: true })
  return { fake, user, ...app }
}

describe('Parcel details form (AT-DE-40)', () => {
  it('shows every details field on the parcel summary and the acres of each interest', async () => {
    open('/parcels/pc1')

    const synopsis = await screen.findByRole('region', { name: 'Parcel synopsis' })
    for (const [label, value] of [
      ['CAD number', 'CAD-77'], ['Acquisition interest type', 'Permanent Easement'], ['Possession type', 'PUA'], ['Segment', 'Seg A'],
      ['Priority', 'High'], ['Parcel location', 'North side of Main St'], ['Active parcel', 'Yes'],
    ]) expect(within(synopsis).getByText(label).closest('div')).toHaveTextContent(value)
    expect(within(synopsis).getByText('Possession date').closest('div')).toHaveTextContent('2026')
    expect(within(synopsis).getByText('Proposed possession date').closest('div')).toHaveTextContent('2026')

    await userEventModule.setup().click(screen.getByRole('tab', { name: 'Interests' }))
    const table = await screen.findByRole('table', { name: 'Interests' })
    expect(within(table).getByRole('columnheader', { name: 'Acres' })).toBeInTheDocument()
    expect(within(table).getAllByRole('row')[1]).toHaveTextContent('43,560 sq ft1')
  })

  it('creates a parcel with the details, offering the Library lists, and sends every field', async () => {
    const { fake, user } = open('/parcels')
    await user.click(await screen.findByRole('button', { name: 'New parcel' }))
    const form = await screen.findByRole('form', { name: 'New parcel' })

    await user.selectOptions(within(form).getByLabelText(/^Project/), 'p1')
    await user.type(within(form).getByLabelText(/^Area \(sq ft\)/), '4300')
    await user.type(within(form).getByLabelText(/^CAD number/), 'CAD-9')
    await waitFor(() => expect(within(within(form).getByLabelText(/^Acquisition interest type/)).getAllByRole('option').map((o) => o.textContent))
      .toEqual(['Select one', 'Permanent Easement', 'Temporary Construction Easement', 'Full Fee']))
    await user.selectOptions(within(form).getByLabelText(/^Acquisition interest type/), 'Full Fee')
    await waitFor(() => expect(within(within(form).getByLabelText(/^Possession type/)).getAllByRole('option').map((o) => o.textContent)).toEqual(['Select one', 'PUA', 'Closing']))
    await user.selectOptions(within(form).getByLabelText(/^Possession type/), 'Closing')
    await user.type(within(form).getByLabelText(/^Possession date/), '2026-07-01')
    await user.type(within(form).getByLabelText(/^Proposed possession date/), '2026-06-15')
    await user.type(within(form).getByLabelText(/^Segment/), 'S1')
    await user.type(within(form).getByLabelText(/^Priority/), 'P2')
    await user.type(within(form).getByLabelText(/^Parcel location/), 'Corner lot')
    expect(within(form).getByLabelText('Active parcel')).toBeChecked()
    await user.click(within(form).getByLabelText('Active parcel'))
    await user.click(within(form).getByRole('button', { name: 'Create parcel' }))

    await waitFor(() => expect(fake.calls.some((c) => c.key === 'POST /api/v1/parcels')).toBe(true))
    expect(fake.calls.find((c) => c.key === 'POST /api/v1/parcels')!.body).toMatchObject({
      cadNumber: 'CAD-9', acquisitionInterestType: 'Full Fee', possessionType: 'Closing', possessionDate: '2026-07-01',
      proposedPossessionDate: '2026-06-15', segment: 'S1', priority: 'P2', parcelLocation: 'Corner lot', isActiveParcel: false,
    })
  })

  it('edits the details of a parcel and keeps a value that has since left the Library list', async () => {
    const { fake, user } = open('/parcels/pc1')
    await user.click(await screen.findByRole('button', { name: 'Edit parcel' }))
    const form = await screen.findByRole('form', { name: 'Edit parcel' })

    expect(within(form).getByLabelText(/^CAD number/)).toHaveValue('CAD-77')
    expect(within(form).getByLabelText(/^Acquisition interest type/)).toHaveValue('Permanent Easement')
    await user.clear(within(form).getByLabelText(/^CAD number/))
    await user.type(within(form).getByLabelText(/^CAD number/), 'CAD-78')
    await user.clear(within(form).getByLabelText(/^Segment/))
    await user.click(within(form).getByRole('button', { name: 'Save parcel' }))

    await waitFor(() => expect(fake.calls.some((c) => c.key.startsWith('PUT /api/v1/parcels/pc1'))).toBe(true))
    expect(fake.calls.find((c) => c.key.startsWith('PUT /api/v1/parcels/pc1'))!.body).toMatchObject({
      cadNumber: 'CAD-78', segment: null, acquisitionInterestType: 'Permanent Easement', priority: 'High', isActiveParcel: true,
    })
  })

  it.each(ROLES.map((r) => [r]))('%s: New parcel and Edit parcel follow the Parcels permission', async (role) => {
    open('/parcels/pc1', role)
    await screen.findByRole('region', { name: 'Parcel synopsis' })

    expect(!!screen.queryByRole('button', { name: 'Edit parcel' })).toBe(canUpdate(role))
    expect(canCreate(role) && !canUpdate(role)).toBe(false)
  })
})
