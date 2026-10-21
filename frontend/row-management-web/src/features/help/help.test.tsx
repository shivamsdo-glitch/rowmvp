import { beforeEach, describe, expect, it } from 'vitest'
import { screen, within } from '@testing-library/react'
import userEventModule from '@testing-library/user-event'
import { permissionRowsFor } from '@/test/contract'
import { fakeAuthFetch } from '@/test/fake-api'
import { renderApp } from '@/test/render-app'
import { HELP_SECTIONS, HELP_TOPICS, searchTopics, topicById } from './help-content'

beforeEach(() => sessionStorage.clear())

function open(path: string, role = 'Negotiator', readable?: string[]) {
  return renderApp({ path, fetchImpl: fakeAuthFetch(readable ? { readable, role } : { rows: permissionRowsFor(role), role }) as typeof fetch, signedIn: true })
}

describe('AT-X-60 help content', () => {
  it('has unique topic ids, only known sections, and something to read in every topic', () => {
    expect(new Set(HELP_TOPICS.map((t) => t.id)).size).toBe(HELP_TOPICS.length)
    for (const t of HELP_TOPICS) {
      expect(HELP_SECTIONS).toContain(t.section)
      expect(t.title.length).toBeGreaterThan(3)
      expect(t.summary.length).toBeGreaterThan(10)
      expect(t.blocks.length).toBeGreaterThan(0)
    }
    for (const s of HELP_SECTIONS) expect(HELP_TOPICS.some((t) => t.section === s), s).toBe(true)
  })

  it('searches title, summary and body, needs every word, puts title matches first and lists everything for no text', () => {
    expect(searchTopics('')).toHaveLength(HELP_TOPICS.length)
    expect(searchTopics('   ')).toHaveLength(HELP_TOPICS.length)
    expect(searchTopics('RIGHT of entry').map((t) => t.id)).toContain('rights-of-entry')
    expect(searchTopics('just compensation').length).toBeGreaterThan(0)
    expect(searchTopics('overdue deadlines').map((t) => t.id)).toContain('dashboard-my-work')
    expect(searchTopics('zzzz-not-in-the-help')).toEqual([])
    expect(searchTopics('dashboard')[0].title.toLowerCase()).toContain('dashboard')
    expect(topicById('nope')).toBeUndefined()
  })
})

describe('AT-X-60 the help center', () => {
  it('opens from the question-mark button in the top bar for every signed-in person, even one who can only read the dashboard', async () => {
    const user = userEventModule.setup()
    open('/', 'Surveyor', ['dashboard'])

    await user.click(await screen.findByRole('link', { name: 'Help' }))

    expect(await screen.findByRole('heading', { name: 'Help center' })).toBeInTheDocument()
    const contents = screen.getByRole('navigation', { name: 'Help contents' })
    for (const s of HELP_SECTIONS) expect(within(contents).getByText(s)).toBeInTheDocument()
  })

  it('opens a topic from the contents, with a breadcrumb, a note and previous and next links', async () => {
    const user = userEventModule.setup()
    open('/help')

    await user.click(within(await screen.findByRole('navigation', { name: 'Help contents' })).getByRole('link', { name: 'Key concepts' }))

    expect(await screen.findByRole('heading', { name: 'Key concepts' })).toBeInTheDocument()
    expect(screen.getByRole('navigation', { name: 'Breadcrumb' })).toHaveTextContent('Getting started')
    expect(within(screen.getByRole('navigation', { name: 'Help contents' })).getByRole('link', { name: 'Key concepts' })).toHaveAttribute('aria-current', 'page')
    expect(screen.getByRole('table', { name: /Term/ })).toHaveTextContent('Just compensation')
    await user.click(screen.getByRole('link', { name: /Quick start →/ }))
    expect(await screen.findByRole('heading', { name: 'Quick start' })).toBeInTheDocument()
    expect(screen.getByRole('link', { name: /← Key concepts/ })).toBeInTheDocument()
  })

  it('goes straight to a topic from its address', async () => {
    open('/help?topic=dashboard-team')

    expect(await screen.findByRole('heading', { name: 'The Team view for managers' })).toBeInTheDocument()
  })

  it('shows the welcome page for an unknown topic', async () => {
    open('/help?topic=does-not-exist')

    expect(await screen.findByRole('heading', { name: 'Help center' })).toBeInTheDocument()
  })

  it('searches topics as you type, lists the matches and says when nothing matches', async () => {
    const user = userEventModule.setup()
    open('/help')
    const box = await screen.findByRole('searchbox', { name: 'Search topics' })

    await user.type(box, 'tax id')
    const results = await screen.findByRole('region', { name: 'Search results' })
    expect(within(results).getByRole('link', { name: /Parties and owners/ })).toBeInTheDocument()

    await user.clear(box)
    await user.type(box, 'qqqqzzzz')
    expect(await screen.findByText('No topics match.')).toBeInTheDocument()
  })

  it('lists the pages the signed-in role can open, and how it can use them', async () => {
    open('/help?topic=my-access', 'Surveyor', ['dashboard', 'parcels'])

    const table = await screen.findByRole('table', { name: 'Pages your role can open' })
    expect(within(table).getByRole('row', { name: /Dashboard/ })).toHaveTextContent('Read only')
    expect(within(table).getByRole('row', { name: /Parcels/ })).toBeInTheDocument()
    expect(within(table).queryByText('Configuration and Settings')).not.toBeInTheDocument()
  })

  it('shows a change-capable page as read and change', async () => {
    open('/help?topic=my-access', 'Admin')

    const table = await screen.findByRole('table', { name: 'Pages your role can open' })
    expect(within(table).getByRole('row', { name: /Configuration and Settings/ })).toHaveTextContent('Read and change')
  })

  it('needs a signed-in person', async () => {
    renderApp({ path: '/help', fetchImpl: fakeAuthFetch({ readable: [] }) as typeof fetch })

    expect(await screen.findByText(/Development sign-in/i)).toBeInTheDocument()
  })
})
