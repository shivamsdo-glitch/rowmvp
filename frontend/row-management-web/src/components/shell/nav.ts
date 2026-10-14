import {
  BookOpen, FileSearch, FolderKanban, Handshake, LandPlot, LayoutDashboard, Scale, Settings, Users,
} from 'lucide-react'
import type { PageKey } from '@/api/auth'

export interface NavItem {
  to: string
  label: string
  Icon: React.ComponentType<{ size?: number; className?: string }>
  /** The page permission that makes this item visible. */
  page: PageKey
  /** False = the screen is not built yet: shown greyed with a "Soon" tag, not a link. */
  enabled?: boolean
  /** Highlight for any path under this prefix (defaults to `to`). */
  activePrefix?: string
}

export interface NavSection {
  /** Null = the first section, whose header row also carries the collapse toggle. */
  title: string
  items: NavItem[]
}

export const NAV_SECTIONS: NavSection[] = [
  {
    title: 'General',
    items: [
      { to: '/', label: 'Home', Icon: LayoutDashboard, page: 'dashboard' },
      { to: '/projects', label: 'Projects', Icon: FolderKanban, page: 'projects', enabled: false },
      { to: '/parcels', label: 'Parcels', Icon: LandPlot, page: 'parcels', enabled: false },
    ],
  },
  {
    title: 'Acquisition',
    items: [
      { to: '/title', label: 'Title & Survey', Icon: FileSearch, page: 'title', enabled: false },
      { to: '/appraisals', label: 'Appraisal', Icon: Scale, page: 'appraisals', enabled: false },
      { to: '/negotiations', label: 'Negotiation & Closing', Icon: Handshake, page: 'negotiations', enabled: false },
    ],
  },
  {
    title: 'Configure',
    items: [
      { to: '/administration', label: 'Configuration', Icon: Settings, page: 'administration' },
      { to: '/users', label: 'User Management', Icon: Users, page: 'administration', enabled: false },
      { to: '/library', label: 'Library', Icon: BookOpen, page: 'administration', enabled: false },
    ],
  },
]

export function isActive(currentPath: string, item: Pick<NavItem, 'to' | 'activePrefix'>): boolean {
  const prefix = item.activePrefix ?? item.to
  if (prefix === '/') return currentPath === '/'
  return currentPath === prefix || currentPath.startsWith(prefix + '/')
}
