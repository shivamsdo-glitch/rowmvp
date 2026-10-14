import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { RouterProvider } from '@tanstack/react-router'
import { AuthProvider } from '@/auth/auth-context'
import { AuthManager } from '@/auth/auth-manager'
import { BRAND } from '@/brand/brand'
import { createAppRouter } from './router'
import './index.css'

document.title = BRAND.htmlTitle

const auth = new AuthManager({ baseUrl: import.meta.env.VITE_API_URL })
const router = createAppRouter(auth)
const queryClient = new QueryClient({
  defaultOptions: { queries: { retry: 1, refetchOnWindowFocus: false } },
})

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <QueryClientProvider client={queryClient}>
      <AuthProvider auth={auth}>
        <RouterProvider router={router} />
      </AuthProvider>
    </QueryClientProvider>
  </StrictMode>,
)
