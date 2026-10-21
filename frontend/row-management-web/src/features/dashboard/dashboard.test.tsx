import { beforeEach, describe, expect, it } from 'vitest'
import { screen, within } from '@testing-library/react'
import userEventModule from '@testing-library/user-event'
import { permissionRowsFor } from '@/test/contract'
import { fakeAuthFetch } from '@/test/fake-api'
import { emptyDashboard, sampleDashboard, sampleTeam } from '@/test/fake-dashboard'
import { renderApp } from '@/test/render-app'

beforeEach(() => sessionStorage.clear())

function open(opts: Parameters<typeof fakeAuthFetch>[0]) {
  return renderApp({ path: '/', fetchImpl: fakeAuthFetch({ rows: permissionRowsFor(opts.role ?? 'Negotiator'), ...opts }) as typeof fetch, signedIn: true })
}

describe('AT-X-58 my dashboard', () => {
  it('shows what is waiting, with a count and a link to the screen where it is done, and hides lines with nothing behind them', async () => {
    open({ role: 'Negotiator', dashboard: sampleDashboard() })

    const list = await screen.findByRole('list', { name: 'Work waiting for you' })
    const decide = within(list).getByRole('link', { name: /Acquisition requests waiting for your decision/ })
    expect(decide).toHaveAttribute('href', '/requests')
    expect(within(decide).getByTestId('pending-requests-to-decide')).toHaveTextContent('3')
    expect(within(list).getByRole('link', { name: /Parcel deadlines that are overdue/ })).toHaveAttribute('href', '/parcels')
    expect(within(list).queryByText(/staff reports still in draft/)).not.toBeInTheDocument()
  })

  it('shows what I did: tiles, a seven-day chart with a text summary, my work by record and my recent activity', async () => {
    open({ role: 'Negotiator', dashboard: sampleDashboard() })

    expect(await screen.findByTestId('tile-Done in 30 days')).toHaveTextContent('11')
    expect(screen.getByText('7 added · 4 changed · 0 removed')).toBeInTheDocument()
    expect(screen.getByTestId('tile-Needs your attention')).toHaveTextContent('5')
    expect(screen.getByTestId('tile-Overdue deadlines')).toHaveTextContent('2')
    const chart = screen.getByRole('img', { name: /Your actions per day: .*\b5\b/ })
    expect(within(chart).getAllByTestId(/^day-/)).toHaveLength(7)
    const table = screen.getByRole('table', { name: 'Your work by kind of record' })
    expect(within(table).getByText('Parcel contact log entry')).toBeInTheDocument()
    expect(screen.getByRole('list', { name: 'Your recent activity' })).toHaveTextContent('Added parcel contact log entry')
    expect(screen.getByTestId('tile-Last active')).toHaveTextContent('5m ago')
  })

  it('says so plainly when nothing is waiting and nothing has been done yet', async () => {
    open({ role: 'Negotiator', dashboard: emptyDashboard() })

    expect(await screen.findByText('Nothing is waiting for you')).toBeInTheDocument()
    expect(screen.getByText(/Nothing yet\. Open a parcel and add a record/)).toBeInTheDocument()
    expect(screen.getByText(/Nothing yet in the last 30 days/)).toBeInTheDocument()
    expect(screen.getByTestId('tile-Done in 30 days')).toHaveTextContent('0')
    expect(screen.getByTestId('tile-Last active')).toHaveTextContent('—')
  })

  it('shows an error with a retry instead of a blank page when the dashboard cannot be loaded', async () => {
    open({ role: 'Negotiator', dashboardFails: true })

    expect(await screen.findByText('Your work could not be loaded')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /Try again|Retry/i })).toBeInTheDocument()
  })

  it('keeps the page heading, so every screen reader and test can tell where it is', async () => {
    open({ role: 'Negotiator', dashboard: sampleDashboard() })

    expect(await screen.findByRole('heading', { name: 'Dashboard' })).toBeInTheDocument()
    expect(screen.getByText(/Welcome, Dev User/)).toBeInTheDocument()
  })
})

describe('AT-X-59 the manager roll-up', () => {
  it('gives a person who is not a manager no Team tab and never asks for the team numbers', async () => {
    open({ role: 'Negotiator', dashboard: sampleDashboard({ isManager: false }) })

    await screen.findByRole('list', { name: 'Work waiting for you' })
    expect(screen.queryByRole('tab', { name: 'Team' })).not.toBeInTheDocument()
  })

  it('gives a manager three tabs (Program first), and the Team tab shows tiles, parcels by status and each person', async () => {
    const user = userEventModule.setup()
    open({ role: 'ProjectManager', dashboard: sampleDashboard({ isManager: true, roles: ['ProjectManager'] }), team: sampleTeam() })

    expect(await screen.findByRole('tab', { name: 'Program' })).toHaveAttribute('aria-selected', 'true')
    await user.click(screen.getByRole('tab', { name: 'Team' }))

    expect(await screen.findByTestId('tile-Active parcels')).toHaveTextContent('31')
    expect(screen.getByTestId('tile-Overdue deadlines')).toHaveTextContent('2')
    expect(screen.getByRole('link', { name: /Awaiting a decision/ })).toHaveAttribute('href', '/requests')
    const status = screen.getByRole('list', { name: 'Parcels by status' })
    expect(within(status).getByText('Negotiation')).toBeInTheDocument()
    const people = screen.getByRole('table', { name: 'Team members' })
    const nora = within(people).getByRole('row', { name: /Nora Negotiator/ })
    expect(nora).toHaveTextContent('11')
    expect(nora).toHaveTextContent('1h ago')
    expect(within(people).getByRole('row', { name: /Dan Designer/ })).toHaveTextContent('No activity')
    expect(screen.getByRole('img', { name: /Team actions per day/ })).toBeInTheDocument()

    await user.click(screen.getByRole('tab', { name: 'My work' }))
    expect(await screen.findByRole('list', { name: 'Work waiting for you' })).toBeInTheDocument()
  })

  it('shows an error with a retry when the team view cannot be loaded', async () => {
    const user = userEventModule.setup()
    open({ role: 'Admin', dashboard: sampleDashboard({ isManager: true }), team: 'forbidden' })

    await user.click(await screen.findByRole('tab', { name: 'Team' }))

    expect(await screen.findByText('The team view could not be loaded')).toBeInTheDocument()
  })
})
