import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import CertificateChainPage from './CertificateChainPage'

describe('CertificateChainPage', () => {
  const fetchMock = vi.fn<typeof fetch>()

  beforeEach(() => {
    vi.stubGlobal('fetch', fetchMock)
    fetchMock.mockReset()
    fetchMock.mockImplementation(async (input) => {
      if (input === '/api/certificates/defaults') {
        return Response.json({
          certificateId: 'configured-certificate',
          xSignerId: 'configured-signer',
        })
      }
      return Response.json([])
    })
  })

  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('requires Certificate ID and renders multiple chain items and raw JSON', async () => {
    fetchMock.mockImplementation(async (input) => {
      if (input === '/api/certificates/defaults') {
        return Response.json({
          certificateId: 'configured-certificate',
          xSignerId: 'configured-signer',
        })
      }
      return Response.json([
      {
        certificate: 'leaf-certificate',
        ids: ['urn:alias:leaf'],
        subject: 'CN=Leaf',
        issuer: 'CN=Issuer',
        status: 'active',
        expirationDate: '2035-01-01T00:00:00Z',
      },
      {
        certificate: 'root-certificate',
        ids: ['urn:key:root'],
        subject: 'CN=Root',
        issuer: 'CN=Root',
        status: 'future-status',
        expirationDate: '2036-01-01T00:00:00Z',
      },
      ])
    })
    const user = userEvent.setup()
    render(<CertificateChainPage />)

    const input = screen.getByLabelText('Certificate ID')
    const signerInput = screen.getByLabelText('X-signer-id')
    const button = screen.getByRole('button', { name: 'Retrieve certificate chain' })
    expect(input).toBeRequired()
    expect(signerInput).toBeRequired()
    expect(button).toBeDisabled()

    await screen.findByDisplayValue('configured-certificate')
    expect(signerInput).toHaveValue('configured-signer')
    await user.clear(input)
    await user.type(input, 'CERT/123')
    expect(button).toBeEnabled()
    await user.click(button)

    expect(await screen.findByText('CN=Leaf')).toBeInTheDocument()
    expect(screen.getByText('CN=Root')).toBeInTheDocument()
    expect(screen.getAllByText('urn:alias:leaf').length).toBeGreaterThan(0)
    expect(screen.getByText('future-status')).toBeInTheDocument()
    expect(fetchMock).toHaveBeenCalledWith(
      '/api/certificates/CERT%2F123/chain',
      expect.objectContaining({
        method: 'POST',
        cache: 'no-store',
        body: JSON.stringify({ xSignerId: 'configured-signer' }),
      }),
    )
    expect(screen.getByText('Raw JSON Response')).toBeInTheDocument()
  })

  it('shows loading and empty-chain states', async () => {
    let resolveResponse: ((response: Response) => void) | undefined
    fetchMock.mockImplementation((input) => {
      if (input === '/api/certificates/defaults') {
        return Promise.resolve(Response.json({
          certificateId: 'configured-certificate',
          xSignerId: 'configured-signer',
        }))
      }
      return new Promise((resolve) => {
        resolveResponse = resolve
      })
    })
    const user = userEvent.setup()
    render(<CertificateChainPage />)

    const input = screen.getByLabelText('Certificate ID')
    await screen.findByDisplayValue('configured-certificate')
    await user.clear(input)
    await user.type(input, 'CERT-1')
    const button = screen.getByRole('button', { name: 'Retrieve certificate chain' })
    await user.click(button)
    expect(screen.getByRole('status')).toHaveTextContent('Loading certificate chain…')
    expect(button).toBeDisabled()

    resolveResponse?.(Response.json([]))
    await waitFor(() => expect(button).toBeEnabled())
    expect(screen.getByText('Nessun certificato presente nella catena restituita dal provider.')).toBeInTheDocument()
  })

  it('shows a safe backend error', async () => {
    fetchMock.mockImplementation(async (input) => {
      if (input === '/api/certificates/defaults') {
        return Response.json({
          certificateId: 'configured-certificate',
          xSignerId: 'configured-signer',
        })
      }
      return Response.json(
        { title: 'Certificate chain unavailable', status: 404, message: 'The certificate chain could not be found.' },
        { status: 404 },
      )
    })
    const user = userEvent.setup()
    render(<CertificateChainPage />)

    const input = screen.getByLabelText('Certificate ID')
    await screen.findByDisplayValue('configured-certificate')
    await user.clear(input)
    await user.type(input, 'missing')
    await user.click(screen.getByRole('button', { name: 'Retrieve certificate chain' }))

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'Certificate chain unavailable: The certificate chain could not be found.',
    )
  })
})
