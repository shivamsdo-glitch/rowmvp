import { beforeEach, describe, expect, it } from 'vitest'
import { screen } from '@testing-library/react'
import userEventModule from '@testing-library/user-event'
import { BRAND } from '@/brand/brand'
import { permissionRowsFor } from '@/test/contract'
import { fakeAuthFetch } from '@/test/fake-api'
import { renderApp } from '@/test/render-app'

beforeEach(() => sessionStorage.clear())

// Phase 1.5 slice 1 ("Next requirements", Landing page): username and password under the Masterworks name.
describe('the landing page', () => {
  function open() {
    return renderApp({ path: '/login', fetchImpl: fakeAuthFetch({ rows: permissionRowsFor('Admin'), role: 'Admin' }) as typeof fetch })
  }

  it('is the Masterworks Right of Way Management page with Username and Password fields', async () => {
    open()

    expect(await screen.findByRole('heading', { name: 'Masterworks Right of Way Management' })).toBeInTheDocument()
    expect(BRAND.productName).toBe('Masterworks Right of Way Management')
    expect(screen.getByLabelText('Username')).toBeInTheDocument()
    expect(screen.getByLabelText('Password')).toHaveAttribute('type', 'password')
    expect(screen.getByRole('button', { name: 'Sign in' })).toBeInTheDocument()
  })

  it('signs in with a username and password and opens the dashboard', async () => {
    const user = userEventModule.setup()
    open()

    await user.type(await screen.findByLabelText('Username'), 'admin')
    await user.type(screen.getByLabelText('Password'), 'qwertyuiop')
    await user.click(screen.getByRole('button', { name: 'Sign in' }))

    expect(await screen.findByRole('heading', { name: 'Dashboard' })).toBeInTheDocument()
  })

  it('says so, without saying which half was wrong, when the credentials are not right', async () => {
    const user = userEventModule.setup()
    open()

    await user.type(await screen.findByLabelText('Username'), 'admin')
    await user.type(screen.getByLabelText('Password'), 'wrong-password')
    await user.click(screen.getByRole('button', { name: 'Sign in' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('Incorrect username or password.')
    expect(screen.queryByRole('heading', { name: 'Dashboard' })).not.toBeInTheDocument()
  })

  it('asks for both fields before it calls the server', async () => {
    const user = userEventModule.setup()
    open()

    await user.type(await screen.findByLabelText('Username'), 'admin')
    await user.click(screen.getByRole('button', { name: 'Sign in' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('Enter your username and password.')
  })
})
