import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import CertificateDetailsPage from './CertificateDetailsPage'
import CertificatesListPage from './CertificatesListPage'

describe('certificate query pages', () => {
  const fetchMock = vi.fn<typeof fetch>()

  beforeEach(() => {
    vi.stubGlobal('fetch', fetchMock)
    fetchMock.mockReset()
    fetchMock.mockImplementation(async (input) => {
      if (input === '/api/certificates/defaults') {
        return Response.json({
          certificateId: 'certificate-default',
          xSignerId: 'signer-default',
        })
      }

      if (input === '/api/certificates') {
        return Response.json({
          certificates: [{ ids: ['urn:alias:alias-1'], aliasId: 'alias-1' }],
        })
      }

      return Response.json({
        certificateId: 'certificate-request',
        ids: ['urn:alias:alias-1'],
        aliasId: 'alias-1',
        certificateAvailable: true,
      })
    })
  })

  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('loads list defaults without querying SIGN and retrieves only after submit', async () => {
    const user = userEvent.setup()
    render(<CertificatesListPage />)

    expect(await screen.findByLabelText('X-signer-id')).toHaveValue('signer-default')
    expect(fetchMock).toHaveBeenCalledTimes(1)
    expect(fetchMock).toHaveBeenCalledWith(
      '/api/certificates/defaults',
      expect.objectContaining({ method: 'GET', cache: 'no-store' }),
    )
    expect(screen.queryByText('No certificates were returned for this X-signer-id.')).not.toBeInTheDocument()

    await user.clear(screen.getByLabelText('X-signer-id'))
    await user.type(screen.getByLabelText('X-signer-id'), 'signer-edited')
    await user.click(screen.getByRole('button', { name: 'Retrieve certificates' }))

    expect(await screen.findByRole('heading', { name: 'alias-1' })).toBeInTheDocument()
    expect(fetchMock).toHaveBeenCalledTimes(2)
    expect(fetchMock).toHaveBeenLastCalledWith(
      '/api/certificates',
      expect.objectContaining({
        method: 'POST',
        body: JSON.stringify({ xSignerId: 'signer-edited' }),
      }),
    )
  })

  it('disables list submission while the explicit request is in flight', async () => {
    let resolveResponse: ((response: Response) => void) | undefined
    fetchMock.mockImplementation((input) => {
      if (input === '/api/certificates/defaults') {
        return Promise.resolve(Response.json({
          certificateId: 'certificate-default',
          xSignerId: 'signer-default',
        }))
      }

      return new Promise((resolve) => {
        resolveResponse = resolve
      })
    })
    const user = userEvent.setup()
    render(<CertificatesListPage />)

    await screen.findByDisplayValue('signer-default')
    const button = screen.getByRole('button', { name: 'Retrieve certificates' })
    await user.click(button)

    expect(button).toBeDisabled()
    expect(screen.getByRole('status')).toHaveTextContent('Loading certificates…')

    resolveResponse?.(Response.json({ certificates: [] }))
    await waitFor(() => expect(button).toBeEnabled())
  })

  it('loads detail defaults without querying SIGN and sends edited values on submit', async () => {
    const user = userEvent.setup()
    render(<CertificateDetailsPage />)

    expect(await screen.findByLabelText('Certificate ID')).toHaveValue('certificate-default')
    expect(screen.getByLabelText('X-signer-id')).toHaveValue('signer-default')
    expect(fetchMock).toHaveBeenCalledTimes(1)
    expect(screen.queryByText('Certificate data available')).not.toBeInTheDocument()

    await user.clear(screen.getByLabelText('Certificate ID'))
    await user.type(screen.getByLabelText('Certificate ID'), 'certificate-edited')
    await user.clear(screen.getByLabelText('X-signer-id'))
    await user.type(screen.getByLabelText('X-signer-id'), 'signer-edited')
    await user.click(screen.getByRole('button', { name: 'Retrieve certificate details' }))

    expect(await screen.findByText('Certificate data available')).toBeInTheDocument()
    expect(fetchMock).toHaveBeenCalledTimes(2)
    expect(fetchMock).toHaveBeenLastCalledWith(
      '/api/certificates/configured',
      expect.objectContaining({
        method: 'POST',
        body: JSON.stringify({
          certificateId: 'certificate-edited',
          xSignerId: 'signer-edited',
        }),
      }),
    )
  })
})
