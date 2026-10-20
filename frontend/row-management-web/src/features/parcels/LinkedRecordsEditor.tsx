import { useState } from 'react'
import { Trash2 } from 'lucide-react'
import type { ContactLogLinkInput } from '@/api/parcel-tabs'
import { buttonClassName } from '@/components/ui/button'
import { DataTable, orNull, TextField } from '../projects/form-kit'

/**
 * The "Linked records" grid of a form: other records (by identifier and module) the record being written refers to. The list is edited
 * here and sent whole with the record, so removing a row and saving removes the link.
 */
export function LinkedRecordsEditor({ links, onChange }: { links: ContactLogLinkInput[]; onChange: (links: ContactLogLinkInput[]) => void }) {
  const [id, setId] = useState('')
  const [module, setModule] = useState('')
  const [notes, setNotes] = useState('')
  const [error, setError] = useState<string | undefined>()

  function add() {
    if (!id.trim()) { setError('Enter the record identifier.'); return }
    onChange([...links, { recordIdentifier: id.trim(), moduleName: orNull(module), notes: orNull(notes) }])
    setId(''); setModule(''); setNotes(''); setError(undefined)
  }

  return (
    <fieldset className="rounded-md border p-3">
      <legend className="px-1 text-xs font-medium text-muted-foreground">Linked records</legend>
      {links.length > 0 && (
        <DataTable caption="Linked records" headers={['Record identifier', 'Module name', 'Notes', 'Actions']}>
          {links.map((l, i) => (
            <tr key={`${l.recordIdentifier}-${i}`}>
              <td className="px-3 py-2">{l.recordIdentifier}</td>
              <td className="px-3 py-2">{l.moduleName ?? '—'}</td>
              <td className="px-3 py-2">{l.notes ?? '—'}</td>
              <td className="px-3 py-2">
                <button type="button" aria-label={`Remove linked record ${l.recordIdentifier}`} className={buttonClassName('ghost', 'sm')} onClick={() => onChange(links.filter((_, j) => j !== i))}>
                  <Trash2 size={14} aria-hidden />
                </button>
              </td>
            </tr>
          ))}
        </DataTable>
      )}
      <div className="mt-3 grid gap-3 sm:grid-cols-3">
        <TextField label="Record identifier" value={id} onChange={setId} maxLength={100} error={error} />
        <TextField label="Module name" value={module} onChange={setModule} maxLength={100} />
        <TextField label="Link notes" value={notes} onChange={setNotes} maxLength={500} />
      </div>
      <button type="button" className={`${buttonClassName('outline', 'sm')} mt-2`} onClick={add}>Add linked record</button>
    </fieldset>
  )
}
