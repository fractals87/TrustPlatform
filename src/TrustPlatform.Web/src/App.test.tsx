import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import App from './App'

describe('TrustPlatform application shell', () => {
  const fetchMock = vi.fn<typeof fetch>()

  beforeEach(() => {
    vi.stubGlobal('fetch', fetchMock)
  })

  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('shows loading and success states for backend health', async () => {
    let resolveResponse: ((response: Response) => void) | undefined
    fetchMock.mockReturnValue(
      new Promise((resolve) => {
        resolveResponse = resolve
      }),
    )

    render(<App />)

    expect(screen.getByRole('status')).toHaveTextContent(
      'Checking backend health…',
    )
    expect(fetchMock).toHaveBeenCalledWith(
      '/api/health',
      expect.objectContaining({ credentials: 'same-origin' }),
    )

    resolveResponse?.(Response.json({ status: 'Healthy' }))
    await waitFor(() => {
      expect(screen.getByRole('status')).toHaveTextContent(
        'The backend is available.',
      )
    })
  })

  it('shows a safe error and can retry the health check', async () => {
    fetchMock
      .mockRejectedValueOnce(new TypeError('internal network detail'))
      .mockResolvedValueOnce(Response.json({ status: 'Healthy' }))

    render(<App />)

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'Unable to connect to the TrustPlatform backend.',
    )
    expect(screen.getByRole('alert')).not.toHaveTextContent(
      'internal network detail',
    )

    fireEvent.click(screen.getByRole('button', { name: 'Retry health check' }))

    await waitFor(() => {
      expect(screen.getByRole('status')).toHaveTextContent(
        'The backend is available.',
      )
    })
  })

  it('provides the requested navigation and renders the signing form', async () => {
    fetchMock.mockResolvedValue(Response.json({ status: 'Healthy' }))
    render(<App />)

    for (const page of [
      'Home',
      'Signing',
      'Validation',
      'Certificates',
      'Diagnostics',
    ]) {
      expect(
        screen.getByRole('button', { name: page }),
      ).toBeInTheDocument()
    }

    fireEvent.click(screen.getByRole('button', { name: 'Signing' }))

    expect(await screen.findByRole('heading', { name: 'Signing' })).toBeInTheDocument()
    expect(screen.getByLabelText('Certificate type')).toHaveValue('AUTOMATIC')
  })

  it('tests SIGN API authentication and displays only safe metadata', async () => {
    fetchMock.mockImplementation(async (input) => {
      if (input === '/api/health') {
        return Response.json({ status: 'Healthy' })
      }

      return Response.json({
        authenticated: true,
        tokenAcquired: true,
        tokenType: 'Bearer',
        expiresIn: 300,
        cached: true,
        error: null,
      })
    })

    const { container } = render(<App />)
    fireEvent.click(screen.getByRole('button', { name: 'Diagnostics' }))
    fireEvent.click(
      screen.getByRole('button', { name: 'Test authentication' }),
    )

    const result = await screen.findByRole('status', {
      name: 'SIGN API authentication result',
    })
    expect(result).toHaveClass('sign-auth-success')
    expect(result).toHaveTextContent(/Authentication:\s*SUCCESS/)
    expect(result).toHaveTextContent(/Token acquired:\s*YES/)
    expect(result).toHaveTextContent(/Token type:\s*Bearer/)
    expect(result).toHaveTextContent(/Expires in:\s*300 seconds/)
    expect(result).toHaveTextContent(/Cached:\s*YES/)
    expect(container).not.toHaveTextContent('test-access-token')
    expect(fetchMock).toHaveBeenCalledWith(
      '/api/diagnostics/sign-auth',
      expect.objectContaining({ method: 'POST' }),
    )
  })

  it('shows a safe SIGN authentication failure message', async () => {
    fetchMock.mockImplementation(async (input) => {
      if (input === '/api/health') {
        return Response.json({ status: 'Healthy' })
      }

      return Response.json(
        {
          authenticated: false,
          tokenAcquired: false,
          tokenType: null,
          expiresIn: null,
          cached: false,
          error: 'SIGN API authentication failed.',
        },
        { status: 502 },
      )
    })

    render(<App />)
    fireEvent.click(screen.getByRole('button', { name: 'Diagnostics' }))
    fireEvent.click(
      screen.getByRole('button', { name: 'Test authentication' }),
    )

    const result = await screen.findByRole('alert')
    expect(result).toHaveClass('sign-auth-error')
    expect(result).toHaveTextContent('Authentication: FAILED')
    expect(result).toHaveTextContent('SIGN API authentication failed.')
  })

  it('shows an authenticating status and disables the button while waiting', async () => {
    let resolveResponse: ((response: Response) => void) | undefined
    fetchMock.mockImplementation((input) => {
      if (input === '/api/health') {
        return Promise.resolve(Response.json({ status: 'Healthy' }))
      }

      return new Promise((resolve) => {
        resolveResponse = resolve
      })
    })

    render(<App />)
    fireEvent.click(screen.getByRole('button', { name: 'Diagnostics' }))
    const button = screen.getByRole('button', { name: 'Test authentication' })
    fireEvent.click(button)

    expect(screen.getByText('Authenticating...', { selector: 'p' })).toBeInTheDocument()
    expect(button).toBeDisabled()

    resolveResponse?.(
      Response.json({
        authenticated: true,
        tokenAcquired: true,
        tokenType: 'Bearer',
        expiresIn: 300,
        cached: true,
      }),
    )
    expect(
      await screen.findByRole('status', {
        name: 'SIGN API authentication result',
      }),
    ).toHaveTextContent(/Authentication:\s*SUCCESS/)
  })
})
