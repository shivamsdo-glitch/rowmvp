import { Link } from '@tanstack/react-router'
import { CircleHelp, Signpost } from 'lucide-react'
import { BRAND } from '@/brand/brand'
import { AccountMenu } from './AccountMenu'
import { NotificationBell } from './NotificationBell'

/** Logo image when one is configured, otherwise a text wordmark with a generic mark. */
function Logo() {
  if (BRAND.logoSrc) {
    return <img src={BRAND.logoSrc} alt={BRAND.productName} className="h-9 w-auto opacity-95 group-hover:opacity-100 transition-opacity" />
  }
  return (
    <span className="flex items-center gap-2">
      <span className="flex h-8 w-8 items-center justify-center rounded-md bg-primary text-primary-foreground" aria-hidden>
        <Signpost size={18} />
      </span>
      <span className="text-base font-semibold tracking-tight text-foreground">{BRAND.productName}</span>
    </span>
  )
}

export function TopBar() {
  return (
    <header className="h-14 flex items-center justify-between bg-card border-b border-border px-3 relative z-20">
      <Link to="/" className="flex items-center group" aria-label={`${BRAND.productName} home`}>
        <Logo />
      </Link>
      <div className="flex items-center gap-0.5">
        <Link
          to="/help"
          aria-label="Help"
          title="Help"
          className="w-9 h-9 flex items-center justify-center rounded-md text-muted-foreground hover:bg-muted hover:text-foreground transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
        >
          <CircleHelp size={20} />
        </Link>
        <NotificationBell />
        <div className="w-px h-6 bg-border mx-1.5" />
        <AccountMenu />
      </div>
    </header>
  )
}
