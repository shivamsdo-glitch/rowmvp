import type { ComponentType } from 'react'

/**
 * @aurigo/react-core-component-dev ships its own .d.ts files compiled against
 * React 18's JSX/component types. Under React 19's stricter JSX component
 * typing this app already hits the same mismatch on Syncfusion's own
 * GridComponent (see components/aurigo/RCGrid.tsx) — it isn't specific to
 * this library. Cast at the import site so every facade gets a component
 * typed correctly for this app's React version instead of `as any`-ing each
 * usage individually.
 */
export function asReactComponent<P>(component: unknown): ComponentType<P> {
  return component as ComponentType<P>
}
