import { expect, test, type APIRequestContext, type Browser, type Page } from '@playwright/test'
import { canRead, devToken, level, newId, ROLES, sessionToken, signInViaUi } from './support'

/**
 * Phase 1, slice 3c — rights of entry, acquisition requests, staff reports and certificates, and the project plan
 * (ROW-077, 080, 240, 241). Real browser, real UI, real API, real Postgres. Case ids match vault/acceptance/<Role>.md.
 * The plan-review gate is agency-wide, so its scenarios run on their own in gate.spec.ts (after everything else).
 * Every test makes its own project and parcel and finds its own rows by unique names.
 */

const unique = (label: string) => `${label} ${newId().slice(0, 8)}`
const allowed = (role: string, page: string, action: 'read' | 'create' | 'update' | 'delete') => {
  const l = level(role, page)
  if (action === 'update') return ['U', 'S', 'P'].includes(l ?? '')
  return action === 'read' ? l !== null : action === 'delete' ? l === 'P' : l === 'S' || l === 'P'
}
const today = (add = 0) => new Date(Date.now() + add * 86_400_000).toISOString().slice(0, 10)

type Headers = { Authorization: string }
const as = async (request: APIRequestContext, role: string): Promise<Headers> => ({ Authorization: `Bearer ${await devToken(request, role)}` })

/** A different person from anyone signed in through the login screen (they all share one stable user per role). */
const someoneElse = async (request: APIRequestContext, role: string): Promise<Headers> => ({ Authorization: `Bearer ${await devToken(request, role, { user: newId() })}` })

async function makeProject(request: APIRequestContext, headers: Headers) {
  const res = await request.post('/api/v1/projects', { headers, data: { name: unique('Acquisition scene'), oversightType: 'Local' } })
  expect(res.status(), await res.text()).toBe(201)
  return (await res.json()) as { id: string; number: string; name: string }
}

async function makeParcel(request: APIRequestContext, headers: Headers, projectId?: string) {
  const project = projectId ? { id: projectId } : await makeProject(request, headers)
  const res = await request.post('/api/v1/parcels', { headers, data: { projectId: project.id, areaSqFt: 1000, acquisitionType: 'Standard' } })
  expect(res.status(), await res.text()).toBe(201)
  return (await res.json()) as { id: string; number: string; projectId: string }
}

async function openTab(page: Page, parcelId: string, tab: string) {
  await page.goto(`/parcels/${parcelId}`)
  await page.getByRole('tab', { name: tab }).click()
}

/** A second person, in their own browser, signed in through the real login screen. */
async function otherPerson(browser: Browser, page: Page, role: string) {
  const context = await browser.newContext({ baseURL: new URL(page.url()).origin })
  const other = await context.newPage()
  await signInViaUi(other, role)
  return { context, other }
}

test.describe('Project Manager · right of entry', () => {
  test('AT-PM-30 records, changes and removes a right of entry; an ended one reads Expired; the report lists it', async ({ page, request }) => {
    const a = await as(request, 'Admin')
    const parcel = await makeParcel(request, a)
    await signInViaUi(page, 'ProjectManager')
    await openTab(page, parcel.id, 'Right of entry')

    await expect(page.getByText('No rights of entry')).toBeVisible()
    const form = page.getByRole('form', { name: 'Record a right of entry' })
    const owner = unique('Alex Marlowe')

    // A granted right needs the date permission was given; the end cannot come before it.
    await form.getByLabel(/^Status/).selectOption('Granted')
    await form.getByRole('button', { name: 'Record' }).click()
    await expect(form.getByText('Enter the date permission was given.')).toBeVisible()
    await form.getByLabel(/^Permission given on/).fill(today(-10))
    await form.getByLabel(/^Permission ends on/).fill(today(-20))
    await form.getByRole('button', { name: 'Record' }).click()
    await expect(form.getByText('Permission cannot end before it was given.')).toBeVisible()
    await form.getByLabel(/^Permission given on/).fill(today(-60))
    await form.getByLabel(/^Permission ends on/).fill(today(-1))
    await form.getByLabel(/^Given by/).fill(`  ${owner}  `)
    await form.getByLabel(/^Purpose/).fill('Boundary survey')
    await form.getByRole('button', { name: 'Record' }).click()

    const table = page.getByRole('table', { name: 'Rights of entry' })
    await expect(table.getByRole('row', { name: new RegExp(owner) })).toContainText('Expired')

    // A right in the future is refused by the server with the reason beside the field.
    await form.getByLabel(/^Status/).selectOption('Requested')
    await form.getByLabel(/^Requested on/).fill(today(30))
    await form.getByRole('button', { name: 'Record' }).click()
    await expect(form.getByText('The request date cannot be in the future.')).toBeVisible()
    await form.getByLabel(/^Requested on/).fill(today(-2))
    await form.getByRole('button', { name: 'Record' }).click()
    await expect(table.getByRole('row')).toHaveCount(3)

    // Change the expired one to revoked, then remove it.
    await page.getByRole('button', { name: 'Edit the expired right of entry' }).click()
    const edit = page.getByRole('form', { name: 'Change right of entry' })
    await edit.getByLabel(/^Status/).selectOption('Revoked')
    await edit.getByRole('button', { name: 'Save changes' }).click()
    await expect(table.getByRole('row', { name: new RegExp(owner) })).toContainText('Revoked')

    const pm = { Authorization: `Bearer ${await sessionToken(page)}` }
    const report = await (await request.get(`/api/v1/rights-of-entry?projectId=${parcel.projectId}`, { headers: pm })).json()
    expect(report.items.map((r: any) => r.effectiveStatus).sort()).toEqual(['Requested', 'Revoked'])
    expect(report.items[0]).toMatchObject({ parcelNumber: parcel.number })

    await page.getByRole('button', { name: 'Remove the revoked right of entry' }).click()
    await expect(table.getByRole('row')).toHaveCount(2)
    expect(((await (await request.get(`/api/v1/parcels/${parcel.id}/rights-of-entry`, { headers: pm })).json()) as unknown[]).length).toBe(1)
  })
})

test.describe('Everyone · rights of entry report', () => {
  for (const role of ROLES) {
    test(`AT-X-40 ${role}: the report of rights of entry and the parcel's right-of-entry tab match the access spec, and the server agrees`, async ({ page, request }) => {
      const a = await as(request, 'Admin')
      const parcel = await makeParcel(request, a)
      const granted = await (await request.post(`/api/v1/parcels/${parcel.id}/rights-of-entry`, { headers: a, data: { status: 'Granted', grantedOn: today(-5), expiresOn: today(10), grantedBy: 'Seed Owner' } })).json()
      await request.post(`/api/v1/parcels/${parcel.id}/rights-of-entry`, { headers: a, data: { status: 'Granted', grantedOn: today(-50), expiresOn: today(-3), grantedBy: 'Old Owner' } })
      await signInViaUi(page, role)
      const mine = { Authorization: `Bearer ${await sessionToken(page)}` }

      // Sidebar and report page.
      const link = page.getByRole('complementary', { name: 'Primary' }).getByRole('link', { name: 'Rights of entry' })
      if (canRead(role, 'parcels')) {
        await link.click()
        await expect(page.getByRole('heading', { level: 1 })).toHaveText('Rights of entry')
        await expect(page.getByRole('table', { name: 'Rights of entry' })).toBeVisible()
        await page.getByLabel('Status').selectOption('Expired')
        const rows = page.getByRole('table', { name: 'Rights of entry' }).getByRole('row')
        await expect(rows.nth(1)).toContainText('Expired')
        await expect(rows.filter({ hasNotText: 'Expired' })).toHaveCount(1) // only the header row
      } else {
        await expect(link).toHaveCount(0)
        await page.goto('/rights-of-entry')
        await expect(page).toHaveURL(/\/forbidden/)
      }

      // The parcel's tab.
      if (canRead(role, 'parcels')) {
        await openTab(page, parcel.id, 'Right of entry')
        await expect(page.getByRole('table', { name: 'Rights of entry' }).getByRole('row', { name: /Seed Owner/ })).toBeVisible()
        await expect(page.getByRole('form', { name: 'Record a right of entry' })).toHaveCount(allowed(role, 'parcels', 'create') ? 1 : 0)
        await expect(page.getByRole('button', { name: /^Edit the/ })).toHaveCount(allowed(role, 'parcels', 'update') ? 2 : 0)
        await expect(page.getByRole('button', { name: /^Remove the/ })).toHaveCount(allowed(role, 'parcels', 'delete') ? 2 : 0)
      }

      // The server agrees.
      const code = async (p: Promise<{ status(): number }>) => (await p).status()
      const ok = (page: string, action: 'read' | 'create' | 'update' | 'delete', yes: number) => (allowed(role, page, action) ? yes : 403)
      expect(await code(request.get('/api/v1/rights-of-entry', { headers: mine }))).toBe(ok('parcels', 'read', 200))
      expect(await code(request.get(`/api/v1/parcels/${parcel.id}/rights-of-entry`, { headers: mine }))).toBe(ok('parcels', 'read', 200))
      expect(await code(request.post(`/api/v1/parcels/${parcel.id}/rights-of-entry`, { headers: mine, data: { status: 'Requested', requestedOn: today(-1), purpose: `By ${role}` } }))).toBe(ok('parcels', 'create', 201))
      expect(await code(request.put(`/api/v1/parcels/${parcel.id}/rights-of-entry/${granted.id}`, { headers: mine, data: { status: 'Granted', grantedOn: today(-5), expiresOn: today(10), grantedBy: `Edited by ${role}` } }))).toBe(ok('parcels', 'update', 200))
      expect(await code(request.delete(`/api/v1/parcels/${parcel.id}/rights-of-entry/${granted.id}`, { headers: mine }))).toBe(ok('parcels', 'delete', 204))
      const anon = { Authorization: '' }
      expect(await code(request.get('/api/v1/rights-of-entry', { headers: anon }))).toBe(401)
    })
  }
})

test.describe('Project Manager and Finance · acquisition requests', () => {
  test('AT-PM-31 / AT-FN-24 the project manager enters a request, cannot decide it; finance approves another and rejects with a reason', async ({ page, request, browser }) => {
    const a = await as(request, 'Admin')
    const project = await makeProject(request, a)
    await signInViaUi(page, 'ProjectManager')
    await page.getByRole('complementary', { name: 'Primary' }).getByRole('link', { name: 'Requests' }).click()
    await expect(page.getByRole('heading', { level: 1 })).toHaveText('Acquisition requests')
    const form = page.getByRole('form', { name: 'Enter a request' })
    const lot = unique('Lot behind 112 Harbor Rd')
    const quay = unique('North quay parcel')

    await form.getByRole('button', { name: 'Submit request' }).click()
    await expect(form.getByText('Enter who is asking.')).toBeVisible()
    await expect(form.getByText('Say which property is wanted.')).toBeVisible()
    await form.getByLabel(/^Who is asking/).fill('Design team')
    await form.getByLabel(/^Property wanted/).fill(lot)
    await form.getByLabel(/^Their email/).fill('nope')
    await form.getByRole('button', { name: 'Submit request' }).click()
    await expect(form.getByText('Enter a valid email address.')).toBeVisible()
    await form.getByLabel(/^Their email/).fill('design@example.com')
    await form.getByLabel(/^Project/).selectOption({ label: `${project.number} · ${project.name}` })
    await form.getByLabel('What they want').fill('Easement for 18 months')
    await form.getByLabel('Estimated amount').fill('45000')
    await form.getByRole('button', { name: 'Submit request' }).click()
    await expect(form.getByText(/Request entered/)).toBeVisible()

    const mine = page.getByRole('article', { name: `Request for ${lot}` })
    await expect(mine).toContainText('inside the agency')
    await expect(mine).toContainText(`Project ${project.number}`)
    await expect(mine).toContainText('by you')
    await expect(mine.getByRole('button', { name: 'Approve' })).toHaveCount(0)
    await expect(mine.getByText('Waiting for someone else to decide.')).toHaveCount(0) // the project manager may withdraw instead
    await expect(mine.getByRole('button', { name: 'Withdraw this request' })).toBeVisible()

    // The server agrees: a person never decides their own request.
    const pmToken = { Authorization: `Bearer ${await sessionToken(page)}` }
    const listed = await (await request.get('/api/v1/acquisition-requests?status=Submitted&pageSize=200', { headers: pmToken })).json()
    const created = listed.items.find((r: any) => r.property === lot)
    expect((await request.post(`/api/v1/acquisition-requests/${created.id}/approve`, { headers: pmToken, data: {} })).status()).toBe(403)

    // A second request, from outside the agency; finance approves one and rejects the other.
    await form.getByLabel(/^Asked by/).selectOption({ label: 'Outside the agency' })
    await form.getByLabel(/^Who is asking/).fill('Harbor Port Authority')
    await form.getByLabel(/^Property wanted/).fill(quay)
    await form.getByRole('button', { name: 'Submit request' }).click()
    await expect(page.getByRole('article', { name: `Request for ${quay}` })).toContainText('outside the agency')

    const { context, other } = await otherPerson(browser, page, 'Finance')
    await other.goto('/requests')
    const theirLot = other.getByRole('article', { name: `Request for ${lot}` })
    await expect(theirLot).toContainText('Design team')
    await theirLot.getByRole('button', { name: 'Approve' }).click()
    await expect(other.getByRole('article', { name: `Request for ${lot}` }).getByText('Approved')).toBeVisible()

    const theirQuay = other.getByRole('article', { name: `Request for ${quay}` })
    await theirQuay.getByRole('button', { name: 'Reject' }).click()
    await expect(theirQuay.getByText('Say why the request is rejected.')).toBeVisible()
    await theirQuay.getByLabel(/^Note/).fill('Outside the corridor.')
    await theirQuay.getByRole('button', { name: 'Reject' }).click()
    await expect(other.getByRole('article', { name: `Request for ${quay}` })).toContainText('Decision note: Outside the corridor.')

    // Decided requests are final and cannot be withdrawn.
    await page.reload()
    await expect(page.getByRole('article', { name: `Request for ${lot}` }).getByRole('button', { name: 'Withdraw this request' })).toHaveCount(0)
    expect((await request.post(`/api/v1/acquisition-requests/${created.id}/withdraw`, { headers: pmToken })).status()).toBe(409)
    await context.close()
  })

  test('AT-PM-31 the person who entered a request can withdraw it, and nobody else can', async ({ page, request }) => {
    await signInViaUi(page, 'ProjectManager')
    await page.goto('/requests')
    const form = page.getByRole('form', { name: 'Enter a request' })
    const lot = unique('Withdrawn lot')
    await form.getByLabel(/^Who is asking/).fill('Design team')
    await form.getByLabel(/^Property wanted/).fill(lot)
    await form.getByRole('button', { name: 'Submit request' }).click()
    const card = page.getByRole('article', { name: `Request for ${lot}` })
    await card.getByRole('button', { name: 'Withdraw this request' }).click()
    await expect(card.getByText('Withdrawn')).toBeVisible()

    const pm = { Authorization: `Bearer ${await sessionToken(page)}` }
    // Look it up by what was typed; poll because the list is read after the click and other specs add requests at the same time.
    let createdId = ''
    await expect.poll(async () => {
      const items = (await (await request.get('/api/v1/acquisition-requests?status=Withdrawn&pageSize=200', { headers: pm })).json()).items
      createdId = items.find((r: any) => r.property === lot)?.id ?? ''
      return createdId
    }, { timeout: 15_000 }).not.toBe('')
    const otherAdmin = await as(request, 'Admin')
    expect((await request.post(`/api/v1/acquisition-requests/${createdId}/approve`, { headers: otherAdmin, data: {} })).status()).toBe(409)
  })
})

test.describe('Project Manager and Finance · staff report and certificate of necessity', () => {
  test('AT-PM-33 / AT-FN-25 the project manager drafts and submits a staff report; finance rejects it with a reason; it is reworked, resubmitted and approved', async ({ page, request, browser }) => {
    const a = await as(request, 'Admin')
    const parcel = await makeParcel(request, a)
    await signInViaUi(page, 'ProjectManager')
    await openTab(page, parcel.id, 'Reviews & reports')
    const card = page.getByRole('article', { name: 'Staff report' })
    const title = unique('Staff report 001')

    await card.getByRole('button', { name: /^Write the staff report/ }).click()
    const form = page.getByRole('form', { name: 'Write the staff report' })
    await form.getByLabel(/^Title/).fill('')
    await form.getByRole('button', { name: 'Save' }).click()
    await expect(form.getByText('Enter a title.')).toBeVisible()
    await expect(form.getByText('Write the report.')).toBeVisible()
    await form.getByLabel(/^Title/).fill(`  ${title}  `)
    await form.getByLabel(/^Report/).fill('The parcel is needed for the bridge approach.')
    await form.getByRole('button', { name: 'Save' }).click()
    await expect(card.getByText(title)).toBeVisible()
    await expect(card.getByText('Draft')).toBeVisible()
    await card.getByRole('button', { name: 'Submit for approval' }).click()
    await expect(card.getByText('Submitted', { exact: true })).toBeVisible()
    await expect(card.getByText(/someone else has to approve or reject it/)).toBeVisible()
    await expect(card.getByRole('button', { name: 'Approve' })).toHaveCount(0)
    await expect(card.getByRole('button', { name: /^Change the staff report/ })).toHaveCount(0)

    const pm = { Authorization: `Bearer ${await sessionToken(page)}` }
    const report = (await (await request.get(`/api/v1/parcels/${parcel.id}/staff-reports`, { headers: pm })).json())[0]
    expect((await request.post(`/api/v1/parcels/${parcel.id}/staff-reports/${report.id}/approve`, { headers: pm, data: {} })).status()).toBe(403)
    expect((await request.put(`/api/v1/parcels/${parcel.id}/staff-reports/${report.id}`, { headers: pm, data: { title: 'x', body: 'y' } })).status()).toBe(409)

    // Finance rejects it with a reason.
    const { context, other } = await otherPerson(browser, page, 'Finance')
    await openTab(other, parcel.id, 'Reviews & reports')
    const theirs = other.getByRole('article', { name: 'Staff report' })
    await theirs.getByRole('button', { name: 'Reject' }).click()
    await expect(theirs.getByText('Say why the report is rejected.')).toBeVisible()
    await theirs.getByLabel(/^Note/).fill('Needs the cost basis.')
    await theirs.getByRole('button', { name: 'Reject' }).click()
    await expect(theirs.getByText('Decision note: Needs the cost basis.')).toBeVisible()
    await expect(theirs.getByRole('button', { name: 'Reject' })).toHaveCount(0)

    // The project manager reworks and resubmits; finance approves; it is then final.
    await page.reload()
    await page.getByRole('tab', { name: 'Reviews & reports' }).click()
    await card.getByRole('button', { name: /^Change the staff report/ }).click()
    const rework = page.getByRole('form', { name: 'Change the staff report' })
    await rework.getByLabel(/^Report/).fill('With the cost basis: $120 per square foot.')
    await rework.getByRole('button', { name: 'Save' }).click()
    await card.getByRole('button', { name: 'Submit for approval' }).click()
    await expect(card.getByText('Submitted', { exact: true })).toBeVisible()

    await other.reload()
    await other.getByRole('tab', { name: 'Reviews & reports' }).click()
    await other.getByRole('article', { name: 'Staff report' }).getByRole('button', { name: 'Approve' }).click()
    await expect(other.getByRole('article', { name: 'Staff report' }).getByText('Approved', { exact: true })).toBeVisible()
    await page.reload()
    await page.getByRole('tab', { name: 'Reviews & reports' }).click()
    await expect(card.getByText('Approved', { exact: true })).toBeVisible()
    await expect(card.getByRole('button', { name: /^Change the staff report/ })).toHaveCount(0)
    expect((await request.delete(`/api/v1/parcels/${parcel.id}/staff-reports/${report.id}`, { headers: pm })).status()).toBe(409)
    await context.close()
  })

  test('AT-PM-33 a certificate of necessity is written separately from the staff report, and a draft can be deleted', async ({ page, request }) => {
    const a = await as(request, 'Admin')
    const parcel = await makeParcel(request, a)
    await signInViaUi(page, 'ProjectManager')
    await openTab(page, parcel.id, 'Reviews & reports')
    const certificate = page.getByRole('article', { name: 'Certificate of necessity' })

    await certificate.getByRole('button', { name: /^Write the certificate of necessity/ }).click()
    const form = page.getByRole('form', { name: 'Write the certificate of necessity' })
    await form.getByLabel(/^Title/).fill('Certificate of necessity 001')
    await form.getByLabel(/^Report/).fill('The acquisition is necessary for the public project.')
    await form.getByRole('button', { name: 'Save' }).click()
    await expect(certificate.getByText('Certificate of necessity 001')).toBeVisible()
    await expect(page.getByRole('article', { name: 'Staff report' }).getByText(/No staff report has been written/)).toBeVisible()

    // The server holds one live report of a kind per parcel.
    const pm = { Authorization: `Bearer ${await sessionToken(page)}` }
    const again = await request.post(`/api/v1/parcels/${parcel.id}/staff-reports`, { headers: pm, data: { kind: 'CertificateOfNecessity', title: 'Second', body: 'x' } })
    expect(again.status()).toBe(409)
    expect(await again.text()).toContain('report_exists')

    await certificate.getByRole('button', { name: 'Delete the certificate of necessity' }).click()
    await expect(certificate.getByText(/No certificate of necessity has been written/)).toBeVisible()
    expect(((await (await request.get(`/api/v1/parcels/${parcel.id}/staff-reports`, { headers: pm })).json()) as unknown[]).length).toBe(0)
  })
})

test.describe('Project Manager · acquisition plan', () => {
  test('AT-PM-34 the plan lists the parcels and milestones and flags a budget the estimate has passed; it can be printed and exported', async ({ page, request }) => {
    const a = await as(request, 'Admin')
    const project = await makeProject(request, a)
    const first = await makeParcel(request, a, project.id)
    await makeParcel(request, a, project.id)
    await request.put(`/api/v1/projects/${project.id}/budgets/2026`, { headers: a, data: { amount: 1_000_000 } })
    expect((await request.post(`/api/v1/projects/${project.id}/cost-estimates`, { headers: a, data: { landCost: 1_200_000, improvementsCost: 0, relocationCost: 0, indirectCost: 0 } })).ok()).toBeTruthy()
    const detail = await (await request.get(`/api/v1/parcels/${first.id}`, { headers: a })).json()
    const notice = detail.milestones.find((m: any) => m.name === 'Notice to owner')
    expect((await request.put(`/api/v1/parcels/${first.id}/milestones/${notice.id}`, { headers: a, data: { dueOn: today(-4) } })).ok()).toBeTruthy()

    await signInViaUi(page, 'ProjectManager')
    await page.goto(`/projects/${project.id}`)
    await page.getByRole('tab', { name: 'Plan' }).click()

    const summary = page.getByLabel('Plan summary')
    await expect(summary).toContainText('Parcels')
    await expect(summary.getByText('2', { exact: true }).first()).toBeVisible()
    const budget = page.getByRole('status', { name: 'Budget against estimate' })
    await expect(budget).toContainText('Over budget by')
    await expect(budget).toContainText('$200,000')
    const schedule = page.getByRole('table', { name: 'Parcel schedule' })
    await expect(schedule.getByRole('row')).toHaveCount(3)
    await expect(schedule.getByRole('row').nth(1)).toContainText(first.number)

    const download = page.waitForEvent('download')
    await page.getByRole('button', { name: /Export CSV/ }).click()
    expect((await download).suggestedFilename()).toBe(`acquisition-plan-${project.number}.csv`)
    await expect(page.getByRole('button', { name: /Print plan/ })).toBeVisible()

    const pm = { Authorization: `Bearer ${await sessionToken(page)}` }
    const plan = await (await request.get(`/api/v1/projects/${project.id}/acquisition-plan`, { headers: pm })).json()
    expect(plan.plan.budget).toMatchObject({ totalBudget: 1_000_000, currentEstimate: 1_200_000, variance: -200_000, overBudget: true })
  })
})

test.describe('Other roles · acquisition records', () => {
  test('AT-AP-25 the appraiser approves a request and a staff report entered by someone else', async ({ page, request }) => {
    const a = await someoneElse(request, 'Admin')
    const parcel = await makeParcel(request, a)
    const lot = unique('Appraiser lot')
    await request.post('/api/v1/acquisition-requests', { headers: a, data: { requesterType: 'Internal', requesterName: 'Design team', property: lot } })
    const draft = await (await request.post(`/api/v1/parcels/${parcel.id}/staff-reports`, { headers: a, data: { kind: 'StaffReport', title: 'Report', body: 'Body' } })).json()
    await request.post(`/api/v1/parcels/${parcel.id}/staff-reports/${draft.id}/submit`, { headers: a })

    await signInViaUi(page, 'Appraiser')
    await page.goto('/requests')
    await expect(page.getByRole('form', { name: 'Enter a request' })).toHaveCount(0) // submitting is a project job
    const card = page.getByRole('article', { name: `Request for ${lot}` })
    await card.getByRole('button', { name: 'Approve' }).click()
    await expect(page.getByRole('article', { name: `Request for ${lot}` }).getByText('Approved')).toBeVisible()

    await openTab(page, parcel.id, 'Reviews & reports')
    await page.getByRole('article', { name: 'Staff report' }).getByRole('button', { name: 'Approve' }).click()
    await expect(page.getByRole('article', { name: 'Staff report' }).getByText('Approved', { exact: true })).toBeVisible()
    await expect(page.getByRole('form', { name: 'Enter a request' })).toHaveCount(0)
  })

  test('AT-NG-26 the negotiator reads rights of entry, reviews, reports and requests but changes none of them', async ({ page, request }) => {
    const a = await as(request, 'Admin')
    const parcel = await makeParcel(request, a)
    const lot = unique('Negotiator lot')
    await request.post(`/api/v1/parcels/${parcel.id}/rights-of-entry`, { headers: a, data: { status: 'Requested', requestedOn: today(-1), grantedBy: 'Seed' } })
    await request.post('/api/v1/acquisition-requests', { headers: a, data: { requesterType: 'External', requesterName: 'Port', property: lot } })

    await signInViaUi(page, 'Negotiator')
    await openTab(page, parcel.id, 'Right of entry')
    await expect(page.getByRole('table', { name: 'Rights of entry' })).toBeVisible()
    await expect(page.getByRole('form', { name: 'Record a right of entry' })).toHaveCount(0)
    await page.getByRole('tab', { name: 'Reviews & reports' }).click()
    await expect(page.getByRole('status', { name: 'Plan review gate' })).toBeVisible()
    await expect(page.getByRole('form', { name: 'Record a plan review' })).toHaveCount(0)
    await expect(page.getByRole('button', { name: /^Write the/ })).toHaveCount(0)
    await page.goto('/requests')
    const card = page.getByRole('article', { name: `Request for ${lot}` })
    await expect(card).toBeVisible()
    await expect(card.getByRole('button', { name: 'Approve' })).toHaveCount(0)
    await expect(page.getByRole('form', { name: 'Enter a request' })).toHaveCount(0)

    const ng = { Authorization: `Bearer ${await sessionToken(page)}` }
    expect((await request.post(`/api/v1/parcels/${parcel.id}/plan-reviews`, { headers: ng, data: { discipline: 'Engineer', outcome: 'Approved', reviewedOn: today(), reviewerName: 'x' } })).status()).toBe(403)
    expect((await request.post('/api/v1/acquisition-requests', { headers: ng, data: { requesterType: 'Internal', requesterName: 'x', property: 'y' } })).status()).toBe(403)
  })

  test('AT-TE-25 the title examiner reads rights of entry but cannot open the requests page or any request', async ({ page, request }) => {
    const a = await as(request, 'Admin')
    const parcel = await makeParcel(request, a)
    await request.post(`/api/v1/parcels/${parcel.id}/rights-of-entry`, { headers: a, data: { status: 'Requested', requestedOn: today(-1), grantedBy: 'Seed' } })
    await signInViaUi(page, 'TitleResearcher')

    await expect(page.getByRole('complementary', { name: 'Primary' }).getByRole('link', { name: 'Requests' })).toHaveCount(0)
    await page.goto('/requests')
    await expect(page).toHaveURL(/\/forbidden/)
    await openTab(page, parcel.id, 'Right of entry')
    await expect(page.getByRole('table', { name: 'Rights of entry' })).toBeVisible()
    await expect(page.getByRole('form', { name: 'Record a right of entry' })).toHaveCount(0)

    const te = { Authorization: `Bearer ${await sessionToken(page)}` }
    expect((await request.get('/api/v1/acquisition-requests', { headers: te })).status()).toBe(403)
    expect((await request.post('/api/v1/acquisition-requests', { headers: te, data: { requesterType: 'Internal', requesterName: 'x', property: 'y' } })).status()).toBe(403)
  })
})

test.describe('Everyone · approvals, reports and the plan', () => {
  for (const role of ROLES) {
    test(`AT-X-41 ${role}: requests, reviews, reports and the plan are available exactly as the access spec says, and the server agrees`, async ({ page, request }) => {
      const a = await as(request, 'Admin')
      const parcel = await makeParcel(request, a)
      const maker = await someoneElse(request, 'Admin') // enters the seeded request and report, so nobody signed in below is their author
      const draft = await (await request.post(`/api/v1/parcels/${parcel.id}/staff-reports`, { headers: a, data: { kind: 'StaffReport', title: 'Seed', body: 'Seed' } })).json()
      await request.post(`/api/v1/parcels/${parcel.id}/staff-reports/${draft.id}/submit`, { headers: maker })
      const seededRequest = await (await request.post('/api/v1/acquisition-requests', { headers: maker, data: { requesterType: 'Internal', requesterName: 'Seed', property: unique('Seed lot'), projectId: parcel.projectId } })).json()
      await signInViaUi(page, role)
      const mine = { Authorization: `Bearer ${await sessionToken(page)}` }

      // UI
      await page.goto(`/projects/${parcel.projectId}`)
      if (allowed(role, 'projects', 'read')) await expect(page.getByRole('tab', { name: 'Plan' })).toHaveCount(allowed(role, 'parcels', 'read') ? 1 : 0)
      if (allowed(role, 'parcels', 'read')) {
        await openTab(page, parcel.id, 'Reviews & reports')
        await expect(page.getByRole('article', { name: 'Staff report' })).toBeVisible()
        await expect(page.getByRole('form', { name: 'Record a plan review' })).toHaveCount(allowed(role, 'parcels', 'update') ? 1 : 0)
        await expect(page.getByRole('article', { name: 'Staff report' }).getByRole('button', { name: 'Approve' })).toHaveCount(allowed(role, 'approvals', 'update') ? 1 : 0)
        await expect(page.getByRole('article', { name: 'Certificate of necessity' }).getByRole('button', { name: /^Write the/ })).toHaveCount(allowed(role, 'parcels', 'create') ? 1 : 0)
      }
      if (allowed(role, 'approvals', 'read')) {
        await page.goto('/requests')
        await expect(page.getByRole('list', { name: 'Requests' })).toBeVisible()
        await expect(page.getByRole('form', { name: 'Enter a request' })).toHaveCount(allowed(role, 'projects', 'create') ? 1 : 0)
      } else {
        await page.goto('/requests')
        await expect(page).toHaveURL(/\/forbidden/)
      }

      // API
      const code = async (p: Promise<{ status(): number }>) => (await p).status()
      const ok = (pg: string, action: 'read' | 'create' | 'update' | 'delete', yes: number) => (allowed(role, pg, action) ? yes : 403)
      expect(await code(request.get(`/api/v1/projects/${parcel.projectId}/acquisition-plan`, { headers: mine }))).toBe(ok('parcels', 'read', 200))
      expect(await code(request.get(`/api/v1/parcels/${parcel.id}/plan-reviews`, { headers: mine }))).toBe(ok('parcels', 'read', 200))
      expect(await code(request.get(`/api/v1/parcels/${parcel.id}/staff-reports`, { headers: mine }))).toBe(ok('parcels', 'read', 200))
      expect(await code(request.get('/api/v1/agency/plan-review-gate', { headers: mine }))).toBe(ok('parcels', 'read', 200))
      expect(await code(request.get('/api/v1/acquisition-requests', { headers: mine }))).toBe(ok('approvals', 'read', 200))
      expect(await code(request.post(`/api/v1/parcels/${parcel.id}/plan-reviews`, { headers: mine, data: { discipline: 'Engineer', outcome: 'Approved', reviewedOn: today(), reviewerName: `By ${role}` } }))).toBe(ok('parcels', 'update', 201))
      expect(await code(request.post(`/api/v1/parcels/${parcel.id}/staff-reports`, { headers: mine, data: { kind: 'CertificateOfNecessity', title: `By ${role}`, body: 'x' } }))).toBe(ok('parcels', 'create', 201))
      expect(await code(request.post('/api/v1/acquisition-requests', { headers: mine, data: { requesterType: 'Internal', requesterName: `By ${role}`, property: 'y' } }))).toBe(ok('projects', 'create', 201))
      expect(await code(request.put('/api/v1/agency/plan-review-gate', { headers: mine, data: { enabled: false } }))).toBe(ok('administration', 'update', 200))
      // Deciding: the approvals page, and never the person who entered it (here a different admin entered the request).
      expect(await code(request.post(`/api/v1/acquisition-requests/${seededRequest.id}/reject`, { headers: mine, data: { note: `By ${role}` } }))).toBe(ok('approvals', 'update', 200))
      expect(await code(request.get('/api/v1/acquisition-requests', { headers: { Authorization: '' } }))).toBe(401)
    })
  }

  test('AT-X-41 the records of another agency and of a split parcel cannot be reached or changed', async ({ request }) => {
    const a = await as(request, 'Admin')
    const parcel = await makeParcel(request, a)
    await request.post(`/api/v1/parcels/${parcel.id}/rights-of-entry`, { headers: a, data: { status: 'Requested', requestedOn: today(-1) } })
    const created = await (await request.post('/api/v1/acquisition-requests', { headers: a, data: { requesterType: 'Internal', requesterName: 'Mine', property: 'Mine' } })).json()
    const other = { Authorization: `Bearer ${await devToken(request, 'Admin', { tenant: newId() })}` }
    for (const part of ['rights-of-entry', 'plan-reviews', 'staff-reports']) expect([403, 404]).toContain((await request.get(`/api/v1/parcels/${parcel.id}/${part}`, { headers: other })).status())
    expect([403, 404]).toContain((await request.get(`/api/v1/acquisition-requests/${created.id}`, { headers: other })).status())
    expect([403, 404]).toContain((await request.get(`/api/v1/projects/${parcel.projectId}/acquisition-plan`, { headers: other })).status())

    expect((await request.post(`/api/v1/parcels/${parcel.id}/split`, { headers: a, data: { parts: [{ areaSqFt: 300 }] } })).status()).toBe(201)
    for (const [url, data] of [
      [`/api/v1/parcels/${parcel.id}/rights-of-entry`, { status: 'Requested', requestedOn: today(-1) }],
      [`/api/v1/parcels/${parcel.id}/plan-reviews`, { discipline: 'Engineer', outcome: 'Approved', reviewedOn: today(), reviewerName: 'x' }],
      [`/api/v1/parcels/${parcel.id}/staff-reports`, { kind: 'StaffReport', title: 'x', body: 'x' }],
    ] as const) {
      const res = await request.post(url, { headers: a, data })
      expect(res.status(), url).toBe(409)
      expect(await res.text()).toContain('parcel_not_active')
    }
  })
})
