import { beforeEach, describe, expect, it } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEventModule from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { createMemoryHistory, RouterProvider } from '@tanstack/react-router'
import { AuthProvider } from '@/auth/auth-context'
import { AuthManager } from '@/auth/auth-manager'
import { BRAND } from '@/brand/brand'
import { createAppRouter } from '@/router'
import { fakeAuthFetch } from '@/test/fake-api'
import { isActive, NAV_SECTIONS } from './nav'
import { relativeTime } from './NotificationBell'

beforeEach(() => sessionStorage.clear())

async function openShell(readable: string[]) {
  sessionStorage.setItem('row.auth.token', 'good-token')
  const auth = new AuthManager({ fetchImpl: fakeAuthFetch({ readable }) as typeof fetch })
  const router = createAppRouter(auth, createMemoryHistory({ initialEntries: ['/'] }))
  render(
    <QueryClientProvider client={new QueryClient()}>
      <AuthProvider auth={auth}>
        <RouterProvider router={router} />
      </AuthProvider>
    </QueryClientProvider>,
  )
  await screen.findByRole('heading', { name: 'Dashboard' })
}

const ALL = NAV_SECTIONS.flatMap((s) => s.items.map((i) => i.page))

describe('sidebar', () => {
  it('shows ROW sections and items to a role that can read everything', async () => {
    await openShell(ALL)

    for (const heading of ['General', 'Acquisition', 'Configure']) expect(screen.getByText(heading)).toBeInTheDocument()
    for (const label of ['Home', 'Projects', 'Parcels', 'Title & Survey', 'Appraisal', 'Negotiation & Closing', 'Configuration', 'User Management', 'Library']) {
      expect(screen.getByTitle(new RegExp(`^${label.replace('&', '\\&')}`))).toBeInTheDocument()
    }
  })

  it('shows built screens as links and screens that are not built yet as disabled "Soon" items', async () => {
    await openShell(ALL)

    expect(screen.getByTitle('Title & Survey (coming soon)')).toHaveAttribute('aria-disabled', 'true')
    expect(screen.queryByRole('link', { name: /Title & Survey/ })).not.toBeInTheDocument()
    expect(screen.getByRole('link', { name: /Projects/ })).toBeInTheDocument()
    expect(screen.getByRole('link', { name: /Parcels/ })).toHaveAttribute('href', '/parcels')
    expect(screen.getAllByText('Soon').length).toBeGreaterThan(0)
  })

  it('hides items the role cannot read, and a section with nothing visible disappears', async () => {
    await openShell(['dashboard', 'title'])

    expect(screen.getByTitle(/^Title & Survey/)).toBeInTheDocument()
    expect(screen.queryByTitle(/^Appraisal/)).not.toBeInTheDocument()
    expect(screen.queryByText('Configure')).not.toBeInTheDocument()
  })

  it('collapses to icons and expands again', async () => {
    const user = userEventModule.setup()
    await openShell(ALL)
    expect(screen.getByText('Acquisition')).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Collapse sidebar' }))

    expect(screen.queryByText('Acquisition')).not.toBeInTheDocument()
    expect(screen.getByRole('link', { name: /Home/ }).textContent).toBe('')
    await user.click(screen.getByRole('button', { name: 'Expand sidebar' }))
    expect(screen.getByText('Acquisition')).toBeInTheDocument()
  })
})

describe('top bar', () => {
  it('shows the product name wordmark (no logo image yet) linking home', async () => {
    await openShell(ALL)

    expect(BRAND.logoSrc).toBeNull()
    expect(screen.getByRole('link', { name: `${BRAND.productName} home` })).toHaveTextContent('ROW Management')
  })

  it('renders an inert bell while the notifications module does not exist', async () => {
    await openShell(ALL)

    const bell = screen.getByRole('button', { name: 'Notifications' })
    expect(bell).toBeDisabled()
    await waitFor(() => expect(bell).toBeDisabled())
  })
})

describe('isActive', () => {
  it('matches Home only on the exact root path', () => {
    expect(isActive('/', { to: '/' })).toBe(true)
    expect(isActive('/projects', { to: '/' })).toBe(false)
  })

  it('matches an item for its path and sub-paths but not look-alikes', () => {
    expect(isActive('/projects', { to: '/projects' })).toBe(true)
    expect(isActive('/projects/42/parcels', { to: '/projects' })).toBe(true)
    expect(isActive('/projects-archive', { to: '/projects' })).toBe(false)
  })

  it('honours an explicit activePrefix', () => {
    expect(isActive('/negotiations/7', { to: '/negotiations', activePrefix: '/negotiations' })).toBe(true)
    expect(isActive('/closing', { to: '/negotiations', activePrefix: '/negotiations' })).toBe(false)
  })
})

describe('relativeTime', () => {
  const now = Date.parse('2026-10-14T12:00:00Z')

  it.each([
    ['2026-10-14T11:59:40Z', 'just now'],
    ['2026-10-14T11:55:00Z', '5m ago'],
    ['2026-10-14T09:00:00Z', '3h ago'],
    ['2026-10-12T12:00:00Z', '2d ago'],
  ])('%s -> %s', (iso, expected) => expect(relativeTime(iso, now)).toBe(expected))

  it('treats a timestamp without a timezone as UTC', () => {
    expect(relativeTime('2026-10-14T11:55:00', now)).toBe('5m ago')
  })

  it('returns empty for junk', () => {
    expect(relativeTime('', now)).toBe('')
    expect(relativeTime('not a date', now)).toBe('')
  })
})
