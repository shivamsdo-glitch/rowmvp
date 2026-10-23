import { expect, test, type APIRequestContext, type Page } from '@playwright/test'
import { canUpdate, devToken, level, newId, ROLES, sessionToken, signInViaUi, PARCEL_TAB_OF , sectionVisible } from './support'

/**
 * Phase 1.5 slice 5b (ADR 0017): a parcel's Contact Log and Easements tabs, with attachments. Real browser, UI, API and Postgres.
 * Case ids match vault/acceptance/Negotiator.md, Surveyor.md and Everyone.md. Every test makes its own project and parcel.
 */

const canCreate = (role: string, page: string) => ['S', 'P'].includes(level(role, page) ?? '')
const canDelete = (role: string, page: string) => level(role, page) === 'P'
const unique = (label: string) => `${label} ${newId().slice(0, 8)}`

async function adminHeaders(request: APIRequestContext) {
  return { Authorization: `Bearer ${await devToken(request, 'Admin')}` }
}

/** The Library lists these two forms offer start empty; give them values the way an administrator would (ignoring "already there"). */
async function ensureLibraryValue(request: APIRequestContext, key: string, value: string) {
  const headers = await adminHeaders(request)
  const fields = (await (await request.get('/api/v1/library/fields', { headers })).json()) as { id: string; key: string }[]
  const id = fields.find((f) => f.key === key)!.id
  const res = await request.post(`/api/v1/library/fields/${id}/values`, { headers, data: { value } })
  expect([200, 201, 409]).toContain(res.status())
}

async function makeParcel(request: APIRequestContext) {
  const headers = await adminHeaders(request)
  const project = await (await request.post('/api/v1/projects', { headers, data: { name: unique('Tabs scene'), oversightType: 'Local', phase: 'Phase 1' } })).json()
  const parcel = await (await request.post('/api/v1/parcels', { headers, data: { projectId: project.id, areaSqFt: 1000, acquisitionType: 'Standard', address: 'Tabs Rd' } })).json()
  return parcel as { id: string; number: string }
}

async function openTab(page: Page, parcelId: string, tab: string) {
  await page.goto(`/parcels/${parcelId}`)
  await expect(page.getByRole('region', { name: 'Parcel synopsis' })).toBeVisible()
  await page.getByRole('tab', { name: PARCEL_TAB_OF[tab] ?? tab, exact: true }).click()
}

test.describe('Negotiator · contact log', () => {
  test('AT-NE-40 logs a contact with linked records and a file, changes it, and removes it', async ({ page, request }) => {
    await ensureLibraryValue(request, 'mode_of_contact', 'Phone')
    const parcel = await makeParcel(request)
    await signInViaUi(page, 'Negotiator')
    await openTab(page, parcel.id, 'Contact log')
    const form = page.getByRole('form', { name: 'Log a contact' })

    await form.getByRole('button', { name: 'Add contact' }).click()
    await expect(form.getByText('Enter the date and time of the contact.')).toBeVisible()
    await form.getByLabel(/^Date and time of contact/).fill('2026-10-02T09:15')
    await form.getByLabel(/^Mode of contact/).selectOption('Phone')
    await form.getByLabel(/^Contact person/).fill('Dana Owner')
    await form.getByLabel(/^Discussion notes/).fill('Walked the boundary.')
    await form.getByLabel(/^Record identifier/).fill('OFFER-7')
    await form.getByLabel(/^Module name/).fill('Negotiation')
    await form.getByRole('button', { name: 'Add linked record' }).click()
    await form.getByRole('button', { name: 'Add contact' }).click()

    const number = `CL-${parcel.number}-001`
    const row = page.getByRole('row', { name: new RegExp(number) })
    await expect(row).toContainText('Phone')
    await expect(row).toContainText('Dana Owner')

    // Change it: the id stays, the linked record can be removed, and a file can be attached.
    await page.getByRole('button', { name: `Open contact ${number}` }).click()
    const edit = page.getByRole('form', { name: 'Change contact' })
    await expect(edit.getByLabel(/^Discussion notes/)).toHaveValue('Walked the boundary.')
    await edit.getByRole('button', { name: 'Remove linked record OFFER-7' }).click()
    await edit.getByLabel(/^Discussion notes/).fill('Changed notes.')
    await edit.getByRole('button', { name: 'Save changes' }).click()
    await expect(page.getByRole('row', { name: new RegExp(number) })).toContainText('Changed notes.')

    await page.getByRole('button', { name: `Open contact ${number}` }).click()
    const panel = page.getByRole('region', { name: `Attachments for ${number}` })
    await expect(panel.getByText('No attachments')).toBeVisible()
    await panel.getByLabel('Upload document').setInputFiles({ name: 'owner-letter.txt', mimeType: 'text/plain', buffer: Buffer.from('letter to the owner') })
    await expect(panel.getByText('owner-letter.txt')).toBeVisible()

    // The server holds what the screens showed.
    const headers = { Authorization: `Bearer ${await sessionToken(page)}` }
    const log = await (await request.get(`/api/v1/parcels/${parcel.id}/contact-log`, { headers })).json()
    expect(log).toHaveLength(1)
    expect(log[0]).toMatchObject({ number, mode: 'Phone', contactPerson: 'Dana Owner', notes: 'Changed notes.', links: [] })
    const docs = await (await request.get(`/api/v1/documents?entityType=ContactLog&entityId=${log[0].id}`, { headers })).json()
    expect(docs).toHaveLength(1)

    await panel.getByRole('button', { name: 'Remove owner-letter.txt' }).click()
    await expect(panel.getByText('No attachments')).toBeVisible()
    await page.getByRole('button', { name: 'Back to the contact log' }).click()
    await page.getByRole('button', { name: `Remove contact ${number}` }).click()
    await expect(page.getByText('No contacts logged')).toBeVisible()
  })

  test('AT-NE-40 the next contact on a parcel gets the next number', async ({ page, request }) => {
    const parcel = await makeParcel(request)
    const headers = await adminHeaders(request)
    for (const n of [1, 2]) {
      const r = await request.post(`/api/v1/parcels/${parcel.id}/contact-log`, { headers, data: { contactedAt: `2026-10-0${n}T10:00:00Z`, notes: `call ${n}` } })
      expect(r.status()).toBe(201)
    }
    await signInViaUi(page, 'Negotiator')
    await openTab(page, parcel.id, 'Contact log')
    const rows = page.getByRole('table', { name: 'Contact log' }).getByRole('row')
    await expect(rows.nth(1)).toContainText(`CL-${parcel.number}-002`)
    await expect(rows.nth(2)).toContainText(`CL-${parcel.number}-001`)
  })
})

test.describe('Surveyor · easements', () => {
  test('AT-SU-40 adds, changes and removes an easement, with the form refusing bad entries', async ({ page, request }) => {
    await ensureLibraryValue(request, 'easement_type', 'Sidewalk Easement')
    const parcel = await makeParcel(request)
    await signInViaUi(page, 'Surveyor')
    await openTab(page, parcel.id, 'Easements')
    const form = page.getByRole('form', { name: 'Add an easement' })

    await form.getByRole('button', { name: 'Add easement' }).click()
    await expect(form.getByText('Easement number is required.')).toBeVisible()
    await form.getByLabel(/^Easement number/).fill('E-1')
    await form.getByLabel(/^Start date/).fill('2026-05-01')
    await form.getByLabel(/^End date/).fill('2026-04-01')
    await form.getByRole('button', { name: 'Add easement' }).click()
    await expect(form.getByText('The end date cannot be before the start date.')).toBeVisible()

    await form.getByLabel(/^End date/).fill('2036-05-01')
    await form.getByLabel(/^Easement type/).selectOption('Sidewalk Easement')
    await form.getByLabel(/^Description/).fill('Along Main Street')
    await form.getByLabel(/^Start station/).fill('10+00')
    await form.getByLabel(/^End station/).fill('12+50')
    await form.getByRole('button', { name: 'Add easement' }).click()
    const row = page.getByRole('row', { name: /E-1/ })
    await expect(row).toContainText('Sidewalk Easement')
    await expect(row).toContainText('12+50')

    // A second easement with the same number is refused by the server and said so on the screen.
    await page.getByRole('form', { name: 'Add an easement' }).getByLabel(/^Easement number/).fill('E-1')
    await page.getByRole('form', { name: 'Add an easement' }).getByRole('button', { name: 'Add easement' }).click()
    await expect(page.getByText("This parcel already has an easement numbered 'E-1'.")).toBeVisible()

    await page.getByRole('button', { name: 'Open easement E-1' }).click()
    const edit = page.getByRole('form', { name: 'Change easement' })
    await edit.getByLabel(/^End station/).fill('13+00')
    await edit.getByRole('button', { name: 'Save changes' }).click()
    await expect(page.getByRole('row', { name: /E-1/ })).toContainText('13+00')

    const headers = { Authorization: `Bearer ${await sessionToken(page)}` }
    const list = await (await request.get(`/api/v1/parcels/${parcel.id}/easements`, { headers })).json()
    expect(list).toHaveLength(1)
    expect(list[0]).toMatchObject({ number: 'E-1', endStation: '13+00', startDate: '2026-05-01', endDate: '2036-05-01' })

    await page.getByRole('button', { name: 'Remove easement E-1' }).click()
    await expect(page.getByText('No easements recorded')).toBeVisible()
  })
})

test.describe('Everyone · parcel tabs', () => {
  for (const role of ROLES) {
    test(`AT-X-46 ${role}: the Contact log and Easements tabs, their buttons and the API follow the access spec`, async ({ page, request }) => {
      const parcel = await makeParcel(request)
      const admin = await adminHeaders(request)
      const log = await (await request.post(`/api/v1/parcels/${parcel.id}/contact-log`, { headers: admin, data: { contactedAt: '2026-10-01T10:00:00Z', notes: 'seed' } })).json()
      const easement = await (await request.post(`/api/v1/parcels/${parcel.id}/easements`, { headers: admin, data: { number: 'SEED' } })).json()

      await signInViaUi(page, role)
      await page.goto(`/parcels/${parcel.id}`)
      await expect(page.getByRole('region', { name: 'Parcel synopsis' })).toBeVisible()

      for (const [tab, name, pageKey] of [['Contact log', 'Contact log', 'contactlog'], ['Easements', 'Easements', 'easements']] as const) {
        const readable = level(role, pageKey) !== null
        expect(await sectionVisible(page, tab), `${role} ${tab} section`).toBe(readable)
        if (!readable) continue
        await page.getByRole('tab', { name: PARCEL_TAB_OF[tab] ?? tab, exact: true }).click()
        await expect(page.getByRole('table', { name })).toBeVisible()
        const form = tab === 'Contact log' ? 'Log a contact' : 'Add an easement'
        const edit = tab === 'Contact log' ? new RegExp(`^Open contact CL-${parcel.number}-001$`) : 'Open easement SEED'
        const remove = tab === 'Contact log' ? new RegExp(`^Remove contact CL-${parcel.number}-001$`) : 'Remove easement SEED'
        await expect(page.getByRole('form', { name: form }), `${role} ${tab} add`).toHaveCount(canCreate(role, pageKey) ? 1 : 0)
        await expect(page.getByRole('button', { name: edit }), `${role} ${tab} edit`).toHaveCount(1)
        await expect(page.getByRole('button', { name: remove }), `${role} ${tab} remove`).toHaveCount(canDelete(role, pageKey) ? 1 : 0)
      }

      // The server answers the same, whatever the screen shows.
      const headers = { Authorization: `Bearer ${await sessionToken(page)}` }
      const code = (r: { status(): number }) => r.status()
      const logUrl = `/api/v1/parcels/${parcel.id}/contact-log`
      const easeUrl = `/api/v1/parcels/${parcel.id}/easements`
      const want = (page: string, action: 'read' | 'create' | 'update' | 'delete', ok: number) =>
        ({ read: level(role, page) !== null, create: canCreate(role, page), update: canUpdate(role, page), delete: canDelete(role, page) })[action] ? ok : 403
      expect(code(await request.get(logUrl, { headers }))).toBe(want('contactlog', 'read', 200))
      expect(code(await request.post(logUrl, { headers, data: { contactedAt: '2026-10-03T10:00:00Z', notes: `by ${role}` } }))).toBe(want('contactlog', 'create', 201))
      expect(code(await request.put(`${logUrl}/${log.id}`, { headers, data: { contactedAt: '2026-10-01T10:00:00Z', notes: `edited by ${role}` } }))).toBe(want('contactlog', 'update', 200))
      expect(code(await request.get(easeUrl, { headers }))).toBe(want('easements', 'read', 200))
      expect(code(await request.post(easeUrl, { headers, data: { number: `BY-${role}` } }))).toBe(want('easements', 'create', 201))
      expect(code(await request.put(`${easeUrl}/${easement.id}`, { headers, data: { number: 'SEED', description: `by ${role}` } }))).toBe(want('easements', 'update', 200))
      expect(code(await request.delete(`${logUrl}/${log.id}`, { headers }))).toBe(want('contactlog', 'delete', 204))
      expect(code(await request.delete(`${easeUrl}/${easement.id}`, { headers }))).toBe(want('easements', 'delete', 204))
    })
  }
})
