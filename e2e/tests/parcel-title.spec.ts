import { expect, test, type APIRequestContext, type Page } from '@playwright/test'
import { canUpdate, devToken, level, newId, ROLES, sessionToken, signInViaUi } from './support'

/**
 * Phase 1.5 slice 6 (ADR 0018): a parcel's Title Encumbrances tab. Real browser, UI, API and Postgres.
 * Case ids match vault/acceptance/TitleExaminer.md and Everyone.md. Every test makes its own parcel.
 */

const canCreate = (role: string, page: string) => ['S', 'P'].includes(level(role, page) ?? '')
const canDelete = (role: string, page: string) => level(role, page) === 'P'
const unique = (label: string) => `${label} ${newId().slice(0, 8)}`

async function adminHeaders(request: APIRequestContext) {
  return { Authorization: `Bearer ${await devToken(request, 'Admin')}` }
}

async function ensureLibraryValue(request: APIRequestContext, key: string, value: string) {
  const headers = await adminHeaders(request)
  const fields = (await (await request.get('/api/v1/library/fields', { headers })).json()) as { id: string; key: string }[]
  const res = await request.post(`/api/v1/library/fields/${fields.find((f) => f.key === key)!.id}/values`, { headers, data: { value } })
  expect([200, 201, 409]).toContain(res.status())
}

async function makeParcel(request: APIRequestContext) {
  const headers = await adminHeaders(request)
  const project = await (await request.post('/api/v1/projects', { headers, data: { name: unique('Title scene'), oversightType: 'Local', phase: 'Phase 1' } })).json()
  return (await (await request.post('/api/v1/parcels', { headers, data: { projectId: project.id, areaSqFt: 1000, acquisitionType: 'Standard', address: 'Title Rd' } })).json()) as { id: string; number: string }
}

async function openTab(page: Page, parcelId: string, tab: string) {
  await page.goto(`/parcels/${parcelId}`)
  await expect(page.getByRole('region', { name: 'Parcel synopsis' })).toBeVisible()
  await page.getByRole('tab', { name: tab }).click()
}

test.describe('Title Researcher · title encumbrances', () => {
  test('AT-TE-40 records an encumbrance with its document, parties and cure, attaches the document, changes it and removes it', async ({ page, request }) => {
    await ensureLibraryValue(request, 'cure_status', 'In progress')
    const parcel = await makeParcel(request)
    await signInViaUi(page, 'TitleResearcher')
    await openTab(page, parcel.id, 'Title encumbrances')
    const form = page.getByRole('form', { name: 'Add an encumbrance' })

    await form.getByRole('button', { name: 'Add encumbrance' }).click()
    for (const m of ['Encumbrance type is required.', 'Document title is required.', 'Grantor / lessor is required.', 'Grantee / lessee is required.'])
      await expect(form.getByText(m)).toBeVisible()

    await form.getByLabel(/^Encumbrance type/).selectOption('Mortgage Lien')
    await form.getByLabel(/^Document title/).fill('Deed of Trust 2019')
    await form.getByLabel(/^Document date/).fill('2019-03-01')
    await form.getByLabel(/^Date recorded/).fill('2019-03-15')
    await form.getByLabel(/^Document number/).fill('Vol 12 Pg 345')
    await form.getByLabel(/^Grantor/).fill('Kamali Family')
    await form.getByLabel(/^Grantee/).fill('First Bank')
    await form.getByLabel(/^Exception to title submitted date/).fill('2026-03-01')
    await form.getByLabel(/^Exception to title approved date/).fill('2026-02-01')
    await form.getByRole('button', { name: 'Add encumbrance' }).click()
    await expect(form.getByText('The exception cannot be approved before it is submitted.')).toBeVisible()
    await form.getByLabel(/^Exception to title approved date/).fill('2026-03-10')
    await form.getByLabel(/^Cure status/).selectOption('In progress')
    await form.getByLabel(/^Impact analysis/).fill('Blocks closing until released')
    await form.getByLabel(/^Recommended cure/).fill('Obtain a release of lien')
    await form.getByLabel(/^Record identifier/).fill('TC-1')
    await form.getByLabel(/^Module name/).fill('Title and closing')
    await form.getByRole('button', { name: 'Add linked record' }).click()
    await form.getByRole('button', { name: 'Add encumbrance' }).click()

    const number = `ENC-${parcel.number}-001`
    const row = page.getByRole('row', { name: new RegExp(number) })
    await expect(row).toContainText('Mortgage Lien')
    await expect(row).toContainText('Kamali Family')
    await expect(row).toContainText('In progress')

    await page.getByRole('button', { name: `Edit encumbrance ${number}` }).click()
    const edit = page.getByRole('form', { name: 'Change encumbrance' })
    await expect(edit.getByLabel(/^Grantee/)).toHaveValue('First Bank')
    await edit.getByLabel('Unrecorded document').check()
    await expect(edit.getByLabel(/^Date recorded/)).toBeDisabled()
    await edit.getByLabel(/^Review notes/).fill('Release requested.')
    await edit.getByRole('button', { name: 'Save changes' }).click()
    await expect(page.getByRole('row', { name: new RegExp(number) })).toContainText('Unrecorded')

    await page.getByRole('button', { name: `Edit encumbrance ${number}` }).click()
    const panel = page.getByRole('region', { name: `Attachments for ${number}` })
    await panel.getByLabel('Upload document').setInputFiles({ name: 'deed-of-trust.txt', mimeType: 'text/plain', buffer: Buffer.from('deed of trust') })
    await expect(panel.getByText('deed-of-trust.txt')).toBeVisible()

    const headers = { Authorization: `Bearer ${await sessionToken(page)}` }
    const list = await (await request.get(`/api/v1/parcels/${parcel.id}/encumbrances`, { headers })).json()
    expect(list).toHaveLength(1)
    expect(list[0]).toMatchObject({
      number, encumbranceType: 'Mortgage Lien', isUnrecorded: true, dateRecorded: null, reviewNotes: 'Release requested.', cureStatus: 'In progress',
      exceptionApprovedDate: '2026-03-10', links: [{ recordIdentifier: 'TC-1', moduleName: 'Title and closing' }],
    })

    await panel.getByRole('button', { name: 'Remove deed-of-trust.txt' }).click()
    await expect(panel.getByText('No attachments')).toBeVisible()
    await page.getByRole('button', { name: `Remove encumbrance ${number}` }).click()
    await expect(page.getByText('No encumbrances recorded')).toBeVisible()
  })

  test('AT-TE-40 the next encumbrance on a parcel gets the next number', async ({ page, request }) => {
    const parcel = await makeParcel(request)
    const headers = await adminHeaders(request)
    for (const n of [1, 2]) {
      const r = await request.post(`/api/v1/parcels/${parcel.id}/encumbrances`, {
        headers, data: { encumbranceType: 'Lease', documentTitle: `Lease ${n}`, grantor: 'A', grantee: 'B', isUnrecorded: true },
      })
      expect(r.status()).toBe(201)
    }
    await signInViaUi(page, 'TitleResearcher')
    await openTab(page, parcel.id, 'Title encumbrances')
    const rows = page.getByRole('table', { name: 'Title encumbrances' }).getByRole('row')
    await expect(rows.nth(1)).toContainText(`ENC-${parcel.number}-001`)
    await expect(rows.nth(2)).toContainText(`ENC-${parcel.number}-002`)
  })
})

/** Agents are people of the Library; the form's "Assigned to" picks from them. Adding the same person twice is refused, which is fine here. */
async function ensureAgent(request: APIRequestContext, name: string) {
  const headers = await adminHeaders(request)
  const res = await request.post('/api/v1/library/agents', { headers, data: { name, agentType: 'Negotiator', company: null, phone: null, email: null, isActive: true } })
  expect([200, 201, 409]).toContain(res.status())
}

test.describe('Title Researcher · title and closing', () => {
  test('AT-TE-41 saves the title and closing record, adds conveyance documents and title commitments, changes it and attaches a file', async ({ page, request }) => {
    await ensureLibraryValue(request, 'title_company', 'Lone Star Title')
    await ensureLibraryValue(request, 'conveyance_type', 'Warranty Deed')
    await ensureAgent(request, 'Casey Closer')
    const parcel = await makeParcel(request)
    const admin = await adminHeaders(request)
    for (const [name, type] of [['Fee Person', 'Fee Owner'], ['Vested Person', 'Vested Owner']])
      expect((await request.post(`/api/v1/parcels/${parcel.id}/ownerships`, { headers: admin, data: { ownerName: name, ownerType: type } })).status()).toBe(201)

    await signInViaUi(page, 'TitleResearcher')
    await openTab(page, parcel.id, 'Title and closing')
    const form = page.getByRole('form', { name: 'Title and closing' })
    await expect(form).toContainText('Assigned when saved')

    await form.getByLabel(/^Assigned to/).selectOption('Casey Closer')
    await form.getByLabel(/^Title company/).selectOption('Lone Star Title')
    await form.getByLabel(/^GF number/).fill('GF-100')
    await form.getByLabel(/^Final title policy no/).fill('FTP-7')
    await form.getByLabel(/^Purchase and sale agreement amount/).fill('125000.50')
    await form.getByLabel(/^Purchase and sale agreement signed by owner date/).fill('2026-03-01')
    await form.getByLabel(/^Settlement statement received date/).fill('2026-04-05')
    await form.getByLabel(/^Settlement statement executed date/).fill('2026-04-01')
    await form.getByRole('button', { name: 'Save title and closing' }).click()
    await expect(form.getByText('The statement cannot be executed before it is received.')).toBeVisible()
    await form.getByLabel(/^Settlement statement executed date/).fill('2026-04-08')
    await form.getByLabel('Curative items completed').check()

    await form.getByLabel(/^Conveyance type/).selectOption('Warranty Deed')
    await form.getByLabel(/^Conveyance document signed date/).fill('2026-04-20')
    await form.getByLabel(/^Conveyance document recorded date/).fill('2026-04-25')
    await form.getByLabel(/^Conveyance document recording number/).fill('2026-0001')
    await form.getByRole('button', { name: 'Add conveyance document' }).click()
    await expect(form.getByRole('table', { name: 'Conveyance documents' })).toContainText('2026-0001')

    await form.getByLabel(/^Commitment vested owner name/).fill('Vested Person')
    await form.getByLabel(/^Title commitment requested date/).fill('2026-02-01')
    await form.getByLabel(/^Title commitment issued date/).fill('2026-02-11')
    await form.getByRole('button', { name: 'Add title commitment' }).click()
    await expect(form.getByRole('table', { name: 'Title commitments' })).toContainText('Vested Person')

    await form.getByRole('button', { name: 'Save title and closing' }).click()
    await expect(page.getByText('Title and closing saved.')).toBeVisible()
    await expect(form).toContainText(`TC-${parcel.number}`)
    await expect(form).toContainText('Vested Person')
    await expect(form).not.toContainText('Fee Person')

    // Reload: everything is read back from the server.
    await page.reload()
    await page.getByRole('tab', { name: 'Title and closing' }).click()
    const again = page.getByRole('form', { name: 'Title and closing' })
    await expect(again.getByLabel(/^GF number/)).toHaveValue('GF-100')
    await expect(again.getByLabel(/^Title company/)).toHaveValue('Lone Star Title')
    await expect(again.getByLabel('Curative items completed')).toBeChecked()
    await expect(again.getByRole('table', { name: 'Conveyance documents' })).toContainText('2026-0001')

    await again.getByLabel(/^Actual closing date/).fill('2026-05-02')
    await again.getByRole('button', { name: 'Remove conveyance document 1' }).click()
    await again.getByRole('button', { name: 'Save changes' }).click()
    await expect(page.getByText('Title and closing saved.')).toBeVisible()

    const panel = page.getByRole('region', { name: `Attachments for TC-${parcel.number}` })
    await panel.getByLabel('Upload document').setInputFiles({ name: 'closing-package.txt', mimeType: 'text/plain', buffer: Buffer.from('closing package') })
    await expect(panel.getByText('closing-package.txt')).toBeVisible()

    const headers = { Authorization: `Bearer ${await sessionToken(page)}` }
    const { record } = await (await request.get(`/api/v1/parcels/${parcel.id}/title-closing`, { headers })).json()
    expect(record).toMatchObject({
      number: `TC-${parcel.number}`, assignedTo: 'Casey Closer', titleCompany: 'Lone Star Title', psaAmount: 125000.5, actualClosingDate: '2026-05-02',
      settlementExecutedDate: '2026-04-08', vestedOwnerName: 'Vested Person', conveyanceDocuments: [],
    })
    expect(record.titleCommitments).toHaveLength(1)
  })

  test('AT-NE-41 a negotiator updates the closing dates on a record the title researcher started', async ({ page, request }) => {
    const parcel = await makeParcel(request)
    const tr = { Authorization: `Bearer ${await devToken(request, 'TitleResearcher')}` }
    expect((await request.put(`/api/v1/parcels/${parcel.id}/title-closing`, { headers: tr, data: { gfNumber: 'GF-1', conveyanceDocuments: [], titleCommitments: [] } })).status()).toBe(200)

    await signInViaUi(page, 'Negotiator')
    await openTab(page, parcel.id, 'Title and closing')
    const form = page.getByRole('form', { name: 'Title and closing' })
    await expect(form.getByLabel(/^GF number/)).toHaveValue('GF-1')
    await form.getByLabel(/^Anticipated closing date/).fill('2026-06-01')
    await form.getByRole('button', { name: 'Save changes' }).click()
    await expect(page.getByText('Title and closing saved.')).toBeVisible()

    const headers = { Authorization: `Bearer ${await sessionToken(page)}` }
    const { record } = await (await request.get(`/api/v1/parcels/${parcel.id}/title-closing`, { headers })).json()
    expect(record).toMatchObject({ gfNumber: 'GF-1', anticipatedClosingDate: '2026-06-01' })
    // Negotiators may change the record but not remove it.
    await expect(page.getByRole('button', { name: 'Remove title and closing record' })).toHaveCount(0)
    expect((await request.delete(`/api/v1/parcels/${parcel.id}/title-closing`, { headers })).status()).toBe(403)
  })
})

test.describe('Everyone · title and closing', () => {
  for (const role of ROLES) {
    test(`AT-X-50 ${role}: the Title and closing tab, its buttons and the API follow the access spec`, async ({ page, request }) => {
      const parcel = await makeParcel(request)
      const other = await makeParcel(request)
      const admin = await adminHeaders(request)
      expect((await request.put(`/api/v1/parcels/${parcel.id}/title-closing`, { headers: admin, data: { gfNumber: 'SEED' } })).status()).toBe(200)

      await signInViaUi(page, role)
      await page.goto(`/parcels/${parcel.id}`)
      await expect(page.getByRole('region', { name: 'Parcel synopsis' })).toBeVisible()

      const readable = level(role, 'titleclosing') !== null
      await expect(page.getByRole('tab', { name: 'Title and closing' }), `${role} tab`).toHaveCount(readable ? 1 : 0)
      if (readable) {
        await page.getByRole('tab', { name: 'Title and closing' }).click()
        await expect(page.getByRole('form', { name: 'Title and closing' })).toBeVisible()
        await expect(page.getByRole('button', { name: 'Save changes' }), `${role} save`).toHaveCount(canUpdate(role, 'titleclosing') ? 1 : 0)
        await expect(page.getByRole('button', { name: 'Remove title and closing record' }), `${role} remove`).toHaveCount(canDelete(role, 'titleclosing') ? 1 : 0)
      }

      const headers = { Authorization: `Bearer ${await sessionToken(page)}` }
      const want = (action: 'read' | 'create' | 'update' | 'delete', ok: number) =>
        ({ read: readable, create: canCreate(role, 'titleclosing'), update: canUpdate(role, 'titleclosing'), delete: canDelete(role, 'titleclosing') })[action] ? ok : 403
      expect((await request.get(`/api/v1/parcels/${parcel.id}/title-closing`, { headers })).status()).toBe(want('read', 200))
      // The first save of a parcel's record needs Create, a later one Update.
      expect((await request.put(`/api/v1/parcels/${other.id}/title-closing`, { headers, data: { gfNumber: `by ${role}` } })).status()).toBe(want('create', 200))
      expect((await request.put(`/api/v1/parcels/${parcel.id}/title-closing`, { headers, data: { gfNumber: `edited by ${role}` } })).status()).toBe(want('update', 200))
      expect((await request.delete(`/api/v1/parcels/${parcel.id}/title-closing`, { headers })).status()).toBe(want('delete', 204))
    })
  }
})

test.describe('Everyone · title encumbrances', () => {
  for (const role of ROLES) {
    test(`AT-X-49 ${role}: the Title encumbrances tab, its buttons and the API follow the access spec`, async ({ page, request }) => {
      const parcel = await makeParcel(request)
      const admin = await adminHeaders(request)
      const body = { encumbranceType: 'Lease', documentTitle: 'Seed lease', grantor: 'A', grantee: 'B' }
      const seed = await (await request.post(`/api/v1/parcels/${parcel.id}/encumbrances`, { headers: admin, data: body })).json()

      await signInViaUi(page, role)
      await page.goto(`/parcels/${parcel.id}`)
      await expect(page.getByRole('region', { name: 'Parcel synopsis' })).toBeVisible()

      const readable = level(role, 'titleencumbrances') !== null
      await expect(page.getByRole('tab', { name: 'Title encumbrances' }), `${role} tab`).toHaveCount(readable ? 1 : 0)
      if (readable) {
        await page.getByRole('tab', { name: 'Title encumbrances' }).click()
        await expect(page.getByRole('table', { name: 'Title encumbrances' })).toBeVisible()
        await expect(page.getByRole('form', { name: 'Add an encumbrance' }), `${role} add`).toHaveCount(canCreate(role, 'titleencumbrances') ? 1 : 0)
        await expect(page.getByRole('button', { name: new RegExp(`^Edit encumbrance ENC-${parcel.number}-001$`) }), `${role} edit`).toHaveCount(canUpdate(role, 'titleencumbrances') ? 1 : 0)
        await expect(page.getByRole('button', { name: new RegExp(`^Remove encumbrance ENC-${parcel.number}-001$`) }), `${role} remove`).toHaveCount(canDelete(role, 'titleencumbrances') ? 1 : 0)
      }

      const headers = { Authorization: `Bearer ${await sessionToken(page)}` }
      const url = `/api/v1/parcels/${parcel.id}/encumbrances`
      const want = (action: 'read' | 'create' | 'update' | 'delete', ok: number) =>
        ({ read: readable, create: canCreate(role, 'titleencumbrances'), update: canUpdate(role, 'titleencumbrances'), delete: canDelete(role, 'titleencumbrances') })[action] ? ok : 403
      expect((await request.get(url, { headers })).status()).toBe(want('read', 200))
      expect((await request.post(url, { headers, data: { ...body, documentTitle: `By ${role}` } })).status()).toBe(want('create', 201))
      expect((await request.put(`${url}/${seed.id}`, { headers, data: { ...body, reviewNotes: `edited by ${role}` } })).status()).toBe(want('update', 200))
      expect((await request.delete(`${url}/${seed.id}`, { headers })).status()).toBe(want('delete', 204))
    })
  }
})
