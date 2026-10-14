import { beforeEach, describe, expect, it } from 'vitest'
import { fireEvent, screen, waitFor, within } from '@testing-library/react'
import userEventModule from '@testing-library/user-event'
import { ROLES } from '@/api/auth'
import type { Project } from '@/api/projects'
import { levelFor, permissionRowsFor } from '@/test/contract'
import { fakeProjectsApi, type FakeProjects } from '@/test/fake-projects'
import { renderApp } from '@/test/render-app'
import { parseCsv, projectsFromCsv } from './ImportPanel'

beforeEach(() => sessionStorage.clear())

const canCreate = (role: string) => ['S', 'P'].includes(levelFor(role, 'projects') ?? '')
const canDelete = (role: string) => levelFor(role, 'projects') === 'P'
const byLabel = (text: string, scope: HTMLElement | Document = document) =>
  within(scope as HTMLElement).getByLabelText(new RegExp(`^${text}`))
const newProjectForm = () => screen.getByRole('form', { name: 'New project' })
const setDate = (label: string, value: string) => fireEvent.change(byLabel(label), { target: { value } })

const SEED: Partial<Project>[] = [
  { id: 'p-fed', number: '2026-0001', name: 'SR-20 Widening', region: 'North', district: 'D1', oversightType: 'Federal' },
  { id: 'p-loc', number: '2026-0002', name: 'Maple Sidewalk', region: 'South', district: 'D2', oversightType: 'Local' },
]

function open(path: string, role = 'ProjectManager', projects: Partial<Project>[] = SEED, extra: Partial<Parameters<typeof fakeProjectsApi>[0]> = {}) {
  const fake = fakeProjectsApi({ role, rows: permissionRowsFor(role), projects, ...extra })
  const user = userEventModule.setup()
  const app = renderApp({ path, fetchImpl: fake.fetch, signedIn: true })
  return { fake, user, ...app }
}

// ── CSV import parsing ───────────────────────────────────────────────────

describe('CSV parsing (ROW-001 import)', () => {
  it('handles quotes, escaped quotes, commas and newlines inside cells, and CRLF line ends', () => {
    expect(parseCsv('a,b\r\n"x, y","say ""hi"""\r\n"line\nbreak",z\r\n')).toEqual([['a', 'b'], ['x, y', 'say "hi"'], ['line\nbreak', 'z']])
  })

  it('ignores blank lines and keeps empty cells', () => {
    expect(parseCsv('a,b,c\n\n1,,3\n')).toEqual([['a', 'b', 'c'], ['1', '', '3']])
  })

  it('maps friendly headers to project fields and defaults the oversight to Local', () => {
    const { rows, problems } = projectsFromCsv('Number,Name,Region,Oversight,Funding amount\nP-1,Bridge,North,federal,1500000\n,Path,,,\n')
    expect(problems).toEqual([])
    expect(rows[0]).toMatchObject({ number: 'P-1', name: 'Bridge', region: 'North', oversightType: 'Federal', fundingAmount: 1500000 })
    expect(rows[1]).toMatchObject({ number: null, name: 'Path', oversightType: 'Local', fundingAmount: null })
  })

  it('reports unknown columns and bad oversight values, and requires a Name column and a data row', () => {
    expect(projectsFromCsv('Name,Colour\nA,blue').problems).toEqual(['Unknown column "Colour" was ignored.'])
    expect(projectsFromCsv('Name,Oversight\nA,Martian').problems).toEqual(['Row 1: "Martian" is not Federal, State or Local.'])
    expect(projectsFromCsv('Region\nNorth').problems).toEqual(['The file needs a "Name" column.'])
    expect(projectsFromCsv('Name').rows).toEqual([])
  })
})

// ── Project list ─────────────────────────────────────────────────────────

describe('Projects list (ROW-001, 003, 004)', () => {
  it('shows each project with its region, oversight and whether acquisition is open or held', async () => {
    open('/projects')

    const table = await screen.findByRole('table', { name: 'Projects' })
    const fed = within(table).getByRole('row', { name: /SR-20 Widening/ })
    expect(within(fed).getByRole('link', { name: '2026-0001' })).toHaveAttribute('href', '/projects/p-fed')
    expect(within(fed).getByText('North / D1')).toBeInTheDocument()
    expect(within(fed).getByText(/Held: NEPA decision, Acquisition authorization/)).toBeInTheDocument()
    expect(within(within(table).getByRole('row', { name: /Maple Sidewalk/ })).getByText('Open for offers')).toBeInTheDocument()
  })

  it('searches by number or name, and filters by region, district and oversight', async () => {
    const { fake, user } = open('/projects')
    await screen.findByRole('table', { name: 'Projects' })

    await user.type(screen.getByLabelText('Search projects'), 'maple')
    await waitFor(() => expect(screen.queryByText('SR-20 Widening')).not.toBeInTheDocument())
    expect(screen.getByText('Maple Sidewalk')).toBeInTheDocument()
    expect(fake.calls.some((c) => c.key.includes('search=maple'))).toBe(true)

    await user.clear(screen.getByLabelText('Search projects'))
    await user.type(screen.getByLabelText('Region'), 'North')
    await waitFor(() => expect(screen.queryByText('Maple Sidewalk')).not.toBeInTheDocument())
    await user.clear(screen.getByLabelText('Region'))
    await user.selectOptions(screen.getByLabelText('Funding oversight'), 'Local')
    await waitFor(() => expect(screen.queryByText('SR-20 Widening')).not.toBeInTheDocument())
    expect(fake.calls.some((c) => c.key.includes('oversightType=Local'))).toBe(true)
  })

  it('says so plainly when nothing matches, and when there are no projects at all', async () => {
    const first = open('/projects')
    await screen.findByRole('table', { name: 'Projects' })
    await first.user.type(screen.getByLabelText('Search projects'), 'zzz-no-match')
    expect(await screen.findByText('No projects match your filters')).toBeInTheDocument()
  })

  it.each([
    ['ProjectManager', /Create the first project/],
    ['Finance', /Projects will appear here once they are created/],
  ])('an empty list reads correctly for %s', async (role, hint) => {
    open('/projects', role, [])

    expect(await screen.findByText('No projects yet')).toBeInTheDocument()
    expect(screen.getByText(hint)).toBeInTheDocument()
  })

  it('shows an error with a working retry when the list cannot be loaded', async () => {
    const { fake, user } = open('/projects', 'ProjectManager', SEED, { listFails: true })

    expect(await screen.findByText('Projects could not be loaded')).toBeInTheDocument()
    expect(screen.getByText('An unexpected error occurred.')).toBeInTheDocument()
    const before = fake.calls.length
    await user.click(screen.getByRole('button', { name: 'Try again' }))
    await waitFor(() => expect(fake.calls.length).toBeGreaterThan(before))
  })

  it('pages through long lists', async () => {
    const many = Array.from({ length: 30 }, (_, i) => ({ id: `m${i}`, number: `M-${String(i).padStart(3, '0')}`, name: `Many ${i}` }))
    const { user } = open('/projects', 'ProjectManager', many)

    expect(await screen.findByText('Page 1 of 2')).toBeInTheDocument()
    expect(screen.getByText('30 projects')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Previous' })).toBeDisabled()
    await user.click(screen.getByRole('button', { name: 'Next' }))
    expect(await screen.findByText('Page 2 of 2')).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'M-029' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Next' })).toBeDisabled()
  })

  it.each(ROLES.map((r) => [r]))('%s sees the create and import buttons exactly when their role can create projects', async (role) => {
    open('/projects', role)
    await screen.findByRole('table', { name: 'Projects' })

    for (const name of ['New project', 'Import CSV']) {
      if (canCreate(role)) expect(screen.getByRole('button', { name })).toBeInTheDocument()
      else expect(screen.queryByRole('button', { name })).not.toBeInTheDocument()
    }
  })

  it('a role that cannot read projects is told they have no access', async () => {
    const fake = fakeProjectsApi({ role: 'Finance', rows: permissionRowsFor('Finance').map((r) => (r.pageKey === 'projects' ? { ...r, canRead: false } : r)) })
    renderApp({ path: '/projects', fetchImpl: fake.fetch, signedIn: true })

    expect(await screen.findByText(/don't have access/i)).toBeInTheDocument()
  })
})

// ── Create ───────────────────────────────────────────────────────────────

describe('Creating a project (ROW-001)', () => {
  it('asks for a name before sending anything', async () => {
    const { fake, user } = open('/projects')
    await user.click(await screen.findByRole('button', { name: 'New project' }))

    await user.click(screen.getByRole('button', { name: 'Create project' }))

    expect(await screen.findByText('Name is required.')).toBeInTheDocument()
    expect(fake.calls.some((c) => c.key.startsWith('POST'))).toBe(false)
  })

  it('creates a project with a generated number and opens it', async () => {
    const { fake, user } = open('/projects')
    await user.click(await screen.findByRole('button', { name: 'New project' }))

    await user.type(byLabel('Name', newProjectForm()), 'New Interchange')
    await user.type(byLabel('Region', newProjectForm()), 'East')
    await user.type(byLabel('Funding amount', newProjectForm()), '2500000')
    await user.click(screen.getByRole('button', { name: 'Create project' }))

    expect(await screen.findByRole('heading', { name: /New Interchange/ })).toBeInTheDocument()
    const post = fake.calls.find((c) => c.key === 'POST /api/v1/projects')!
    expect(post.body).toMatchObject({ number: null, name: 'New Interchange', region: 'East', fundingAmount: 2500000, oversightType: 'Federal' })
    expect(screen.getByRole('status', { name: 'Acquisition status' })).toHaveTextContent('on hold')
  })

  it('keeps a hand-typed number and explains a duplicate without losing what was typed', async () => {
    const { user } = open('/projects')
    await user.click(await screen.findByRole('button', { name: 'New project' }))

    await user.type(byLabel('Project number'), '2026-0001')
    await user.type(byLabel('Name'), 'Clashing project')
    await user.click(screen.getByRole('button', { name: 'Create project' }))

    expect(await screen.findByText("A project numbered '2026-0001' already exists.")).toBeInTheDocument()
    expect(byLabel('Name')).toHaveValue('Clashing project')
    expect(screen.getByRole('button', { name: 'Create project' })).toBeEnabled()
  })

  it("shows the server's field-level problems next to the field", async () => {
    const { user } = open('/projects')
    await user.click(await screen.findByRole('button', { name: 'New project' }))

    await user.type(byLabel('Name'), 'Dates backwards')
    setDate('Construction start', '2027-05-01')
    setDate('Construction end', '2027-04-01')
    await user.click(screen.getByRole('button', { name: 'Create project' }))

    expect(await screen.findByText('Construction end cannot be before construction start.')).toBeInTheDocument()
    expect(screen.getByText('Please correct the highlighted fields.')).toBeInTheDocument()
    expect(byLabel('Construction end')).toHaveAttribute('aria-invalid', 'true')
  })

  it('cancel closes the form without sending anything', async () => {
    const { fake, user } = open('/projects')
    await user.click(await screen.findByRole('button', { name: 'New project' }))

    await user.click(screen.getByRole('button', { name: 'Cancel' }))

    expect(screen.queryByRole('form', { name: 'New project' })).not.toBeInTheDocument()
    expect(fake.calls.some((c) => c.key.startsWith('POST'))).toBe(false)
  })
})

// ── Import ───────────────────────────────────────────────────────────────

describe('Importing projects from CSV (ROW-001)', () => {
  const csv = (text: string) => new File([text], 'projects.csv', { type: 'text/csv' })

  it('previews the file, imports the good rows and lists the bad ones', async () => {
    const { fake, user } = open('/projects')
    await user.click(await screen.findByRole('button', { name: 'Import CSV' }))

    await user.upload(screen.getByLabelText('CSV file'), csv('Number,Name,Oversight\nIMP-1,Imported one,State\n2026-0001,Clashes,Local\n,Generated,Local\n'))
    expect(await screen.findByText('3 projects ready to import.')).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Import' }))

    expect(await screen.findByText('2 created, 1 failed.')).toBeInTheDocument()
    const failures = screen.getByRole('table', { name: 'Rows that could not be imported' })
    expect(within(failures).getByText("A project numbered '2026-0001' already exists.")).toBeInTheDocument()
    expect(fake.projects.map((p) => p.number)).toContain('IMP-1')
    // The list behind it refreshed.
    await user.click(screen.getByRole('button', { name: 'Done' }))
    expect(await screen.findByText('Imported one')).toBeInTheDocument()
  })

  it('refuses a file with no usable rows and explains why', async () => {
    const { user } = open('/projects')
    await user.click(await screen.findByRole('button', { name: 'Import CSV' }))

    await user.upload(screen.getByLabelText('CSV file'), csv('Region\nNorth\n'))

    expect(await screen.findByText('The file needs a "Name" column.')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Import' })).toBeDisabled()
  })
})

// ── Detail ───────────────────────────────────────────────────────────────

describe('Project detail', () => {
  it('shows the facts, the breadcrumb and why offers are on hold', async () => {
    open('/projects/p-fed', 'ProjectManager', [{ ...SEED[0], fundingSource: 'FHWA', fundingAmount: 12000000, description: 'Widening the corridor.' }])

    expect(await screen.findByRole('heading', { name: '2026-0001 · SR-20 Widening' })).toBeInTheDocument()
    expect(screen.getByRole('navigation', { name: 'Breadcrumb' })).toHaveTextContent('Projects')
    expect(screen.getByRole('status', { name: 'Acquisition status' })).toHaveTextContent('Missing: NEPA decision and Acquisition authorization')
    expect(screen.getByText('$12,000,000')).toBeInTheDocument()
    expect(screen.getByText('Widening the corridor.')).toBeInTheDocument()
  })

  it('says the project cannot be opened when it does not exist', async () => {
    open('/projects/nope', 'ProjectManager', SEED)

    expect(await screen.findByText('This project could not be opened')).toBeInTheDocument()
  })

  it('walks a project from held to open: NEPA decision, a preliminary then an acquisition authorization', async () => {
    const { user } = open('/projects/p-fed')
    const banner = await screen.findByRole('status', { name: 'Acquisition status' })
    expect(banner).toHaveTextContent('on hold')

    await user.click(screen.getByRole('tab', { name: 'Clearance' }))
    await user.selectOptions(screen.getByLabelText('Decision type'), 'CategoricalExclusion')
    await user.click(screen.getByRole('button', { name: 'Save NEPA decision' }))
    expect(await screen.findByText('A decision date is required.')).toBeInTheDocument()
    setDate('Decision date', '2026-01-10')
    await user.click(screen.getByRole('button', { name: 'Save NEPA decision' }))
    expect(await screen.findByText('NEPA decision saved.')).toBeInTheDocument()
    await waitFor(() => expect(screen.getByRole('status', { name: 'Acquisition status' })).toHaveTextContent('Missing: Acquisition authorization'))

    await user.click(screen.getByRole('tab', { name: 'ROW authorizations' }))
    await user.selectOptions(screen.getByLabelText(/^Authorization type/), 'Preliminary')
    await user.type(byLabel('Authorized amount'), '100000')
    setDate('Authorization date', '2026-01-20')
    await user.click(screen.getByRole('button', { name: 'Add authorization' }))
    expect(await screen.findByRole('row', { name: /Preliminary/ })).toBeInTheDocument()
    expect(screen.getByRole('status', { name: 'Acquisition status' })).toHaveTextContent('on hold')

    await user.selectOptions(screen.getByLabelText(/^Authorization type/), 'Acquisition')
    await user.type(byLabel('Authorized amount'), '500000')
    await user.type(byLabel('Federal share'), '80')
    setDate('Authorization date', '2026-02-01')
    await user.click(screen.getByRole('button', { name: 'Add authorization' }))

    const row = await screen.findByRole('row', { name: /Acquisition/ })
    expect(within(row).getByText('80% ($400,000)')).toBeInTheDocument()
    await waitFor(() => expect(screen.getByRole('status', { name: 'Acquisition status' })).toHaveTextContent('Offers and negotiations are open'))

    // Taking the acquisition authorization away holds the project again.
    await user.click(screen.getByRole('button', { name: 'Delete Acquisition authorization' }))
    await waitFor(() => expect(screen.getByRole('status', { name: 'Acquisition status' })).toHaveTextContent('on hold'))
  })

  it('needs a date and a reason for an exception, then opens the project and can withdraw it', async () => {
    const { user } = open('/projects/p-fed')
    await user.click(await screen.findByRole('tab', { name: 'Clearance' }))

    await user.selectOptions(screen.getByLabelText('Exception type'), 'Hardship')
    await user.click(screen.getByRole('button', { name: 'Save exception' }))
    expect(await screen.findByText('An approval date is required.')).toBeInTheDocument()
    expect(screen.getByText('A reason is required for an exception.')).toBeInTheDocument()

    setDate('Approval date', '2026-03-01')
    await user.type(byLabel('Reason'), 'Owner medical hardship')
    await user.click(screen.getByRole('button', { name: 'Save exception' }))
    expect(await screen.findByText('Exception saved.')).toBeInTheDocument()
    await waitFor(() => expect(screen.getByRole('status', { name: 'Acquisition status' })).toHaveTextContent('hardship acquisition exception is approved'))

    await user.selectOptions(screen.getByLabelText('Exception type'), '')
    await user.click(screen.getByRole('button', { name: 'Save exception' }))
    await waitFor(() => expect(screen.getByRole('status', { name: 'Acquisition status' })).toHaveTextContent('on hold'))
  })

  it('keeps every estimate version, newest first, and marks the current one', async () => {
    const { user } = open('/projects/p-fed')
    await user.click(await screen.findByRole('tab', { name: 'Cost estimates' }))
    expect(await screen.findByText('No estimate yet')).toBeInTheDocument()

    for (const [land, note] of [['1000', 'first'], ['2000', 'revised']] as const) {
      await user.click(screen.getByRole('button', { name: 'Add estimate version' }))
      await user.type(byLabel('Land'), land)
      await user.type(byLabel('Improvements'), '100')
      await user.type(byLabel('Notes'), note)
      await user.click(screen.getByRole('button', { name: 'Save new version' }))
      await screen.findByRole('row', { name: new RegExp(`v${land === '1000' ? 1 : 2}`) })
    }

    const rows = within(screen.getByRole('table', { name: 'Cost estimate versions' })).getAllByRole('row').slice(1)
    expect(rows).toHaveLength(2)
    expect(rows[0]).toHaveTextContent('v2')
    expect(rows[0]).toHaveTextContent('Current')
    expect(rows[0]).toHaveTextContent('$2,100')
    expect(rows[1]).toHaveTextContent('v1')
    expect(rows[1]).toHaveTextContent('$1,100')
    expect(rows[1]).not.toHaveTextContent('Current')
  })

  it('sets and adjusts a fiscal-year budget, totals them, and validates the year', async () => {
    const { user } = open('/projects/p-fed')
    await user.click(await screen.findByRole('tab', { name: 'Budget' }))

    await user.type(byLabel('Fiscal year'), '27')
    await user.click(screen.getByRole('button', { name: 'Set budget' }))
    expect(await screen.findByText('Enter a four-digit fiscal year.')).toBeInTheDocument()
    expect(screen.getByText('Enter the budget amount.')).toBeInTheDocument()

    await user.clear(byLabel('Fiscal year'))
    await user.type(byLabel('Fiscal year'), '2027')
    await user.type(screen.getByLabelText(/^Budget \(\$\)/), '1000')
    await user.click(screen.getByRole('button', { name: 'Set budget' }))
    await screen.findByRole('row', { name: /FY2027/ })

    await user.type(byLabel('Fiscal year'), '2027')
    await user.type(screen.getByLabelText(/^Budget \(\$\)/), '1500')
    await user.click(screen.getByRole('button', { name: 'Set budget' }))
    await waitFor(() => expect(screen.getByRole('row', { name: /FY2027/ })).toHaveTextContent('$1,500'))
    expect(screen.getAllByRole('row', { name: /FY\d{4}/ })).toHaveLength(1)
    expect(screen.getByText(/Total budgeted/)).toHaveTextContent('$1,500')

    await user.click(screen.getByRole('button', { name: 'Delete FY2027 budget' }))
    expect(await screen.findByText('No budget yet')).toBeInTheDocument()
  })

  it('edits the project record and shows the change', async () => {
    const { fake, user } = open('/projects/p-fed')
    await user.click(await screen.findByRole('button', { name: 'Edit project' }))

    await user.clear(byLabel('Name'))
    await user.type(byLabel('Name'), 'SR-20 Widening Phase 2')
    await user.click(screen.getByRole('button', { name: 'Save changes' }))

    expect(await screen.findByRole('heading', { name: '2026-0001 · SR-20 Widening Phase 2' })).toBeInTheDocument()
    expect(fake.calls.find((c) => c.key.startsWith('PUT /api/v1/projects/p-fed'))!.body.name).toBe('SR-20 Widening Phase 2')
  })

  it.each(ROLES.map((r) => [r]))('%s gets edit controls exactly where their role allows', async (role) => {
    const { user } = open('/projects/p-fed', role)
    await screen.findByRole('heading', { name: /SR-20 Widening/ })

    expect(!!screen.queryByRole('button', { name: 'Edit project' })).toBe(levelFor(role, 'projects') !== 'B')
    await user.click(screen.getByRole('tab', { name: 'Cost estimates' }))
    await screen.findByText('No estimate yet')
    expect(!!screen.queryByRole('button', { name: 'Add estimate version' })).toBe(levelFor(role, 'projects') !== 'B')

    await user.click(screen.getByRole('tab', { name: 'Budget' }))
    await screen.findByText('No budget yet')
    expect(!!screen.queryByRole('form', { name: 'Set a fiscal-year budget' })).toBe(levelFor(role, 'projects') !== 'B')

    await user.click(screen.getByRole('tab', { name: 'Clearance' }))
    expect(!!screen.queryByRole('button', { name: 'Save NEPA decision' })).toBe(levelFor(role, 'projects') !== 'B')
  })

  const BUDGET = { 'p-fed': [{ id: 'b1', fiscalYear: 2027, amount: 1000, notes: null }] }
  const AUTH = { 'p-fed': [{ id: 'a1', type: 'Acquisition' as const, authorizedAmount: 500, federalSharePercent: null, federalShareAmount: null, authorizedOn: '2026-02-01', referenceNumber: null, programmingConfirmed: false, notes: null }] }

  it.each(ROLES.map((r) => [r]))('%s sees delete buttons on budgets and authorizations exactly when their role can delete', async (role) => {
    const { user } = open('/projects/p-fed', role, SEED, { budgets: BUDGET, authorizations: AUTH })
    await user.click(await screen.findByRole('tab', { name: 'Budget' }))
    await screen.findByRole('row', { name: /FY2027/ })
    expect(!!screen.queryByRole('button', { name: 'Delete FY2027 budget' })).toBe(canDelete(role))

    await user.click(screen.getByRole('tab', { name: 'ROW authorizations' }))
    await screen.findByRole('row', { name: /Acquisition/ })
    expect(!!screen.queryByRole('button', { name: 'Delete Acquisition authorization' })).toBe(canDelete(role))
  })

  it('a role that may update but not delete can add but never sees delete', async () => {
    const rows = permissionRowsFor('ProjectManager').map((r) => (r.pageKey === 'projects' ? { ...r, canDelete: false } : r))
    const { user } = open('/projects/p-fed', 'ProjectManager', SEED, { rows, budgets: BUDGET })
    await user.click(await screen.findByRole('tab', { name: 'Budget' }))
    await screen.findByRole('row', { name: /FY2027/ })

    expect(screen.getByRole('form', { name: 'Set a fiscal-year budget' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /^Delete/ })).not.toBeInTheDocument()
  })
})

// ── Administration: project number format (ROW-001) ──────────────────────

describe('Project number format in Configuration', () => {
  it('lets an administrator change the pattern and shows the next number', async () => {
    const { fake, user } = open('/administration', 'Admin')
    const field = await screen.findByLabelText('Number pattern')
    expect(field).toHaveValue('{FY}-{SEQ:4}')

    await user.clear(field)
    await user.type(field, 'DOT-{{FY}-{{SEQ:3}')
    await user.selectOptions(screen.getByLabelText('Fiscal year starts in'), '10')
    await user.click(screen.getByRole('button', { name: 'Save format' }))

    expect(await screen.findByRole('status')).toHaveTextContent('Saved.')
    await waitFor(() => expect(screen.getByText(/Next project number/)).toHaveTextContent('DOT-2026-001'))
    expect(fake.calls.find((c) => c.key === 'PUT /api/v1/projects/number-settings')!.body).toEqual({ pattern: 'DOT-{FY}-{SEQ:3}', fiscalYearStartMonth: 10 })
  })

  it('shows the validation message for a pattern the server rejects, and keeps what was typed', async () => {
    const { user } = open('/administration', 'Admin')
    const field = await screen.findByLabelText('Number pattern')

    await user.clear(field)
    await user.type(field, 'no-sequence')
    await user.click(screen.getByRole('button', { name: 'Save format' }))

    expect(await screen.findAllByText('The pattern must contain exactly one {SEQ:n} token.')).not.toHaveLength(0)
    expect(field).toHaveValue('no-sequence')
  })

  it('is not reachable for a project manager', async () => {
    open('/administration', 'ProjectManager')

    expect(await screen.findByText(/don't have access/i)).toBeInTheDocument()
    expect(screen.queryByLabelText('Number pattern')).not.toBeInTheDocument()
  })
})

export type { FakeProjects }
