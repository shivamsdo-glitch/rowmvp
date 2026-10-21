import type { ReactNode } from 'react'
import type { Parcel } from '@/api/parcels'
import { buttonClassName } from '@/components/ui/button'
import { DataTable } from '../projects/form-kit'

function Section({ title, children }: { title: string; children: ReactNode }) {
  return (
    <section aria-label={title} className="mb-6 rounded-lg border bg-card p-4">
      <h2 className="mb-3 text-sm font-semibold">{title}</h2>
      {children}
    </section>
  )
}

/**
 * Parcel Location (read-only): the parcel's id, CAD number and whether its shape has been saved to ESRI. There is no map or GIS link
 * yet (ADR 0017), so nothing has been saved and the status says so.
 */
export function ParcelLocationTab({ parcel }: { parcel: Parcel }) {
  const status = 'Not saved'
  function exportCsv() {
    const q = (v: string) => `"${v.replace(/"/g, '""')}"`
    const csv = ['ID,CAD Number,ESRI Save Status', [parcel.number, parcel.cadNumber ?? '', status].map(q).join(',')].join('\r\n')
    const url = URL.createObjectURL(new Blob([csv], { type: 'text/csv' }))
    const a = document.createElement('a')
    a.href = url
    a.download = `parcel-location-${parcel.number}.csv`
    a.click()
    URL.revokeObjectURL(url)
  }
  return (
    <Section title="Parcel location">
      <div className="mb-3 flex items-center justify-between">
        <p className="text-xs text-muted-foreground">Map and GIS are not connected yet, so no parcel shape has been saved to ESRI.</p>
        <button type="button" className={buttonClassName('outline', 'sm')} onClick={exportCsv}>Export to CSV</button>
      </div>
      <DataTable caption="Parcel location" headers={['ID', 'CAD number', 'ESRI save status']}>
        <tr>
          <td className="px-3 py-2 font-medium">{parcel.number}</td>
          <td className="px-3 py-2">{parcel.cadNumber ?? '—'}</td>
          <td className="px-3 py-2">{status}</td>
        </tr>
      </DataTable>
    </Section>
  )
}
