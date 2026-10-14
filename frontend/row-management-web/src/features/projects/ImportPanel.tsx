import { useState } from 'react'
import { OVERSIGHT_TYPES, type ImportSummary, type OversightType, type ProjectInput } from '@/api/projects'
import { buttonClassName } from '@/components/ui/button'
import { DataTable, FormError, messageOf, numOrNull, orNull } from './form-kit'

/** Splits CSV text into rows of cells. Handles quoted cells, escaped quotes ("") and commas/newlines inside quotes. */
export function parseCsv(text: string): string[][] {
  const rows: string[][] = []
  let row: string[] = []
  let cell = ''
  let quoted = false
  for (let i = 0; i < text.length; i++) {
    const c = text[i]
    if (quoted) {
      if (c === '"' && text[i + 1] === '"') { cell += '"'; i++ }
      else if (c === '"') quoted = false
      else cell += c
    } else if (c === '"') quoted = true
    else if (c === ',') { row.push(cell); cell = '' }
    else if (c === '\n' || c === '\r') {
      if (c === '\r' && text[i + 1] === '\n') i++
      row.push(cell); cell = ''
      if (row.some((x) => x.trim() !== '')) rows.push(row)
      row = []
    } else cell += c
  }
  row.push(cell)
  if (row.some((x) => x.trim() !== '')) rows.push(row)
  return rows
}

const HEADERS: Record<string, keyof ProjectInput> = {
  number: 'number', 'project number': 'number', name: 'name', description: 'description', region: 'region',
  district: 'district', oversight: 'oversightType', 'funding oversight': 'oversightType', 'funding source': 'fundingSource',
  'funding amount': 'fundingAmount', 'design estimate': 'designEstimate', 'letting date': 'lettingDate',
  'plan turn-in date': 'planTurnInDate', 'construction start': 'constructionStartDate', 'construction end': 'constructionEndDate',
}

export interface CsvResult {
  rows: Partial<ProjectInput>[]
  /** Problems with the file itself (unknown headers, a bad oversight value). Row-level validation happens on the server. */
  problems: string[]
}

/** Turns CSV text into import rows. The first line must be a header row. */
export function projectsFromCsv(text: string): CsvResult {
  const table = parseCsv(text)
  if (table.length < 2) return { rows: [], problems: ['The file needs a header row and at least one project.'] }
  const problems: string[] = []
  const columns = table[0].map((h) => {
    const key = HEADERS[h.trim().toLowerCase()]
    if (!key) problems.push(`Unknown column "${h.trim()}" was ignored.`)
    return key
  })
  if (!columns.includes('name')) return { rows: [], problems: ['The file needs a "Name" column.'] }

  const rows = table.slice(1).map((cells, i) => {
    const r: Record<string, unknown> = {}
    columns.forEach((key, c) => {
      if (!key) return
      const raw = cells[c] ?? ''
      if (key === 'fundingAmount' || key === 'designEstimate') r[key] = numOrNull(raw)
      else if (key === 'oversightType') {
        const match = OVERSIGHT_TYPES.find((o) => o.toLowerCase() === raw.trim().toLowerCase())
        if (raw.trim() && !match) problems.push(`Row ${i + 1}: "${raw.trim()}" is not Federal, State or Local.`)
        r[key] = (match ?? 'Local') as OversightType
      } else r[key] = key === 'name' ? raw.trim() : orNull(raw)
    })
    if (!r.oversightType) r.oversightType = 'Local'
    return r as Partial<ProjectInput>
  })
  return { rows, problems }
}

/** Bulk-creates projects from a CSV file (ROW-001: project numbers can come from another system). */
export function ImportPanel({
  onImport, onClose,
}: {
  onImport: (rows: Partial<ProjectInput>[]) => Promise<ImportSummary>
  onClose: () => void
}) {
  const [parsed, setParsed] = useState<CsvResult | null>(null)
  const [summary, setSummary] = useState<ImportSummary | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  async function pick(file: File | undefined) {
    setSummary(null)
    setError(null)
    if (!file) return setParsed(null)
    setParsed(projectsFromCsv(await file.text()))
  }

  async function run() {
    if (!parsed) return
    setBusy(true)
    setError(null)
    try {
      setSummary(await onImport(parsed.rows))
    } catch (e) {
      setError(messageOf(e))
    } finally {
      setBusy(false)
    }
  }

  return (
    <section aria-label="Import projects" className="mb-6 space-y-3 rounded-lg border bg-card p-4">
      <h2 className="text-sm font-semibold">Import projects from CSV</h2>
      <p className="text-xs text-muted-foreground">
        First row = column names: Number, Name, Region, District, Oversight (Federal, State or Local), Funding source,
        Funding amount, Design estimate, Letting date, Plan turn-in date, Construction start, Construction end.
        Leave Number empty to have one generated. Up to 500 rows.
      </p>
      <div>
        <label htmlFor="import-file" className="mb-1 block text-xs font-medium">CSV file</label>
        <input id="import-file" type="file" accept=".csv,text/csv" onChange={(e) => void pick(e.target.files?.[0])} />
      </div>
      {parsed && parsed.problems.length > 0 && (
        <ul className="list-disc pl-5 text-xs text-muted-foreground">{parsed.problems.map((p) => <li key={p}>{p}</li>)}</ul>
      )}
      {parsed && parsed.rows.length > 0 && !summary && (
        <p className="text-sm">{parsed.rows.length} project{parsed.rows.length === 1 ? '' : 's'} ready to import.</p>
      )}
      <FormError error={error} />
      {summary && (
        <div role="status" className="space-y-2">
          <p className="text-sm font-medium">{summary.created} created, {summary.failed} failed.</p>
          {summary.failed > 0 && (
            <DataTable caption="Rows that could not be imported" headers={['Row', 'Number', 'Problem']}>
              {summary.results.filter((r) => r.outcome === 'Failed').map((r) => (
                <tr key={r.row}><td className="px-3 py-2">{r.row}</td><td className="px-3 py-2">{r.number ?? '—'}</td><td className="px-3 py-2">{r.error}</td></tr>
              ))}
            </DataTable>
          )}
        </div>
      )}
      <div className="flex gap-2">
        {!summary && (
          <button type="button" className={buttonClassName('default')} disabled={busy || !parsed || parsed.rows.length === 0} onClick={() => void run()}>
            {busy ? 'Importing…' : 'Import'}
          </button>
        )}
        <button type="button" className={buttonClassName('outline')} onClick={onClose}>{summary ? 'Done' : 'Cancel'}</button>
      </div>
    </section>
  )
}
