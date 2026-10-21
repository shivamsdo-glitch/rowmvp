import { beforeEach, describe, expect, it } from 'vitest'
import { screen, within } from '@testing-library/react'
import userEventModule from '@testing-library/user-event'
import { permissionRowsFor } from '@/test/contract'
import { fakeAuthFetch } from '@/test/fake-api'
import { sampleDashboard, sampleProgram, sampleProject, sampleProjectDashboard } from '@/test/fake-dashboard'
import { renderApp } from '@/test/render-app'
import { compactMoney } from './ProgramView'

beforeEach(() => sessionStorage.clear())

function open(role: string, opts: Parameters<typeof fakeAuthFetch>[0] = {}) {
  return renderApp({
    path: '/', signedIn: true,
    fetchImpl: fakeAuthFetch({ rows: permissionRowsFor(role), role, dashboard: sampleDashboard({ isManager: role === 'ProjectManager' || role === 'Admin', roles: [role] }), ...opts }) as typeof fetch,
  })
}

describe('compactMoney', () => {
  it.each([[900, '$900'], [1_000, '$1K'], [850_000, '$850K'], [1_250_000, '$1.3M'], [12_400_000, '$12M'], [0, '$0']])('%s -> %s', (v, text) => expect(compactMoney(v)).toBe(text))
})

describe('AT-X-61 the program view for a ROW Director', () => {
  it('opens on the Program tab for a manager, with the tiles that say where the program stands', async () => {
    open('ProjectManager')

    expect(await screen.findByTestId('tile-Projects')).toHaveTextContent('2')
    expect(screen.getByTestId('tile-Active parcels')).toHaveTextContent('7')
    expect(screen.getByTestId('tile-Parcels closed')).toHaveTextContent('28%')
    expect(screen.getByTestId('tile-Projects needing attention')).toHaveTextContent('1')
    expect(screen.getByTestId('tile-Parcels stuck on a step')).toHaveTextContent('1')
    expect(screen.getByTestId('tile-Estimated ROW cost')).toHaveTextContent('$1.3M')
    expect(screen.getByTestId('tile-Known cost to date')).toHaveTextContent('$840K')
  })

  it('lists every project with its health and the reason, how much is closed, where the parcels are and the money', async () => {
    open('Admin')

    const table = await screen.findByRole('table', { name: 'Projects' })
    const red = within(table).getByRole('row', { name: /P-2026-001/ })
    expect(within(red).getByText('Off track')).toBeInTheDocument()
    expect(red).toHaveTextContent('The ROW clear date (2026-09-01) has passed with 3 parcels not closed.')
    expect(red).toHaveTextContent('(+1 more)')
    expect(within(red).getByRole('img', { name: '25% closed' })).toBeInTheDocument()
    expect(within(red).getByRole('img', { name: /Parcels by step: Parcel set up 1, Title & survey 1, Appraisal & just compensation 1, Closing 1/ })).toBeInTheDocument()
    expect(red).toHaveTextContent('$500,000')
    expect(red).toHaveTextContent('$620,000')
    expect(red).toHaveTextContent('50 days ago')
    const green = within(table).getByRole('row', { name: /P-2026-002/ })
    expect(within(green).getByText('On track')).toBeInTheDocument()
    expect(green).toHaveTextContent('Main St Bridge')
  })

  it('shows where the time goes by team, longest first, with who is stuck, and the slowest parcels with a link', async () => {
    open('ProjectManager')

    const bars = await screen.findByRole('list', { name: 'Average days a parcel stays with each team' })
    const teams = within(bars).getAllByRole('listitem')
    expect(teams.map((t) => t.getAttribute('data-testid'))).toEqual(['team-Appraisal', 'team-Negotiation', 'team-Title & Survey'])
    expect(teams[0]).toHaveTextContent('43 days avg')
    expect(teams[0]).toHaveTextContent('1 over 30 days')
    const stuck = screen.getByRole('list', { name: 'Parcels stuck longest' })
    expect(within(stuck).getByRole('link', { name: /P-2026-001 · Parcel 1004/ })).toHaveAttribute('href', '/parcels/pc1')
    expect(stuck).toHaveTextContent('61 days')
    const stages = screen.getByRole('table', { name: 'Time in each workflow step' })
    expect(within(stages).getAllByRole('row')).toHaveLength(8)
    expect(within(stages).getByRole('row', { name: /Appraisal & just compensation/ })).toHaveTextContent('Appraisal')
  })

  it('says so when nothing is stuck and when there are no projects or workflow history yet', async () => {
    open('Admin', { program: sampleProgram({ projects: [], stuck: [], teams: [] }) })

    expect(await screen.findByText(/No projects yet/)).toBeInTheDocument()
    expect(screen.getByText(/No parcel has been on one step for more than 30 days/)).toBeInTheDocument()
    expect(screen.getByText(/No parcels have been through the workflow yet/)).toBeInTheDocument()
  })

  it('opens a project to show its parcels with step, money, closing and what needs attention, and goes back', async () => {
    const user = userEventModule.setup()
    open('ProjectManager')

    await user.click(await screen.findByRole('button', { name: 'Open project P-2026-001' }))

    expect(await screen.findByRole('heading', { name: /P-2026-001 · US-90 Widening/ })).toBeInTheDocument()
    expect(screen.getByRole('list', { name: 'Why this project needs attention' })).toHaveTextContent('over the estimate')
    expect(screen.getByTestId('tile-Closed')).toHaveTextContent('25%')
    expect(screen.getByTestId('tile-Known cost')).toHaveTextContent('$620K')
    expect(screen.getByTestId('tile-Estimate')).toHaveTextContent('$500K')
    expect(screen.getByTestId('step-title_survey')).toHaveTextContent('1')
    expect(screen.getByTestId('step-offer')).toHaveTextContent('0')

    const parcels = screen.getByRole('table', { name: 'Parcels in this project' })
    const first = within(parcels).getByRole('row', { name: /1001/ })
    expect(within(first).getByRole('link', { name: 'Open parcel 1001' })).toHaveAttribute('href', '/parcels/pc1')
    expect(first).toHaveTextContent('Ada Owner')
    expect(first).toHaveTextContent('Appraisal & just compensation')
    expect(first).toHaveTextContent('$102,500')
    expect(first).toHaveTextContent('$90,000')
    expect(first).toHaveTextContent('Overdue deadline')
    expect(first).toHaveTextContent('61 days on this step')
    const closed = within(parcels).getByRole('row', { name: /1002/ })
    expect(closed).toHaveTextContent('$95,000')
    expect(closed).toHaveTextContent('Closed')
    expect(within(parcels).getByRole('row', { name: /1003/ })).toHaveTextContent('Due Dec 1, 2026')
    expect(screen.getByRole('table', { name: 'Time in each workflow step for this project' })).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: /All projects/ }))
    expect(await screen.findByRole('table', { name: 'Projects' })).toBeInTheDocument()
  })

  it('shows a project with no parcels plainly', async () => {
    const user = userEventModule.setup()
    open('Admin', { project: sampleProjectDashboard({ project: sampleProject({ parcels: 0, closed: 0, percentClosed: 0, health: 'None', reasons: [], byStep: [] }), parcels: [] }) })

    await user.click(await screen.findByRole('button', { name: 'Open project P-2026-001' }))

    expect(await screen.findByText('This project has no parcels yet.')).toBeInTheDocument()
    expect(screen.getAllByText('No parcels').length).toBeGreaterThan(0)
  })

  it('shows an error with a retry when the program cannot be loaded', async () => {
    open('Admin', { program: 'fails' })

    expect(await screen.findByText('The program view could not be loaded')).toBeInTheDocument()
  })

  it('shows an error with a retry when a project cannot be loaded', async () => {
    const user = userEventModule.setup()
    open('Admin', { project: 'fails' })

    await user.click(await screen.findByRole('button', { name: 'Open project P-2026-001' }))

    expect(await screen.findByText('The project could not be loaded')).toBeInTheDocument()
  })

  it('gives everyone who is not a manager no Program tab and never opens the program view', async () => {
    open('Negotiator')

    await screen.findByRole('list', { name: 'Work waiting for you' })
    expect(screen.queryByRole('tab', { name: 'Program' })).not.toBeInTheDocument()
    expect(screen.queryByTestId('tile-Active parcels')).not.toBeInTheDocument()
  })

  it('lets a manager switch between Program, My work and Team', async () => {
    const user = userEventModule.setup()
    open('Admin')

    await user.click(await screen.findByRole('tab', { name: 'My work' }))
    expect(await screen.findByRole('list', { name: 'Work waiting for you' })).toBeInTheDocument()
    await user.click(screen.getByRole('tab', { name: 'Team' }))
    expect(await screen.findByRole('table', { name: 'Team members' })).toBeInTheDocument()
    await user.click(screen.getByRole('tab', { name: 'Program' }))
    expect(await screen.findByRole('table', { name: 'Projects' })).toBeInTheDocument()
  })
})
