import { createRootRouteWithContext, Outlet } from '@tanstack/react-router'
import type { RouterContext } from '@/auth/guards'

export const Route = createRootRouteWithContext<RouterContext>()({
  component: () => <Outlet />,
})
