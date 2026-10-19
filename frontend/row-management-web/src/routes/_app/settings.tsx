import { createFileRoute } from '@tanstack/react-router'
import { requirePageAccess } from '@/auth/guards'
import { SettingsPage } from '@/features/settings/SettingsPage'

export const Route = createFileRoute('/_app/settings')({
  beforeLoad: ({ context, location }) => requirePageAccess(context.auth, location.pathname, 'administration'),
  component: SettingsPage,
})
