import { useMemo, useState } from 'react'
import { Link } from '@tanstack/react-router'
import { ChevronRight, Printer, Search } from 'lucide-react'
import { useAuth } from '@/auth/auth-context'
import { DataTable } from '../projects/form-kit'
import { HELP_SECTIONS, HELP_TOPICS, searchTopics, topicById, type HelpBlock, type HelpTopic } from './help-content'

/** What each page key is called to a person. */
export const PAGE_LABELS: Record<string, string> = {
  dashboard: 'Dashboard', projects: 'Projects', parcels: 'Parcels', parties: 'Parties', approvals: 'Requests and approvals', title: 'Title & Survey screen',
  survey: 'Survey', appraisals: 'Appraisal screen', negotiations: 'Negotiation & Closing screen', agreements: 'Agreements', closing: 'Closing', payments: 'Payments',
  administration: 'Configuration and Settings', library: 'Library', contactlog: 'Parcel tab: Contact log', easements: 'Parcel tab: Easements',
  parcellocation: 'Parcel tab: Parcel location', titleencumbrances: 'Parcel tab: Title encumbrances', titleclosing: 'Parcel tab: Title and closing', parcelappraisal: 'Parcel tab: Appraisal',
  parcelnegotiation: 'Parcel tab: Negotiation', parcelquestionnaire: 'Parcel tab: Questionnaire', parcelcondemnation: 'Parcel tab: Condemnation', parcelproperty: 'Parcel tab: Property management & disposition',
  parcelrelocation: 'Parcel tab: Relocation', relocations: 'Relocations screen',
}

function MyAccess() {
  const { permissions, user } = useAuth()
  const rows = permissions.filter((p) => p.canRead).map((p) => [PAGE_LABELS[p.pageKey] ?? p.pageKey, p.canCreate || p.canUpdate || p.canDelete ? 'Read and change' : 'Read only'])
  return (
    <div className="my-3">
      <p className="mb-2 text-sm">Signed in as <strong>{user?.fullName}</strong> ({user?.roles.join(', ')}).</p>
      {rows.length === 0 ? <p className="text-sm text-muted-foreground">Your role cannot open any page yet. Ask your Administrator.</p> : (
        <DataTable caption="Pages your role can open" headers={['Page', 'Your access']}>
          {rows.map(([page, access]) => <tr key={page}><td className="px-3 py-2">{page}</td><td className="px-3 py-2">{access}</td></tr>)}
        </DataTable>
      )}
    </div>
  )
}

function Block({ block }: { block: HelpBlock }) {
  switch (block.kind) {
    case 'p': return <p className="my-3 leading-7">{block.text}</p>
    case 'note': return <p className="my-4 rounded-r border-l-4 border-primary bg-primary-soft px-4 py-3 text-sm"><strong>Note: </strong>{block.text}</p>
    case 'steps': return (
      <div className="my-3">
        {block.title && <h3 className="mb-1 mt-5 text-base font-semibold">{block.title}</h3>}
        <ol className="list-decimal space-y-1.5 pl-6 leading-7">{block.items.map((i) => <li key={i}>{i}</li>)}</ol>
      </div>
    )
    case 'list': return (
      <div className="my-3">
        {block.title && <h3 className="mb-1 mt-5 text-base font-semibold">{block.title}</h3>}
        <ul className="list-disc space-y-1.5 pl-6 leading-7">{block.items.map((i) => <li key={i}>{i}</li>)}</ul>
      </div>
    )
    case 'table': return (
      <div className="my-4">
        <DataTable caption={block.head.join(' / ')} headers={block.head}>
          {block.rows.map((r) => <tr key={r[0]}>{r.map((c, i) => <td key={i} className={i === 0 ? 'px-3 py-2 font-medium' : 'px-3 py-2'}>{c}</td>)}</tr>)}
        </DataTable>
      </div>
    )
    case 'my-access': return <MyAccess />
  }
}

function Article({ topic }: { topic: HelpTopic }) {
  const i = HELP_TOPICS.findIndex((t) => t.id === topic.id)
  const prev = HELP_TOPICS[i - 1]
  const next = HELP_TOPICS[i + 1]
  return (
    <article aria-label={topic.title} className="max-w-3xl">
      <nav aria-label="Breadcrumb" className="mb-3 flex flex-wrap items-center gap-1 text-xs text-muted-foreground">
        <Link to="/help" className="hover:text-foreground">Help</Link><ChevronRight size={12} aria-hidden />
        <span>{topic.section}</span><ChevronRight size={12} aria-hidden />
        <span className="text-foreground/80">{topic.title}</span>
      </nav>
      <h1 className="page-title mb-1">{topic.title}</h1>
      <p className="text-muted-foreground">{topic.summary}</p>
      <div className="hairline my-4" />
      {topic.blocks.map((b, k) => <Block key={k} block={b} />)}
      <div className="mt-8 flex justify-between gap-3 border-t pt-4 text-sm">
        {prev ? <Link to="/help" search={{ topic: prev.id }} className="text-primary hover:underline">← {prev.title}</Link> : <span />}
        {next ? <Link to="/help" search={{ topic: next.id }} className="text-right text-primary hover:underline">{next.title} →</Link> : <span />}
      </div>
    </article>
  )
}

function Welcome() {
  return (
    <div className="max-w-3xl">
      <h1 className="page-title mb-1">Help center</h1>
      <p className="text-muted-foreground">Everything you need to get the most out of Right of Way Management. Pick a topic on the left, or search.</p>
      <div className="hairline my-4" />
      <div className="grid gap-4 sm:grid-cols-2">
        {HELP_SECTIONS.map((s) => (
          <section key={s} aria-label={s} className="rounded-lg border bg-card p-4">
            <h2 className="mb-2 text-sm font-semibold">{s}</h2>
            <ul className="space-y-1 text-sm">
              {HELP_TOPICS.filter((t) => t.section === s).map((t) => <li key={t.id}><Link to="/help" search={{ topic: t.id }} className="text-primary hover:underline">{t.title}</Link></li>)}
            </ul>
          </section>
        ))}
      </div>
    </div>
  )
}

export function HelpPage({ topicId }: { topicId?: string }) {
  const [text, setText] = useState('')
  const found = useMemo(() => searchTopics(text), [text])
  const topic = topicById(topicId)
  const searching = text.trim() !== ''

  return (
    <div className="flex flex-col gap-6 lg:flex-row" data-testid="help-center">
      <aside aria-label="Help topics" className="shrink-0 lg:w-72">
        <div className="mb-4 flex items-center justify-between">
          <span className="eyebrow">Help center</span>
          <button type="button" onClick={() => window.print()} aria-label="Print this topic" className="rounded p-1 text-muted-foreground hover:bg-muted hover:text-foreground"><Printer size={16} /></button>
        </div>
        <label className="relative mb-4 block">
          <span className="sr-only">Search topics</span>
          <Search size={14} className="pointer-events-none absolute left-3 top-1/2 -translate-y-1/2 text-muted-foreground" aria-hidden />
          <input type="search" value={text} onChange={(e) => setText(e.target.value)} placeholder="Search topics…" className="w-full rounded-md border bg-card py-2 pl-8 pr-3 text-sm focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring" />
        </label>
        {searching ? (
          <div role="region" aria-label="Search results" aria-live="polite">
            <p className="mb-2 text-xs text-muted-foreground">{found.length === 0 ? 'No topics match.' : `${found.length} topic${found.length === 1 ? '' : 's'} found`}</p>
            <ul className="space-y-1 text-sm">
              {found.map((t) => (
                <li key={t.id}>
                  <Link to="/help" search={{ topic: t.id }} className="block rounded px-2 py-1.5 hover:bg-muted">
                    <span className="font-medium text-primary">{t.title}</span>
                    <span className="block text-xs text-muted-foreground">{t.summary}</span>
                  </Link>
                </li>
              ))}
            </ul>
          </div>
        ) : (
          <nav aria-label="Help contents" className="space-y-4">
            {HELP_SECTIONS.map((s) => (
              <div key={s}>
                <div className="px-2 pb-1 text-sm font-semibold">{s}</div>
                <ul>
                  {HELP_TOPICS.filter((t) => t.section === s).map((t) => (
                    <li key={t.id}>
                      <Link to="/help" search={{ topic: t.id }} aria-current={t.id === topic?.id ? 'page' : undefined}
                        className={`block rounded px-2 py-1.5 text-sm ${t.id === topic?.id ? 'bg-primary-soft font-medium text-primary' : 'text-foreground/80 hover:bg-muted'}`}>
                        {t.title}
                      </Link>
                    </li>
                  ))}
                </ul>
              </div>
            ))}
          </nav>
        )}
      </aside>
      <div className="min-w-0 flex-1">{topic ? <Article topic={topic} /> : <Welcome />}</div>
    </div>
  )
}
