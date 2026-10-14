/**
 * Switches for UI whose backend does not exist yet. Flip a flag when the endpoint ships; the UI is
 * already wired and tested against the planned contract.
 */
export const FEATURES = {
  /** GET /api/v1/notifications, POST /api/v1/notifications/{id}/read, POST /api/v1/notifications/read-all. */
  notifications: false,
}
