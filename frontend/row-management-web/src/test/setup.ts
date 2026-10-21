import '@testing-library/jest-dom/vitest'
import { configure } from '@testing-library/react'

// A busy machine (the whole gate running, or a browser open) can take longer than the 1 s default to render a screen.
// Waiting longer costs nothing when the screen renders on time, and stops a slow run failing a correct test.
configure({ asyncUtilTimeout: 5000 })
