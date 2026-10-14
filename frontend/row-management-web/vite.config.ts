/// <reference types="vitest/config" />
import { fileURLToPath, URL } from 'node:url'
import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

export default defineConfig({
  plugins: [react()],
  resolve: {
    alias: { '@': fileURLToPath(new URL('./src', import.meta.url)) },
    // @aurigo/react-core-component-dev hard-pins its own React 18, which npm would nest privately.
    // The package.json "overrides" entry forces it onto this app's React 19; dedupe is the Vite-side
    // backstop so two copies of React can never be bundled (that renders as "older version of React").
    dedupe: ['react', 'react-dom'],
  },
  test: {
    environment: 'jsdom',
    setupFiles: ['./src/test/setup.ts'],
    globals: true, // lets Testing Library auto-clean the DOM between tests
    pool: 'threads',
  },
})
