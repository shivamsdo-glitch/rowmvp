import { useState } from 'react'
import { useNavigate } from '@tanstack/react-router'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Bell } from 'lucide-react'
import { cn } from '@/lib/cn'
import { useAuthManager } from '@/auth/auth-context'
import { FEATURES } from '@/config/features'

export interface NotificationItem {
  id: string
  kind: string
  title: string
  body: string
  /** In-app path to open when the notification is clicked, if any. */
  link: string | null
  isRead: boolean
  createdAt: string
}

export function relativeTime(iso: string, now = Date.now()): string {
  if (!iso) return ''
  // Server timestamps are UTC; if the string carries no timezone, treat it as UTC.
  const hasTz = /[zZ]|[+-]\d{2}:?\d{2}$/.test(iso)
  const ms = now - new Date(hasTz ? iso : iso + 'Z').getTime()
  if (Number.isNaN(ms)) return ''
  const s = Math.round(ms / 1000)
  if (s < 60) return 'just now'
  const m = Math.round(s / 60)
  if (m < 60) return `${m}m ago`
  const h = Math.round(m / 60)
  if (h < 24) return `${h}h ago`
  return `${Math.round(h / 24)}d ago`
}

/**
 * Bell + dropdown, wired to the planned notifications endpoints. Until the module exists
 * (`FEATURES.notifications`), it renders as an inert icon and makes no requests.
 */
export function NotificationBell() {
  if (!FEATURES.notifications) return <InertBell />
  return <LiveBell />
}

const bellButtonCls =
  'relative w-9 h-9 rounded-md flex items-center justify-center text-muted-foreground transition-colors'

function InertBell() {
  return (
    <button type="button" aria-label="Notifications" disabled title="Notifications are not available yet" className={cn(bellButtonCls, 'opacity-50 cursor-not-allowed')}>
      <Bell size={18} />
    </button>
  )
}

function LiveBell() {
  const { api } = useAuthManager()
  const navigate = useNavigate()
  const qc = useQueryClient()
  const [open, setOpen] = useState(false)

  const { data } = useQuery({
    queryKey: ['notifications'],
    queryFn: () => api.get<NotificationItem[]>('/api/v1/notifications'),
    refetchInterval: 15_000, // so a new item shows up without a reload
    refetchOnWindowFocus: true,
  })
  const list = data ?? []
  const unread = list.filter((n) => !n.isRead).length

  const refresh = () => qc.invalidateQueries({ queryKey: ['notifications'] })
  const markRead = useMutation({ mutationFn: (id: string) => api.post(`/api/v1/notifications/${id}/read`), onSuccess: refresh })
  const markAll = useMutation({ mutationFn: () => api.post('/api/v1/notifications/read-all'), onSuccess: refresh })

  async function openNote(n: NotificationItem) {
    if (!n.isRead) markRead.mutate(n.id)
    setOpen(false)
    // Only same-site paths: a notification body must never be able to navigate off-site.
    if (n.link && /^\/(?!\/)/.test(n.link)) await navigate({ to: n.link as never })
  }

  return (
    <div className="relative">
      <button type="button" aria-label="Notifications" onClick={() => setOpen((o) => !o)} className={cn(bellButtonCls, 'hover:bg-muted hover:text-foreground')}>
        <Bell size={18} />
        {unread > 0 && (
          <span className="absolute top-0.5 right-0.5 min-w-[16px] h-4 px-1 rounded-full bg-destructive text-white text-[10px] font-semibold inline-flex items-center justify-center ring-2 ring-card">
            {unread > 9 ? '9+' : unread}
          </span>
        )}
      </button>

      {open && (
        <>
          <div className="fixed inset-0 z-30" onClick={() => setOpen(false)} />
          <div className="absolute right-0 mt-1.5 w-80 rounded-md border border-border bg-card shadow-lg z-40">
            <div className="px-3 py-2.5 border-b border-border flex items-center justify-between">
              <span className="text-sm font-medium text-foreground">Notifications</span>
              {unread > 0 && (
                <button type="button" onClick={() => markAll.mutate()} className="text-xs text-primary hover:underline">
                  Mark all read
                </button>
              )}
            </div>
            <div className="max-h-96 overflow-y-auto">
              {list.length === 0 && <div className="px-3 py-8 text-center text-sm text-muted-foreground">You're all caught up.</div>}
              {list.map((n) => (
                <button
                  type="button"
                  key={n.id}
                  onClick={() => openNote(n)}
                  className={cn('w-full text-left px-3 py-2.5 border-b border-border last:border-0 hover:bg-muted transition-colors flex gap-2.5', !n.isRead && 'bg-primary/[0.04]')}
                >
                  <span className={cn('mt-1.5 h-2 w-2 rounded-full shrink-0', n.isRead ? 'bg-transparent' : 'bg-destructive')} />
                  <span className="min-w-0">
                    <span className="block text-sm font-medium text-foreground">{n.title}</span>
                    <span className="block text-xs text-muted-foreground leading-snug">{n.body}</span>
                    <span className="block text-[10px] text-muted-foreground mt-0.5">{relativeTime(n.createdAt)}</span>
                  </span>
                </button>
              ))}
            </div>
          </div>
        </>
      )}
    </div>
  )
}
