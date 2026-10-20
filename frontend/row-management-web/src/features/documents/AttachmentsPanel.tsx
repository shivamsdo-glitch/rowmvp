import { useRef, useState } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { Download, Trash2 } from 'lucide-react'
import { documentsApi, type DocumentInfo } from '@/api/parcel-tabs'
import { useAuth } from '@/auth/auth-context'
import { buttonClassName } from '@/components/ui/button'
import { EmptyState, ErrorState, LoadingRows } from '@/components/ui/states'
import { DataTable, dateText, FormError, messageOf } from '../projects/form-kit'

const sizeText = (bytes: number) => (bytes < 1024 ? `${bytes} B` : bytes < 1024 * 1024 ? `${(bytes / 1024).toFixed(1)} KB` : `${(bytes / 1024 / 1024).toFixed(1)} MB`)

/**
 * The files attached to one record (a contact, an easement...), through the shared documents API. Which page governs the record
 * decides what the person may do: read to see and download, update to attach, delete to remove (ADR 0017).
 */
export function AttachmentsPanel({ entityType, entityId, page, title = 'Attachments' }: { entityType: string; entityId: string; page: string; title?: string }) {
  const { manager, token } = useAuth()
  const api = documentsApi(manager.api)
  const queryClient = useQueryClient()
  const key = ['documents', entityType, entityId]
  const docs = useQuery({ queryKey: key, queryFn: () => api.list(entityType, entityId), enabled: manager.can(page) })
  const input = useRef<HTMLInputElement>(null)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const canAdd = manager.can(page, 'update')
  const canRemove = manager.can(page, 'delete')

  if (!manager.can(page)) return null

  async function run(action: () => Promise<unknown>) {
    setError(null)
    setBusy(true)
    try {
      await action()
      await queryClient.invalidateQueries({ queryKey: key })
    } catch (e) {
      setError(messageOf(e))
    } finally {
      setBusy(false)
    }
  }

  async function upload(file: File | undefined) {
    if (!file) return
    await run(() => api.upload(entityType, entityId, file))
    if (input.current) input.current.value = ''
  }

  async function download(d: DocumentInfo) {
    setError(null)
    try {
      const res = await fetch(`/api/v1/documents/${d.id}/content`, { headers: { Authorization: `Bearer ${token ?? ''}` } })
      if (!res.ok) throw new Error('The file could not be downloaded.')
      const url = URL.createObjectURL(await res.blob())
      const a = document.createElement('a')
      a.href = url
      a.download = d.fileName
      a.click()
      URL.revokeObjectURL(url)
    } catch (e) {
      setError(messageOf(e))
    }
  }

  return (
    <section aria-label={title} className="mb-6 rounded-lg border bg-card p-4">
      <h3 className="mb-3 text-sm font-semibold">{title}</h3>
      {docs.isPending ? <LoadingRows rows={2} /> : docs.isError ? (
        <ErrorState title="The attachments could not be loaded" description={docs.error.message} retry={() => void docs.refetch()} />
      ) : docs.data.length === 0 ? (
        <EmptyState title="No attachments" description={canAdd ? 'Upload a document to keep it with this record.' : 'Nothing has been attached to this record.'} />
      ) : (
        <DataTable caption={`${title} list`} headers={['File name', 'Size', 'Uploaded', 'Actions']}>
          {docs.data.map((d) => (
            <tr key={d.id}>
              <td className="px-3 py-2 font-medium">{d.fileName}</td>
              <td className="px-3 py-2">{sizeText(d.sizeBytes)}</td>
              <td className="px-3 py-2">{dateText(d.uploadedAt)}</td>
              <td className="px-3 py-2">
                <span className="inline-flex gap-1">
                  <button type="button" aria-label={`Download ${d.fileName}`} className={buttonClassName('ghost', 'sm')} onClick={() => void download(d)}><Download size={14} aria-hidden /></button>
                  {canRemove && (
                    <button type="button" aria-label={`Remove ${d.fileName}`} disabled={busy} className={buttonClassName('ghost', 'sm')} onClick={() => void run(() => api.remove(d.id))}>
                      <Trash2 size={14} aria-hidden />
                    </button>
                  )}
                </span>
              </td>
            </tr>
          ))}
        </DataTable>
      )}
      {canAdd && (
        <div className="mt-3">
          <label className="text-sm font-medium" htmlFor={`upload-${entityId}`}>Upload document</label>
          <input id={`upload-${entityId}`} ref={input} type="file" disabled={busy} className="mt-1 block text-sm" onChange={(e) => void upload(e.target.files?.[0])} />
        </div>
      )}
      <FormError error={error} />
    </section>
  )
}
