import { useQuery } from '@tanstack/react-query'
import { libraryApi } from '@/api/library'
import { useAuth } from '@/auth/auth-context'

/**
 * The active values of a Library list (a dropdown on a form), by the field's key. Every role can read the Library. While it loads,
 * or if it cannot be read, the list is empty and the form simply offers nothing to choose.
 */
export function useLibraryValues(key: string, enabled = true): string[] {
  const { manager } = useAuth()
  const api = libraryApi(manager.api)
  const fields = useQuery({ queryKey: ['library', 'fields'], queryFn: () => api.fields(), enabled, retry: false })
  const id = fields.data?.find((f) => f.key === key)?.id
  const field = useQuery({ queryKey: ['library', 'field', id], queryFn: () => api.field(id!), enabled: enabled && !!id, retry: false })
  return (field.data?.values ?? []).filter((v) => v.isActive).map((v) => v.value)
}

/** Options for a SelectField from plain values; a value already saved on the record stays selectable even if it was switched off since. */
export function optionsWith(values: string[], current?: string | null): { value: string; label: string }[] {
  const all = current && !values.includes(current) ? [...values, current] : values
  return all.map((v) => ({ value: v, label: v }))
}

/** The names of the active people in the Library's Agents list (designers, surveyors, negotiators, title agents...), for "assigned to" selects. */
export function useAgentNames(enabled = true): string[] {
  const { manager } = useAuth()
  const api = libraryApi(manager.api)
  const agents = useQuery({ queryKey: ['library', 'agents'], queryFn: () => api.agents(), enabled, retry: false })
  return (agents.data ?? []).filter((a) => a.isActive).map((a) => a.name)
}
