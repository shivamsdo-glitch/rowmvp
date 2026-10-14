import { createFileRoute, Link } from '@tanstack/react-router'
import { ShieldAlert } from 'lucide-react'
import { EmptyState } from '@/components/ui/states'

export const Route = createFileRoute('/_app/forbidden')({
  component: () => (
    <EmptyState
      icon={ShieldAlert}
      title="You don't have access to that page"
      description="Ask an administrator if you think you should."
      action={<Link to="/" className="text-sm underline">Back to the dashboard</Link>}
    />
  ),
})
