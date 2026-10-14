import { Link } from '@tanstack/react-router'
import { cn } from '@/lib/cn'
import { useAuth } from '@/auth/auth-context'
import { isActive, NAV_SECTIONS, type NavItem } from './nav'

function SidebarToggleIcon({ collapsed }: { collapsed: boolean }) {
  return (
    <svg width="16" height="14" viewBox="0 0 16 14" fill="none" aria-hidden>
      <line x1="0" y1="1" x2="9" y2="1" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" />
      <line x1="0" y1="7" x2="9" y2="7" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" />
      <line x1="0" y1="13" x2="9" y2="13" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" />
      {collapsed
        ? <polyline points="12,3 15,7 12,11" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round" />
        : <polyline points="15,3 12,7 15,11" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round" />}
    </svg>
  )
}

export function Sidebar({ collapsed, onToggle, currentPath }: { collapsed: boolean; onToggle: () => void; currentPath: string }) {
  const { manager } = useAuth()
  // Items follow page permissions; a section with nothing visible disappears entirely.
  const sections = NAV_SECTIONS
    .map((s) => ({ ...s, items: s.items.filter((i) => manager.can(i.page, 'read')) }))
    .filter((s) => s.items.length > 0)

  return (
    <aside
      aria-label="Primary"
      className={cn('shrink-0 bg-sidebar flex flex-col transition-all duration-200', collapsed ? 'w-14' : 'w-60')}
    >
      <div className="flex-1 min-h-0 overflow-y-auto overflow-x-hidden" style={{ scrollbarWidth: 'none' }}>
        {sections.map((section, index) =>
          index === 0 ? (
            // First section: the collapse toggle lives in its header row.
            <div key={section.title} className="mt-4">
              <div className={cn('flex items-center mb-1', collapsed ? 'justify-center px-2' : 'px-4')}>
                {!collapsed && (
                  <span className="flex-1 text-[10px] font-semibold uppercase text-sidebar-foreground/60" style={{ letterSpacing: '0.12em' }}>
                    {section.title}
                  </span>
                )}
                <button
                  type="button"
                  aria-label={collapsed ? 'Expand sidebar' : 'Collapse sidebar'}
                  onClick={onToggle}
                  className="flex items-center justify-center text-sidebar-foreground/70 hover:text-white transition-colors"
                >
                  <SidebarToggleIcon collapsed={collapsed} />
                </button>
              </div>
              <nav className="flex flex-col" aria-label={section.title}>
                {section.items.map((item) => (
                  <SidebarItem key={item.label} item={item} collapsed={collapsed} currentPath={currentPath} />
                ))}
              </nav>
            </div>
          ) : (
            <SidebarSection key={section.title} title={section.title} collapsed={collapsed}>
              {section.items.map((item) => (
                <SidebarItem key={item.label} item={item} collapsed={collapsed} currentPath={currentPath} />
              ))}
            </SidebarSection>
          ),
        )}
        <div className="pb-3" />
      </div>
    </aside>
  )
}

export function SidebarSection({ title, collapsed, children }: { title: string; collapsed: boolean; children: React.ReactNode }) {
  return (
    <div className="mt-5">
      {!collapsed && (
        <div className="px-4 mb-1 text-[10px] font-semibold uppercase text-sidebar-foreground/60" style={{ letterSpacing: '0.12em' }}>
          {title}
        </div>
      )}
      <nav className="flex flex-col" aria-label={title}>{children}</nav>
    </div>
  )
}

export function SidebarItem({ item, collapsed, currentPath }: { item: NavItem; collapsed: boolean; currentPath: string }) {
  const { to, label, Icon, enabled = true } = item
  const active = isActive(currentPath, item)
  const baseCls = cn(
    'group h-9 flex items-center gap-3 text-sm transition-colors mx-2 my-0.5 rounded-lg',
    collapsed ? 'justify-center' : 'pl-3 pr-3',
    enabled
      ? active
        ? 'bg-sidebar-active text-white font-medium border border-white/10 border-l-[3px] border-l-white/60'
        : 'text-sidebar-foreground hover:bg-sidebar-hover hover:text-white'
      : 'text-sidebar-foreground/40 cursor-not-allowed',
  )
  const content = (
    <>
      <Icon size={16} className="shrink-0" />
      {!collapsed && <span className="truncate">{label}</span>}
      {!collapsed && !enabled && (
        <span className="ml-auto text-[9px] uppercase tracking-wider text-sidebar-foreground/50">Soon</span>
      )}
    </>
  )

  if (!enabled) {
    return (
      <div className={baseCls} title={`${label} (coming soon)`} aria-disabled="true">
        {content}
      </div>
    )
  }
  return (
    <Link to={to as never} className={baseCls} title={label} aria-current={active ? 'page' : undefined}>
      {content}
    </Link>
  )
}
