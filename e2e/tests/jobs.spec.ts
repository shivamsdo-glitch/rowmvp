import { expect, test, type APIRequestContext, type Page } from '@playwright/test'
import { devToken, documents, newId, savedFilters, sessionToken, signInViaUi } from './support'

/**
 * Per-role job journeys that exist today. Each signs in through the real screen, then does that role's
 * work against the API with the very token the browser holds (through the Vite proxy), and checks the
 * hand-offs to other roles. As each phase adds screens, the job moves from the pending list
 * (future.spec.ts) to a UI journey here. Case ids match vault/acceptance/<Role>.md.
 */

const bytes = (label: string) => `contents of ${label} ${newId()}`

async function as(page: Page, role: string) {
  await signInViaUi(page, role)
  return sessionToken(page)
}

/** Someone with full access puts a file on a record, so other roles have something to look at. */
async function seed(request: APIRequestContext, entityType: string, entityId: string, name: string, content: string) {
  const res = await documents(request, await devToken(request, 'Admin')).upload(entityType, entityId, name, content)
  expect(res.status()).toBe(201)
  return (await res.json()).id as string
}

// ───────────────────────────────── Project Manager ─────────────────────────────────
test.describe('Project Manager', () => {
  test('AT-PM-10 attaches the funding approval to a project and a sketch to a parcel, then finds and opens them', async ({ page, request }) => {
    const token = await as(page, 'ProjectManager')
    const docs = documents(request, token)
    const project = newId(), parcel = newId(), sketch = bytes('parcel sketch')

    expect((await docs.upload('Project', project, 'funding-approval.pdf', bytes('funding'), 'FundingApproval')).status()).toBe(201)
    const up = await docs.upload('Parcel', parcel, 'parcel-sketch.pdf', sketch)
    expect(up.status()).toBe(201)

    const list = await (await docs.list('Parcel', parcel)).json()
    expect(list).toHaveLength(1)
    expect(list[0].fileName).toBe('parcel-sketch.pdf')
    const download = await docs.download(list[0].id)
    expect(download.status()).toBe(200)
    expect((await download.body()).toString()).toBe(sketch)
    expect(download.headers()['content-disposition']).toContain('attachment')
  })

  test('AT-PM-11 can read what specialists attach but cannot change title, appraisal or negotiation records', async ({ page, request }) => {
    const token = await as(page, 'ProjectManager')
    const docs = documents(request, token)
    const entity = newId()
    const cases: [string, string][] = [['TitleRecord', 'deed.pdf'], ['Appraisal', 'report.pdf'], ['Offer', 'offer.pdf']]

    for (const [type, name] of cases) {
      const id = await seed(request, type, entity, name, bytes(name))
      expect((await docs.download(id)).status(), `read ${type}`).toBe(200)
      expect((await docs.upload(type, entity, 'mine.pdf', 'x')).status(), `attach ${type}`).toBe(403)
      expect((await docs.remove(id)).status(), `remove ${type}`).toBe(403)
    }
  })

  test('AT-PM-12 keeps a saved view of parcels that survives reload and is private to them', async ({ page, request }) => {
    await signInViaUi(page, 'ProjectManager')
    const me = newId(), colleague = newId()
    const mine = savedFilters(request, await devToken(request, 'ProjectManager', { user: me }))

    expect((await mine.create('parcels', 'My open parcels', { status: ['Open'] })).status()).toBe(201)
    await page.reload()
    const again = await (await savedFilters(request, await devToken(request, 'ProjectManager', { user: me })).list('parcels')).json()
    expect(again.map((f: { name: string }) => f.name)).toEqual(['My open parcels'])

    const theirs = await (await savedFilters(request, await devToken(request, 'ProjectManager', { user: colleague })).list('parcels')).json()
    expect(theirs).toHaveLength(0)
  })

  test('AT-PM-13 has no access to Administration, on screen or on the server', async ({ page, request }) => {
    const token = await as(page, 'ProjectManager')
    await page.goto('/administration')
    await expect(page.getByText(/don't have access/i)).toBeVisible()
    expect((await request.get('/api/v1/admin/ping', { headers: { Authorization: `Bearer ${token}` } })).status()).toBe(403)
  })
})

// ───────────────────────────────── Title Examiner ─────────────────────────────────
test.describe('Title Examiner', () => {
  test('AT-TE-10 attaches the commitment and deed to the title record and the survey CAD file to the survey request', async ({ page, request }) => {
    const token = await as(page, 'TitleResearcher')
    const docs = documents(request, token)
    const parcel = newId()

    for (const [type, name, docType] of [['TitleRecord', 'commitment.pdf', 'TitleCommitment'], ['TitleRecord', 'deed.pdf', 'Deed'], ['SurveyRequest', 'boundary.dwg', 'BoundarySurvey']]) {
      expect((await docs.upload(type, parcel, name, bytes(name), docType)).status(), name).toBe(201)
    }
    const title = await (await docs.list('TitleRecord', parcel)).json()
    expect(title.map((d: { fileName: string }) => d.fileName).sort()).toEqual(['commitment.pdf', 'deed.pdf'])
    expect(await (await docs.list('SurveyRequest', parcel)).json()).toHaveLength(1)
  })

  test('AT-TE-11 can withdraw a document they attached by mistake', async ({ page, request }) => {
    const docs = documents(request, await as(page, 'TitleResearcher'))
    const parcel = newId()
    const id = (await (await docs.upload('TitleRecord', parcel, 'wrong-parcel.pdf', 'x')).json()).id

    expect((await docs.remove(id)).status()).toBe(204)

    expect(await (await docs.list('TitleRecord', parcel)).json()).toHaveLength(0)
    expect((await docs.download(id)).status()).toBe(404)
  })

  test('AT-TE-12 cannot alter appraisal, offer or payment records and has no view of payments', async ({ page, request }) => {
    const docs = documents(request, await as(page, 'TitleResearcher'))
    const entity = newId()

    expect((await docs.upload('Appraisal', entity, 'a.pdf', 'x')).status()).toBe(403)
    expect((await docs.upload('Offer', entity, 'a.pdf', 'x')).status()).toBe(403)
    expect((await docs.upload('Payment', entity, 'a.pdf', 'x')).status()).toBe(403)
    expect((await docs.list('Payment', entity)).status()).toBe(403)
  })

  test('AT-TE-13 saves a view on the title page but not on a page they cannot open', async ({ page, request }) => {
    const filters = savedFilters(request, await as(page, 'TitleResearcher'))

    expect((await filters.create('title', 'Defects to clear', { hasDefects: true })).status()).toBe(201)
    expect((await filters.create('payments', 'Nope')).status()).toBe(403)
    expect((await filters.list('payments')).status()).toBe(403)
  })
})

// ───────────────────────────────── Appraiser ─────────────────────────────────
test.describe('Appraiser', () => {
  test('AT-AP-10 attaches the appraisal report so the negotiator can use it', async ({ page, request }) => {
    const docs = documents(request, await as(page, 'Appraiser'))
    const parcel = newId(), report = bytes('appraisal report')

    const up = await docs.upload('Appraisal', parcel, 'appraisal-report.pdf', report, 'AppraisalReport')
    expect(up.status()).toBe(201)
    const id = (await up.json()).id

    const negotiator = documents(request, await devToken(request, 'Negotiator'))
    const opened = await negotiator.download(id)
    expect(opened.status()).toBe(200)
    expect((await opened.body()).toString()).toBe(report)
  })

  test('AT-AP-11 reads the title and survey package but cannot change it', async ({ page, request }) => {
    const docs = documents(request, await as(page, 'Appraiser'))
    const parcel = newId()
    const title = await seed(request, 'TitleRecord', parcel, 'commitment.pdf', bytes('c'))
    const survey = await seed(request, 'SurveyRequest', parcel, 'plat.pdf', bytes('p'))

    for (const id of [title, survey]) {
      expect((await docs.download(id)).status()).toBe(200)
      expect((await docs.remove(id)).status()).toBe(403)
    }
    expect((await docs.upload('TitleRecord', parcel, 'x.pdf', 'x')).status()).toBe(403)
    expect((await docs.upload('SurveyRequest', parcel, 'x.pdf', 'x')).status()).toBe(403)
  })

  test('AT-AP-12 has no access to negotiation or payment records', async ({ page, request }) => {
    const docs = documents(request, await as(page, 'Appraiser'))
    const entity = newId()

    for (const type of ['Offer', 'Agreement', 'Payment', 'ClosingRecord']) {
      expect((await docs.list(type, entity)).status(), type).toBe(403)
      expect((await docs.upload(type, entity, 'a.pdf', 'x')).status(), type).toBe(403)
    }
  })
})

// ───────────────────────────────── Negotiator ─────────────────────────────────
test.describe('Negotiator', () => {
  test('AT-NG-10 attaches the offer letter and the signed agreement to a parcel’s negotiation', async ({ page, request }) => {
    const docs = documents(request, await as(page, 'Negotiator'))
    const parcel = newId()

    expect((await docs.upload('Offer', parcel, 'offer-letter.pdf', bytes('offer'), 'OfferLetter')).status()).toBe(201)
    expect((await docs.upload('Agreement', parcel, 'signed-agreement.pdf', bytes('agreement'), 'SignedAgreement')).status()).toBe(201)
    expect(await (await docs.list('Offer', parcel)).json()).toHaveLength(1)
    expect(await (await docs.list('Agreement', parcel)).json()).toHaveLength(1)
  })

  test('AT-NG-11 can add to a closing record but cannot delete from it', async ({ page, request }) => {
    const docs = documents(request, await as(page, 'Negotiator'))
    const parcel = newId()
    const id = (await (await docs.upload('ClosingRecord', parcel, 'closing-checklist.pdf', bytes('checklist'))).json()).id

    expect((await docs.upload('ClosingRecord', parcel, 'recording-receipt.pdf', bytes('receipt'))).status()).toBe(201)
    expect((await docs.remove(id)).status()).toBe(403)
    expect(await (await docs.list('ClosingRecord', parcel)).json()).toHaveLength(2)
  })

  test('AT-NG-12 manages owner and interest-holder records fully', async ({ page, request }) => {
    const docs = documents(request, await as(page, 'Negotiator'))
    const party = newId()
    const id = (await (await docs.upload('Party', party, 'w9.pdf', bytes('w9'), 'TaxForm')).json()).id

    expect((await docs.remove(id)).status()).toBe(204)
    expect(await (await docs.list('Party', party)).json()).toHaveLength(0)
  })

  test('AT-NG-13 reads title and appraisal records, cannot alter them, and has no survey access', async ({ page, request }) => {
    const docs = documents(request, await as(page, 'Negotiator'))
    const parcel = newId()
    const title = await seed(request, 'TitleRecord', parcel, 'commitment.pdf', bytes('c'))
    const appraisal = await seed(request, 'Appraisal', parcel, 'report.pdf', bytes('r'))

    expect((await docs.download(title)).status()).toBe(200)
    expect((await docs.download(appraisal)).status()).toBe(200)
    expect((await docs.remove(title)).status()).toBe(403)
    expect((await docs.upload('Appraisal', parcel, 'x.pdf', 'x')).status()).toBe(403)
    expect((await docs.list('SurveyRequest', parcel)).status()).toBe(403)
  })
})

// ───────────────────────────────── Finance ─────────────────────────────────
test.describe('Finance', () => {
  test('AT-FN-10 owns payment records: attach, correct and remove', async ({ page, request }) => {
    const docs = documents(request, await as(page, 'Finance'))
    const parcel = newId()
    const id = (await (await docs.upload('Payment', parcel, 'payment-request.pdf', bytes('pay'), 'PaymentRequest')).json()).id

    expect((await docs.upload('Payment', parcel, 'w9.pdf', bytes('w9'))).status()).toBe(201)
    expect((await docs.remove(id)).status()).toBe(204)
    expect(await (await docs.list('Payment', parcel)).json()).toHaveLength(1)
  })

  test('AT-FN-11 can read offers, agreements and closing records but cannot change them', async ({ page, request }) => {
    const docs = documents(request, await as(page, 'Finance'))
    const parcel = newId()

    for (const type of ['Offer', 'Agreement', 'ClosingRecord']) {
      const id = await seed(request, type, parcel, `${type}.pdf`, bytes(type))
      expect((await docs.download(id)).status(), `read ${type}`).toBe(200)
      expect((await docs.upload(type, parcel, 'x.pdf', 'x')).status(), `attach ${type}`).toBe(403)
      expect((await docs.remove(id)).status(), `remove ${type}`).toBe(403)
    }
  })

  test('AT-FN-12 has no access to title, survey or appraisal records', async ({ page, request }) => {
    const docs = documents(request, await as(page, 'Finance'))
    const parcel = newId()

    for (const type of ['TitleRecord', 'SurveyRequest', 'Appraisal']) {
      const id = await seed(request, type, parcel, `${type}.pdf`, bytes(type))
      expect((await docs.list(type, parcel)).status(), type).toBe(403)
      expect((await docs.download(id)).status(), type).toBe(403)
    }
  })
})

// ───────────────────────────────── Administrator ─────────────────────────────────
test.describe('Administrator', () => {
  test('AT-AD-10 can attach, open and remove files on every kind of record', async ({ page, request }) => {
    const docs = documents(request, await as(page, 'Admin'))
    for (const type of ['Project', 'Parcel', 'Party', 'TitleRecord', 'SurveyRequest', 'Appraisal', 'Offer', 'Agreement', 'ClosingRecord', 'Payment']) {
      const entity = newId(), content = bytes(type)
      const up = await docs.upload(type, entity, `${type}.pdf`, content)
      expect(up.status(), type).toBe(201)
      const id = (await up.json()).id
      expect((await (await docs.download(id)).body()).toString(), type).toBe(content)
      expect((await docs.remove(id)).status(), type).toBe(204)
    }
  })

  test('AT-AD-11 opens Configuration from the sidebar', async ({ page }) => {
    await signInViaUi(page, 'Admin')

    await page.getByRole('complementary', { name: 'Primary' }).getByRole('link', { name: 'Configuration' }).click()

    await expect(page).toHaveURL(/\/administration$/)
    await expect(page.getByRole('heading', { name: 'Administration' })).toBeVisible()
  })

  test('AT-AD-12 can reach the administrator-only server endpoint', async ({ page, request }) => {
    const token = await as(page, 'Admin')

    expect((await request.get('/api/v1/admin/ping', { headers: { Authorization: `Bearer ${token}` } })).status()).toBe(200)
  })

  test('AT-AD-13 sees every page in their permissions, including Administration', async ({ page, request }) => {
    const token = await as(page, 'Admin')
    const perms = await (await request.get('/api/v1/auth/permissions', { headers: { Authorization: `Bearer ${token}` } })).json()

    expect(perms).toHaveLength(13)
    for (const p of perms) expect([p.pageKey, p.canCreate, p.canRead, p.canUpdate, p.canDelete]).toEqual([p.pageKey, true, true, true, true])
  })
})
