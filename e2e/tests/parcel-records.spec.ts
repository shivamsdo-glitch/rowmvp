import { expect, test, type APIRequestContext, type Page } from '@playwright/test'
import { devToken, level, newId, ROLES, sessionToken, signInViaUi } from './support'

/**
 * Phase 1, slice 3b — a parcel's team, negotiation diary and notes (ROW-019, 020, 021). Real browser, real UI, real API,
 * real Postgres. Case ids match vault/acceptance/<Role>.md. Every test makes its own project and parcel.
 */

const unique = (label: string) => `${label} ${newId().slice(0, 8)}`
const allowed = (role: string, page: string, action: 'read' | 'create' | 'delete') => {
  const l = level(role, page)
  return action === 'read' ? l !== null : action === 'create' ? l === 'S' || l === 'P' : l === 'P'
}
const today = (add = 0) => new Date(Date.now() + add * 86_400_000).toISOString().slice(0, 10)

type Headers = { Authorization: string }
const admin = async (request: APIRequestContext): Promise<Headers> => ({ Authorization: `Bearer ${await devToken(request, 'Admin')}` })

async function makeParcel(request: APIRequestContext, headers: Headers) {
  const project = await (await request.post('/api/v1/projects', { headers, data: { name: unique('Records scene'), oversightType: 'Local' } })).json()
  const res = await request.post('/api/v1/parcels', { headers, data: { projectId: project.id, areaSqFt: 1000, acquisitionType: 'Standard' } })
  expect(res.status(), await res.text()).toBe(201)
  return (await res.json()) as { id: string; number: string }
}

async function openTab(page: Page, parcelId: string, tab: string) {
  await page.goto(`/parcels/${parcelId}`)
  await page.getByRole('tab', { name: tab }).click()
}

test.describe('Project Manager · team and notes', () => {
  test('AT-PM-28 assigns, changes and removes the people on a parcel', async ({ page, request }) => {
    const parcel = await makeParcel(request, await admin(request))
    await signInViaUi(page, 'ProjectManager')
    await openTab(page, parcel.id, 'Team')
    const person = unique('Riley Okafor')

    const form = page.getByRole('form', { name: 'Assign a team member' })
    await form.getByRole('button', { name: 'Assign' }).click()
    await expect(form.getByText('Name is required.')).toBeVisible()
    await form.getByLabel(/^Role/).selectOption({ label: 'Negotiator' })
    await form.getByLabel(/^Name/).fill(person)
    await form.getByLabel('Organization').fill('Okafor Land Services')
    await form.getByLabel(/^Consultant/).check()
    await form.getByLabel('Email').fill('nope')
    await form.getByRole('button', { name: 'Assign' }).click()
    await expect(form.getByText('Enter a valid email address.')).toBeVisible()
    await form.getByLabel('Email').fill('riley@example.com')
    await form.getByRole('button', { name: 'Assign' }).click()

    const table = page.getByRole('table', { name: 'Parcel team' })
    await expect(table.getByRole('row', { name: new RegExp(person) })).toContainText('Consultant')
    await expect(table.getByRole('row', { name: new RegExp(person) })).toContainText('Okafor Land Services')

    // The same person in the same role twice is refused; another role is fine.
    await form.getByLabel(/^Role/).selectOption({ label: 'Negotiator' })
    await form.getByLabel(/^Name/).fill(person.toUpperCase())
    await form.getByRole('button', { name: 'Assign' }).click()
    await expect(form.getByText(/is already the negotiator on this parcel/)).toBeVisible()
    await form.getByLabel(/^Role/).selectOption({ label: 'Counsel' })
    await form.getByRole('button', { name: 'Assign' }).click()
    await expect(table.getByRole('row', { name: /Counsel/ })).toBeVisible()

    await page.getByRole('button', { name: `Edit ${person}, Negotiator` }).click()
    const edit = page.getByRole('form', { name: 'Change team member' })
    await edit.getByLabel(/^Role/).selectOption({ label: 'Appraiser' })
    await edit.getByRole('button', { name: 'Save changes' }).click()
    await expect(table.getByRole('row', { name: /^Appraiser/ })).toContainText(person)

    await page.getByRole('button', { name: `Remove ${person}, Appraiser` }).click()
    await expect(table.getByRole('row', { name: /^Appraiser/ })).toHaveCount(0)
    const pm = { Authorization: `Bearer ${await sessionToken(page)}` }
    const team = await (await request.get(`/api/v1/parcels/${parcel.id}/team`, { headers: pm })).json()
    expect(team.map((m: any) => m.name)).toEqual([person.toUpperCase()])
    expect(team[0].role).toBe('Counsel')
  })

  test('AT-PM-29 writes a note, sees who wrote it, and deletes it', async ({ page, request }) => {
    const parcel = await makeParcel(request, await admin(request))
    await signInViaUi(page, 'ProjectManager')
    await openTab(page, parcel.id, 'Notes')
    const form = page.getByRole('form', { name: 'Add a note' })
    const text = unique('Gate code is 4412')

    await form.getByRole('button', { name: 'Add note' }).click()
    await expect(form.getByText('Write the note first.')).toBeVisible()
    await form.getByLabel(/^Note/).fill(`  ${text}  `)
    await form.getByRole('button', { name: 'Add note' }).click()
    await expect(page.getByText(text)).toBeVisible()
    await expect(page.getByText(/Made by .* on /)).toBeVisible()

    const pm = { Authorization: `Bearer ${await sessionToken(page)}` }
    const notes = await (await request.get(`/api/v1/parcels/${parcel.id}/notes`, { headers: pm })).json()
    expect(notes).toHaveLength(1)
    expect(notes[0].text).toBe(text)

    // Hostile text is shown as text.
    await form.getByLabel(/^Note/).fill('<img src=x onerror=alert(1)>')
    await form.getByRole('button', { name: 'Add note' }).click()
    await expect(page.getByText('<img src=x onerror=alert(1)>')).toBeVisible()

    await page.getByRole('button', { name: new RegExp(`Delete the note "${text.slice(0, 20)}`) }).click()
    await expect(page.getByText(text)).toHaveCount(0)
    expect((await (await request.get(`/api/v1/parcels/${parcel.id}/notes`, { headers: pm })).json())).toHaveLength(1)
  })
})

test.describe('Negotiator · diary', () => {
  test('AT-NG-17 logs contacts in the diary, cannot date one in the future, removes a mistake; the project manager can read but not write', async ({ page, request, browser }) => {
    const parcel = await makeParcel(request, await admin(request))
    await signInViaUi(page, 'Negotiator')
    await openTab(page, parcel.id, 'Diary')
    const form = page.getByRole('form', { name: 'Log a contact' })
    const who = unique('Alex Marlowe')

    await form.getByRole('button', { name: 'Add to diary' }).click()
    await expect(form.getByText('Choose the date.')).toBeVisible()
    await expect(form.getByText('Say who was contacted.')).toBeVisible()
    await expect(form.getByText('Summary is required.')).toBeVisible()

    await form.getByLabel(/^Date of contact/).fill(today(5))
    await form.getByLabel(/^Who was contacted/).fill(who)
    await form.getByLabel(/^What was said/).fill('Explained the offer.')
    await form.getByRole('button', { name: 'Add to diary' }).click()
    await expect(form.getByText('A diary entry cannot be dated in the future.')).toBeVisible()

    await form.getByLabel(/^Date of contact/).fill(today(-3))
    await form.getByLabel(/^What was said/).fill('Explained the offer and the appraisal.')
    await form.getByLabel('What was promised or answered').fill('Will call back Friday')
    await form.getByRole('button', { name: 'Add to diary' }).click()
    await expect(page.getByRole('table', { name: 'Negotiation diary' }).getByRole('row')).toHaveCount(2)
    await form.getByLabel(/^Date of contact/).fill(today())
    await form.getByLabel(/^How/).selectOption({ label: 'Meeting' })
    await form.getByLabel(/^Who started it/).selectOption({ label: 'They contacted us' })
    await form.getByLabel(/^Who was contacted/).fill(`${who} (again)`)
    await form.getByLabel(/^What was said/).fill('Site walk.')
    await form.getByRole('button', { name: 'Add to diary' }).click()

    const table = page.getByRole('table', { name: 'Negotiation diary' })
    await expect(table.getByRole('row')).toHaveCount(3)
    await expect(table.getByRole('row').nth(1)).toContainText(`${who} (again)`) // newest first
    await expect(table.getByRole('row').nth(1)).toContainText('Meeting (in)')
    await expect(table.getByRole('row').nth(2)).toContainText('Will call back Friday')
    await expect(page.getByRole('button', { name: /Print diary/ })).toBeVisible()

    // A project manager reads the diary but has no form and no remove buttons; the server agrees.
    const pmContext = await browser.newContext({ baseURL: new URL(page.url()).origin })
    const pmPage = await pmContext.newPage()
    await signInViaUi(pmPage, 'ProjectManager')
    await openTab(pmPage, parcel.id, 'Diary')
    await expect(pmPage.getByRole('table', { name: 'Negotiation diary' }).getByRole('row')).toHaveCount(3)
    await expect(pmPage.getByRole('form', { name: 'Log a contact' })).toHaveCount(0)
    await expect(pmPage.getByRole('button', { name: /^Remove the/ })).toHaveCount(0)
    const pm = { Authorization: `Bearer ${await sessionToken(pmPage)}` }
    expect((await request.post(`/api/v1/parcels/${parcel.id}/diary`, { headers: pm, data: { occurredOn: today(), method: 'Call', direction: 'Outgoing', contactName: 'X', summary: 'x' } })).status()).toBe(403)
    await pmContext.close()

    // Remove the mistake.
    await page.getByRole('button', { name: new RegExp(`^Remove the .* entry with ${who} \\(again\\)`) }).click()
    await expect(table.getByRole('row')).toHaveCount(2)
    const ng = { Authorization: `Bearer ${await sessionToken(page)}` }
    expect((await (await request.get(`/api/v1/parcels/${parcel.id}/diary`, { headers: ng })).json())).toHaveLength(1)
  })
})

test.describe('Everyone · team, diary and notes', () => {
  for (const role of ROLES) {
    test(`AT-X-38 ${role}: the Team, Diary and Notes controls match the access spec, and the server agrees`, async ({ page, request }) => {
      const a = await admin(request)
      const parcel = await makeParcel(request, a)
      const member = await (await request.post(`/api/v1/parcels/${parcel.id}/team`, { headers: a, data: { role: 'Counsel', name: 'Seed Counsel', isConsultant: false } })).json()
      const entry = await (await request.post(`/api/v1/parcels/${parcel.id}/diary`, { headers: a, data: { occurredOn: today(), method: 'Call', direction: 'Outgoing', contactName: 'Seed', summary: 'seed' } })).json()
      const note = await (await request.post(`/api/v1/parcels/${parcel.id}/notes`, { headers: a, data: { text: 'seed note' } })).json()
      await signInViaUi(page, role)
      const mine = { Authorization: `Bearer ${await sessionToken(page)}` }

      await openTab(page, parcel.id, 'Team')
      await expect(page.getByRole('table', { name: 'Parcel team' })).toBeVisible()
      await expect(page.getByRole('form', { name: 'Assign a team member' })).toHaveCount(allowed(role, 'parcels', 'create') ? 1 : 0)
      await expect(page.getByRole('button', { name: /^Remove Seed Counsel/ })).toHaveCount(allowed(role, 'parcels', 'delete') ? 1 : 0)
      await page.getByRole('tab', { name: 'Notes' }).click()
      await expect(page.getByText('seed note')).toBeVisible()
      await expect(page.getByRole('form', { name: 'Add a note' })).toHaveCount(allowed(role, 'parcels', 'create') ? 1 : 0)
      await expect(page.getByRole('button', { name: /^Delete the note/ })).toHaveCount(allowed(role, 'parcels', 'delete') ? 1 : 0)
      await page.getByRole('tab', { name: 'Diary' }).click()
      if (allowed(role, 'negotiations', 'read')) {
        await expect(page.getByRole('table', { name: 'Negotiation diary' })).toBeVisible()
        await expect(page.getByRole('form', { name: 'Log a contact' })).toHaveCount(allowed(role, 'negotiations', 'create') ? 1 : 0)
        await expect(page.getByRole('button', { name: /^Remove the/ })).toHaveCount(allowed(role, 'negotiations', 'delete') ? 1 : 0)
      } else {
        await expect(page.getByText('Your role cannot see the negotiation diary.')).toBeVisible()
      }

      const code = async (p: Promise<{ status(): number }>) => (await p).status()
      const ok = (page: string, action: 'read' | 'create' | 'delete', yes: number) => (allowed(role, page, action) ? yes : 403)
      expect(await code(request.get(`/api/v1/parcels/${parcel.id}/team`, { headers: mine }))).toBe(ok('parcels', 'read', 200))
      expect(await code(request.get(`/api/v1/parcels/${parcel.id}/notes`, { headers: mine }))).toBe(ok('parcels', 'read', 200))
      expect(await code(request.get(`/api/v1/parcels/${parcel.id}/diary`, { headers: mine }))).toBe(ok('negotiations', 'read', 200))
      expect(await code(request.post(`/api/v1/parcels/${parcel.id}/team`, { headers: mine, data: { role: 'Other', name: `By ${role}`, isConsultant: false } }))).toBe(ok('parcels', 'create', 201))
      expect(await code(request.post(`/api/v1/parcels/${parcel.id}/notes`, { headers: mine, data: { text: `by ${role}` } }))).toBe(ok('parcels', 'create', 201))
      expect(await code(request.post(`/api/v1/parcels/${parcel.id}/diary`, { headers: mine, data: { occurredOn: today(), method: 'Call', direction: 'Outgoing', contactName: role, summary: 'x' } }))).toBe(ok('negotiations', 'create', 201))
      expect(await code(request.delete(`/api/v1/parcels/${parcel.id}/diary/${entry.id}`, { headers: mine }))).toBe(ok('negotiations', 'delete', 204))
      expect(await code(request.delete(`/api/v1/parcels/${parcel.id}/notes/${note.id}`, { headers: mine }))).toBe(ok('parcels', 'delete', 204))
      expect(await code(request.delete(`/api/v1/parcels/${parcel.id}/team/${member.id}`, { headers: mine }))).toBe(ok('parcels', 'delete', 204))
      expect(await code(request.get(`/api/v1/parcels/${parcel.id}/team`))).toBe(401)
    })
  }

  test('AT-X-39 records of another agency or of a split parcel cannot be reached or changed', async ({ page, request }) => {
    const a = await admin(request)
    const parcel = await makeParcel(request, a)
    await request.post(`/api/v1/parcels/${parcel.id}/notes`, { headers: a, data: { text: 'mine' } })
    const other = { Authorization: `Bearer ${await devToken(request, 'Admin', { tenant: newId() })}` }
    for (const part of ['team', 'diary', 'notes']) expect([403, 404]).toContain((await request.get(`/api/v1/parcels/${parcel.id}/${part}`, { headers: other })).status())
    expect([403, 404]).toContain((await request.post(`/api/v1/parcels/${parcel.id}/notes`, { headers: other, data: { text: 'x' } })).status())

    const split = await request.post(`/api/v1/parcels/${parcel.id}/split`, { headers: a, data: { parts: [{ areaSqFt: 300 }] } })
    expect(split.status()).toBe(201)
    const late = await request.post(`/api/v1/parcels/${parcel.id}/notes`, { headers: a, data: { text: 'late' } })
    expect(late.status()).toBe(409)
    expect(await late.text()).toContain('parcel_not_active')
    await signInViaUi(page, 'Admin')
    await openTab(page, parcel.id, 'Notes')
    await expect(page.getByText('mine')).toBeVisible()
    await expect(page.getByRole('form', { name: 'Add a note' })).toHaveCount(0)
    expect((await request.post(`/api/v1/parcels/${parcel.id}/team`, { headers: a, data: { role: 'Other', name: 'late' } })).status()).toBe(409)
  })
})
