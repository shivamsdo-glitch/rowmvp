import { render } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { createMemoryHistory, RouterProvider } from '@tanstack/react-router'
import { AuthProvider } from '@/auth/auth-context'
import { AuthManager } from '@/auth/auth-manager'
import { createAppRouter } from '@/router'

/** Mounts the real router and shell over a fake fetch. Optionally pre-signs-in by storing the token. */
export function renderApp(opts: { path: string; fetchImpl: typeof fetch; signedIn?: boolean; token?: string }) {
  if (opts.signedIn) sessionStorage.setItem('row.auth.token', opts.token ?? 'good-token')
  const auth = new AuthManager({ fetchImpl: opts.fetchImpl })
  const router = createAppRouter(auth, createMemoryHistory({ initialEntries: [opts.path] }))
  const view = render(
    <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
      <AuthProvider auth={auth}>
        <RouterProvider router={router} />
      </AuthProvider>
    </QueryClientProvider>,
  )
  return { auth, router, ...view }
}
