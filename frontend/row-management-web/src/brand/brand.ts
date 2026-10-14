/**
 * Brand strings for the app, kept in one place so the logo, name and copy are not hardcoded
 * across the UI. Colors live in `index.css` design tokens.
 *
 * The logo is a PLACEHOLDER (a text wordmark with a generic icon). Replace `logoSrc` with a real
 * image path (served from /public) when the asset exists; the shell uses it automatically.
 */
export const BRAND = {
  productName: 'ROW Management',
  /** Set to an image path (e.g. '/row-management.png') once a real logo exists. */
  logoSrc: null as string | null,
  tagline: 'Right-of-way acquisition, from parcel to closing.',
  /** Document <title>. */
  htmlTitle: 'Aurigo · ROW Management',
  copyright: '© 2026 Aurigo Software Technologies Inc.',
}
