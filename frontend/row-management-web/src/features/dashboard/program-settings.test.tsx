import { beforeEach, describe, expect, it } from 'vitest'
import { screen, within } from '@testing-library/react'
import userEventModule from '@testing-library/user-event'
import type { ProgramSettingsInput } from '@/api/dashboard'
import { permissionRowsFor } from '@/test/contract'
import { fakeAuthFetch } from '@/test/fake-api'
import { renderApp } from '@/test/render-app'

beforeEach(() => sessionStorage.clear())

function open(opts: Parameters<typeof fakeAuthFetch>[0] = {}) {
  return renderApp({ path: '/administration', signedIn: true, fetchImpl: fakeAuthFetch({ rows: permissionRowsFor('Admin'), role: 'Admin', ...opts }) as typeof fetch })
}

async function card() {
  return within(await screen.findByRole('form', { name: 'Program view settings' }))
}

describe('AT-X-63 Program view settings on the Configuration page', () => {
  it('shows the current thresholds with their defaults and every step with its team and its default team', async () => {
    open()

    const form = await card()
    expect(form.getByLabelText('Days on one step before a parcel is stuck')).toHaveValue(30)
    expect(form.getByLabelText('Warn this many days before the ROW clear date')).toHaveValue(90)
    expect(form.getByLabelText('Percent closed expected inside that window')).toHaveValue(75)
    expect(form.getByLabelText(/Overdue parcels before a project is "Off track"/)).toHaveValue(3)
    expect(form.getByLabelText(/Share of the project overdue/)).toHaveValue(25)
    expect(screen.getAllByText(/Default: 30\./).length).toBeGreaterThan(0)
    const table = form.getByRole('table', { name: 'Team for each workflow step' })
    expect(within(table).getAllByRole('row')).toHaveLength(8)
    expect(form.getByLabelText('Team for Appraisal & just compensation')).toHaveValue('Appraisal')
    expect(within(table).getByRole('row', { name: /Title & survey/ })).toHaveTextContent('Title & Survey')
  })

  it('saves changed thresholds and a changed team, sending every step, and says so', async () => {
    const user = userEventModule.setup()
    let sent: ProgramSettingsInput | null = null
    open({ onSettingsSaved: (b) => { sent = b } })
    const form = await card()

    const stuck = form.getByLabelText('Days on one step before a parcel is stuck')
    await user.clear(stuck)
    await user.type(stuck, '45')
    const team = form.getByLabelText('Team for Appraisal & just compensation')
    await user.clear(team)
    await user.type(team, '  Valuation ')
    await user.click(form.getByRole('button', { name: 'Save program settings' }))

    expect(await screen.findByText('Saved. The Program view now uses these settings.')).toBeInTheDocument()
    expect(sent).toMatchObject({ stuckDays: 45, clearDateWarningDays: 90, clearDateWarningPercentClosed: 75, redOverdueMinimumParcels: 3, redOverduePercent: 25 })
    const steps = sent!.steps
    expect(steps).toHaveLength(7)
    expect(steps.find((s) => s.stepKey === 'appraisal')).toEqual({ stepKey: 'appraisal', team: 'Valuation' })
    expect(steps.find((s) => s.stepKey === 'closing')).toEqual({ stepKey: 'closing', team: 'Closing' })
    expect(await card().then((c) => c.getByLabelText('Team for Appraisal & just compensation'))).toHaveValue('Valuation')
  })

  it.each([
    ['0', 'Days on one step before a parcel is stuck', 'from 1 to 365'],
    ['366', 'Days on one step before a parcel is stuck', 'from 1 to 365'],
    ['2.5', 'Warn this many days before the ROW clear date', 'whole number'],
    ['101', 'Percent closed expected inside that window', 'from 1 to 100'],
    ['', 'Percent closed expected inside that window', 'whole number'],
  ])('refuses %j for "%s" before asking the server', async (value, label, message) => {
    const user = userEventModule.setup()
    let sent = false
    open({ onSettingsSaved: () => { sent = true } })
    const form = await card()

    const field = form.getByLabelText(label)
    await user.clear(field)
    if (value) await user.type(field, value)
    await user.click(form.getByRole('button', { name: 'Save program settings' }))

    expect(await form.findByRole('alert')).toHaveTextContent(message)
    expect(sent).toBe(false)
  })

  it('will not save a step with no team', async () => {
    const user = userEventModule.setup()
    let sent = false
    open({ onSettingsSaved: () => { sent = true } })
    const form = await card()

    await user.clear(form.getByLabelText('Team for Offer'))
    await user.click(form.getByRole('button', { name: 'Save program settings' }))

    expect(await screen.findByText('Give "Offer" a team.')).toBeInTheDocument()
    expect(sent).toBe(false)
  })

  it('shows the server message when it refuses the settings', async () => {
    const user = userEventModule.setup()
    open({ settingsRejects: 'A step is listed twice.' })
    const form = await card()

    await user.click(form.getByRole('button', { name: 'Save program settings' }))

    expect(await screen.findByText(/A step is listed twice\./)).toBeInTheDocument()
    expect(screen.queryByText('Saved. The Program view now uses these settings.')).not.toBeInTheDocument()
  })

  it('puts everything back to the defaults with Reset', async () => {
    const user = userEventModule.setup()
    open()
    const form = await card()
    const stuck = form.getByLabelText('Days on one step before a parcel is stuck')
    await user.clear(stuck)
    await user.type(stuck, '60')
    await user.click(form.getByRole('button', { name: 'Save program settings' }))
    await screen.findByText('Saved. The Program view now uses these settings.')
    expect((await card()).getByLabelText('Days on one step before a parcel is stuck')).toHaveValue(60)

    await user.click((await card()).getByRole('button', { name: 'Reset to defaults' }))

    expect(await screen.findByText('Back to the default settings.')).toBeInTheDocument()
    expect((await card()).getByLabelText('Days on one step before a parcel is stuck')).toHaveValue(30)
  })

  it('shows an error with a retry when the settings cannot be loaded', async () => {
    open({ settings: 'fails' })

    expect(await screen.findByText('The program settings could not be loaded')).toBeInTheDocument()
  })

  it('is not on the Configuration page for a person who cannot open that page', async () => {
    renderApp({ path: '/administration', signedIn: true, fetchImpl: fakeAuthFetch({ rows: permissionRowsFor('ProjectManager'), role: 'ProjectManager' }) as typeof fetch })

    expect(await screen.findByText(/don't have access/i)).toBeInTheDocument()
    expect(screen.queryByRole('form', { name: 'Program view settings' })).not.toBeInTheDocument()
  })
})
