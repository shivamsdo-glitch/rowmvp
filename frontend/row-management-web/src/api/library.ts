import type { ApiClient } from './client'
import { CUSTOM_FIELD_TYPES, type CustomFieldType } from './parcels'

export { CUSTOM_FIELD_TYPES }
export type LibraryFieldType = CustomFieldType

export interface LibraryFieldSummary {
  id: string
  key: string
  label: string
  fieldType: LibraryFieldType
  isSystem: boolean
  valueCount: number
  activeCount: number
}

export interface LibraryValue {
  id: string
  value: string
  isActive: boolean
  sortOrder: number
  /** The value new records start with, where the field has one (the initial parcel status). */
  isDefault: boolean
}

export interface LibraryField {
  id: string
  key: string
  label: string
  fieldType: LibraryFieldType
  isSystem: boolean
  values: LibraryValue[]
}

export interface LibraryImportResult {
  added: number
  alreadyThere: number
  field: LibraryField
}

export const libraryApi = (api: ApiClient) => ({
  fields: () => api.get<LibraryFieldSummary[]>('/api/v1/library/fields'),
  field: (id: string) => api.get<LibraryField>(`/api/v1/library/fields/${id}`),
  createField: (label: string, fieldType: LibraryFieldType) => api.post<LibraryField>('/api/v1/library/fields', { label, fieldType }),
  updateField: (id: string, label: string, fieldType: LibraryFieldType) => api.put<LibraryField>(`/api/v1/library/fields/${id}`, { label, fieldType }),
  addValue: (id: string, value: string) => api.post<LibraryField>(`/api/v1/library/fields/${id}/values`, { value }),
  updateValue: (id: string, valueId: string, value: string, isActive: boolean) =>
    api.put<LibraryField>(`/api/v1/library/fields/${id}/values/${valueId}`, { value, isActive }),
  importValues: (id: string, values: string[]) => api.post<LibraryImportResult>(`/api/v1/library/fields/${id}/import`, { values }),
})

/** The first column of each line of a CSV or text file, one value per line. A first line that is just a heading is skipped. */
export function parseValues(text: string, heading: string): string[] {
  const out: string[] = []
  const lines = text.replace(/^﻿/, '').split(/\r?\n/)
  lines.forEach((line, i) => {
    let cell: string
    if (line.startsWith('"')) {
      // A quoted cell runs to the next lone quote; a doubled quote inside is one literal quote.
      let j = 1
      let text = ''
      while (j < line.length) {
        if (line[j] === '"') {
          if (line[j + 1] === '"') { text += '"'; j += 2; continue }
          break
        }
        text += line[j++]
      }
      cell = text
    } else {
      const comma = line.indexOf(',')
      cell = comma >= 0 ? line.slice(0, comma) : line
    }
    cell = cell.trim()
    if (!cell) return
    if (i === 0 && ['value', 'values', heading.toLowerCase()].includes(cell.toLowerCase())) return
    out.push(cell)
  })
  return out
}

/** The values as a CSV file's text: a heading, then one quoted value per line. */
export function valuesToCsv(heading: string, values: { value: string; isActive: boolean }[]): string {
  const q = (s: string) => `"${s.replace(/"/g, '""')}"`
  return [`${q(heading)},"Is active"`, ...values.map((v) => `${q(v.value)},${v.isActive ? 'Yes' : 'No'}`)].join('\r\n') + '\r\n'
}
