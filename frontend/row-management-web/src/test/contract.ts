import type { PagePermission } from '@/api/auth'
import contractJson from '../../../../vault/acceptance/permission-matrix.json'

/**
 * The permission contract published by the backend tests (vault/acceptance/permission-matrix.json).
 * Frontend tests build their fake API from it, so the UI is always tested against the same truth the API is.
 */
interface Contract {
  roles: string[]
  pages: string[]
  matrix: Record<string, Record<string, 'B' | 'U' | 'S' | 'P' | null>>
  attachmentPages: Record<string, string>
}

export const contract = contractJson as unknown as Contract

export function levelFor(role: string, page: string) {
  return contract.matrix[role][page]
}

export function canRead(role: string, page: string) {
  return levelFor(role, page) !== null
}

/** The rows GET /auth/permissions returns for a role, derived from the contract. */
export function permissionRowsFor(role: string): PagePermission[] {
  return contract.pages.map((pageKey) => {
    const level = levelFor(role, pageKey)
    return {
      pageKey,
      canCreate: level === 'S' || level === 'P',
      canRead: level !== null,
      canUpdate: level === 'U' || level === 'S' || level === 'P',
      canDelete: level === 'P',
    }
  })
}
