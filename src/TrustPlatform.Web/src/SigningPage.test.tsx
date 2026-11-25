import { fireEvent, render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import SigningPage from './SigningPage'

describe('SigningPage', () => {
  const fetchMock = vi.fn<typeof fetch>()
  const supportedOptions = {
    maxFileSizeBytes: 16,
    allowedExtensions: ['.pdf'],
    allowedContentTypes: ['application/pdf'],
    packagingValues: ['ENVELOPED', 'ENVELOPING'],
    signatureLevelValues: ['BASELINE-B'],
  }
  const developmentDefaults = {
    certificateId: 'certificate-default',
    xSignerId: 'signer-default',
    pin: 'pin-default',
    infocertSat: 'sat-default',
    packaging: 'ENVELOPING',
    signatureLevel: 'BASELINE-B',
  }
  const padesDefaults = {
    ...developmentDefaults,
    isVisible: true,
    isPdfa: true,
    page: 1,
    llx: 100,
    lly: 100,
    urx: 20,
    ury: 20,
  }

  beforeEach(() => {
    vi.stubGlobal('fetch', fetchMock)
    fetchMock.mockImplementation(async (input) => {
      if (input === '/api/signing/options') {
        return Response.json(supportedOptions)
      }
      if (input === '/api/signing/automatic/cades/defaults') {
        return Response.json(developmentDefaults)
      }
      if (input === '/api/signing/automatic/pades/defaults') {
        return Response.json(padesDefaults)
      }

      return new Response(new Blob(['signed document'], { type: 'application/pkcs7-mime' }), {
        status: 200,
        headers: {
          'Content-Type': 'application/pkcs7-mime',
          'Content-Disposition': 'attachment; filename="signed-document-abc.p7m"',
          'X-TrustPlatform-Request-Id': 'operation-123',
        },
      })
    })
    vi.stubGlobal('URL', {
      ...URL,
      createObjectURL: vi.fn(() => 'blob:signed-document'),
      revokeObjectURL: vi.fn(),
    })
  })

  afterEach(() => {
    vi.unstubAllGlobals()
    vi.restoreAllMocks()
  })

  async function renderPage() {
    const user = userEvent.setup()
    render(<SigningPage />)
    await screen.findByText('Drag and drop one PDF here')
    await screen.findByDisplayValue('certificate-default')
    return user
  }

  async function completeForm(user: ReturnType<typeof userEvent.setup>) {
    const file = new File(['%PDF-1.7'], 'contract.pdf', { type: 'application/pdf' })
    await user.upload(screen.getByLabelText('Select a PDF document'), file)
    await user.clear(screen.getByLabelText('Certificate ID'))
    await user.type(screen.getByLabelText('Certificate ID'), 'INFOCERT+CL_EXT_ID_ZORRO_000000001')
    await user.clear(screen.getByLabelText('X-signer-id'))
    await user.type(screen.getByLabelText('X-signer-id'), 'signer-123')
    await user.clear(screen.getByLabelText('PIN'))
    await user.type(screen.getByLabelText('PIN'), 'pin-secret')
    await user.clear(screen.getByLabelText('Infocert-SAT'))
    await user.type(screen.getByLabelText('Infocert-SAT'), 'sat-secret')
    await user.selectOptions(screen.getByLabelText('Packaging'), 'ENVELOPED')
    await user.selectOptions(screen.getByLabelText('Signature level'), 'BASELINE-B')
  }

  it('loads development defaults when Automatic signature is selected', async () => {
    const storageWrites = vi.spyOn(Storage.prototype, 'setItem')
    const user = await renderPage()

    expect(screen.getByLabelText('Certificate type')).toHaveValue('AUTOMATIC')
    expect(fetchMock).toHaveBeenCalledWith(
      '/api/signing/automatic/cades/defaults',
      expect.objectContaining({ cache: 'no-store', credentials: 'same-origin' }),
    )
    expect(screen.getByLabelText('Certificate ID')).toHaveValue('certificate-default')
    expect(screen.getByLabelText('X-signer-id')).toHaveValue('signer-default')
    expect(screen.getByLabelText('PIN')).toHaveValue('pin-default')
    expect(screen.getByLabelText('PIN')).toHaveAttribute('type', 'password')
    expect(screen.getByLabelText('Infocert-SAT')).toHaveValue('sat-default')
    expect(screen.getByLabelText('Infocert-SAT')).toHaveAttribute('type', 'password')
    expect(screen.getByLabelText('Packaging')).toHaveValue('ENVELOPING')
    expect(screen.getByLabelText('Signature level')).toHaveValue('BASELINE-B')

    await user.clear(screen.getByLabelText('Certificate ID'))
    await user.type(screen.getByLabelText('Certificate ID'), 'edited-certificate')
    await user.clear(screen.getByLabelText('X-signer-id'))
    await user.type(screen.getByLabelText('X-signer-id'), 'edited-signer')
    await user.clear(screen.getByLabelText('PIN'))
    await user.type(screen.getByLabelText('PIN'), 'edited-pin')
    await user.clear(screen.getByLabelText('Infocert-SAT'))
    await user.type(screen.getByLabelText('Infocert-SAT'), 'edited-sat')
    await user.selectOptions(screen.getByLabelText('Packaging'), 'ENVELOPED')

    expect(screen.getByLabelText('Certificate ID')).toHaveValue('edited-certificate')
    expect(screen.getByLabelText('X-signer-id')).toHaveValue('edited-signer')
    expect(screen.getByLabelText('PIN')).toHaveValue('edited-pin')
    expect(screen.getByLabelText('Infocert-SAT')).toHaveValue('edited-sat')
    expect(screen.getByLabelText('Packaging')).toHaveValue('ENVELOPED')
    expect(storageWrites).not.toHaveBeenCalled()
  })

  it('enables Automatic + CAdES and keeps other combinations visible but disabled', async () => {
    await renderPage()

    expect(screen.getByLabelText('Certificate type')).toHaveValue('AUTOMATIC')
    expect(screen.getByLabelText('Signature type')).toHaveValue('CAdES')
    expect(screen.getByLabelText('Signature level')).toHaveValue('BASELINE-B')
    expect(
      screen.getByRole('option', { name: 'Remote signature — Not implemented yet' }),
    ).toBeDisabled()
    expect(screen.getByRole('option', { name: 'PAdES' })).toBeEnabled()
    for (const type of ['XAdES', 'Hash']) {
      expect(
        screen.getByRole('option', { name: `${type} — Not implemented yet` }),
      ).toBeDisabled()
    }
    expect(screen.getByRole('button', { name: 'Sign document' })).toBeDisabled()
  })

  it('shows PAdES appearance fields and submits to the PAdES endpoint', async () => {
    const user = await renderPage()

    await user.selectOptions(screen.getByLabelText('Signature type'), 'PAdES')
    expect(await screen.findByText('PAdES signature appearance')).toBeInTheDocument()
    expect(screen.getByLabelText('Visible signature')).toHaveValue('true')
    expect(screen.getByLabelText('PDF/A')).toHaveValue('true')
    expect(screen.getByLabelText('Page')).toHaveValue(1)
    expect(screen.getByLabelText('Lower-left X (llx)')).toHaveValue(100)
    expect(screen.getByLabelText('Lower-left Y (lly)')).toHaveValue(100)
    expect(screen.getByLabelText('Upper-right X (urx)')).toHaveValue(20)
    expect(screen.getByLabelText('Upper-right Y (ury)')).toHaveValue(20)

    await user.selectOptions(screen.getByLabelText('Visible signature'), 'false')
    expect(screen.queryByLabelText('Signature field 1')).not.toBeInTheDocument()
    expect(screen.queryByLabelText('PDF/A')).not.toBeInTheDocument()

    await user.selectOptions(screen.getByLabelText('Visible signature'), 'true')
    await completeForm(user)
    await user.click(screen.getByRole('button', { name: 'Sign document' }))

    expect(fetchMock).toHaveBeenCalledWith(
      '/api/signing/automatic/pades',
      expect.objectContaining({ method: 'POST', credentials: 'same-origin' }),
    )
  })

  it('accepts a drag-and-dropped PDF and shows filename, size, and detected type', async () => {
    await renderPage()
    const file = new File(['%PDF-1.7'], 'dropped.pdf', { type: 'application/pdf' })

    fireEvent.drop(screen.getByLabelText('PDF document upload'), {
      dataTransfer: { files: [file] },
    })

    expect(await screen.findByText('dropped.pdf')).toBeInTheDocument()
    expect(screen.getByText('Detected content type:')).toBeInTheDocument()
    expect(screen.getByText('application/pdf')).toBeInTheDocument()
    expect(screen.getByText('0.0 KB')).toBeInTheDocument()
  })

  it('uses the Select file button and supports replacing and removing a file', async () => {
    const user = await renderPage()
    const input = screen.getByLabelText('Select a PDF document') as HTMLInputElement
    const clickSpy = vi.spyOn(input, 'click')
    await user.click(screen.getByRole('button', { name: 'Select file' }))
    expect(clickSpy).toHaveBeenCalledOnce()

    await user.upload(input, new File(['%PDF-one'], 'first.pdf', { type: 'application/pdf' }))
    expect(await screen.findByText('first.pdf')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Replace file' })).toBeInTheDocument()

    await user.upload(input, new File(['%PDF-two'], 'second.pdf', { type: 'application/pdf' }))
    expect(await screen.findByText('second.pdf')).toBeInTheDocument()
    expect(screen.queryByText('first.pdf')).not.toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Remove file' }))
    expect(screen.queryByText('second.pdf')).not.toBeInTheDocument()
  })

  it('shows clear validation errors for unsupported type and oversized files', async () => {
    await renderPage()
    const input = screen.getByLabelText('Select a PDF document')
    fireEvent.change(input, {
      target: {
        files: [new File(['not pdf'], 'notes.txt', { type: 'text/plain' })],
      },
    })
    expect(await screen.findByRole('alert')).toHaveTextContent(
      'Only PDF documents are supported.',
    )

    fireEvent.change(input, {
      target: {
        files: [
          new File(['%PDF-1.7 a document larger than sixteen bytes'], 'large.pdf', {
            type: 'application/pdf',
          }),
        ],
      },
    })
    expect(await screen.findByRole('alert')).toHaveTextContent(
      'TrustPlatform upload limit.',
    )
  })

  it('masks PIN and SAT and blocks submission until all required values are present', async () => {
    await renderPage()

    expect(screen.getByLabelText('Certificate ID')).toBeRequired()
    expect(screen.getByLabelText('X-signer-id')).toBeRequired()
    expect(screen.getByText('Signing identifier sent in the X-signer-id provider header.')).toBeInTheDocument()
    expect(screen.getByLabelText('PIN')).toHaveAttribute('type', 'password')
    expect(screen.getByLabelText('Infocert-SAT')).toHaveAttribute('type', 'password')
    expect(screen.getByRole('button', { name: 'Sign document' })).toBeDisabled()

    const user = userEvent.setup()
    await user.clear(screen.getByLabelText('X-signer-id'))
    await user.upload(
      screen.getByLabelText('Select a PDF document'),
      new File(['%PDF-1.7'], 'contract.pdf', { type: 'application/pdf' }),
    )
    expect(screen.getByRole('button', { name: 'Sign document' })).toBeDisabled()
  })

  it('posts only once, clears secrets, and offers the signed document download', async () => {
    const storageWrite = vi.spyOn(Storage.prototype, 'setItem')
    const user = await renderPage()
    await completeForm(user)
    let resolveSigning: ((response: Response) => void) | undefined
    fetchMock.mockImplementation((input) => {
      if (input === '/api/signing/options') {
        return Promise.resolve(Response.json(supportedOptions))
      }

      return new Promise((resolve) => {
        resolveSigning = resolve
      })
    })

    const submit = screen.getByRole('button', { name: 'Sign document' })
    await user.click(submit)
    expect(screen.getByRole('button', { name: 'Signing document...' })).toBeDisabled()
    expect(screen.getByRole('status')).toHaveTextContent('Signing document...')
    await user.click(screen.getByRole('button', { name: 'Signing document...' }))
    expect(
      fetchMock.mock.calls.filter(([input]) => input === '/api/signing/automatic/cades'),
    ).toHaveLength(1)

    resolveSigning?.(
      new Response(new Blob(['signed document'], { type: 'application/pkcs7-mime' }), {
        status: 200,
        headers: {
          'Content-Type': 'application/pkcs7-mime',
          'Content-Disposition': 'attachment; filename="signed-document-abc.p7m"',
          'X-TrustPlatform-Request-Id': 'operation-123',
        },
      }),
    )

    expect(await screen.findByText('Signing successful')).toBeInTheDocument()
    expect(screen.getByText('Original filename: contract.pdf')).toBeInTheDocument()
    expect(screen.getByText('Resulting filename: signed-document-abc.p7m')).toBeInTheDocument()
    expect(screen.getByText('Operation identifier: operation-123')).toBeInTheDocument()
    expect(
      screen.getByRole('link', { name: 'Download signed document' }),
    ).toHaveAttribute('download', 'signed-document-abc.p7m')
    expect(screen.getByLabelText('PIN')).toHaveValue('')
    expect(screen.getByLabelText('Infocert-SAT')).toHaveValue('')
    expect(storageWrite).not.toHaveBeenCalled()

    const signingCalls = fetchMock.mock.calls.filter(
      ([input]) => input === '/api/signing/automatic/cades',
    )
    expect(signingCalls).toHaveLength(1)
    const sentForm = signingCalls[0]?.[1]?.body as FormData
    expect(sentForm.get('pin')).toBe('pin-secret')
    expect(sentForm.get('sat')).toBe('sat-secret')
    expect(sentForm.get('certificateId')).toBe('INFOCERT+CL_EXT_ID_ZORRO_000000001')
    expect(sentForm.get('xSignerId')).toBe('signer-123')
    expect(sentForm.get('file')).toBeInstanceOf(File)
    expect(screen.getByRole('link', { name: 'Download signed document' })).toHaveAttribute(
      'href',
      'blob:signed-document',
    )
  })

  it('shows a sanitized failure and clears PIN and SAT after completion', async () => {
    const user = await renderPage()
    await completeForm(user)
    fetchMock.mockImplementation(async (input) => {
      if (input === '/api/signing/options') {
        return Response.json(supportedOptions)
      }

      return Response.json(
        { error: 'The signing service could not complete the request.' },
        { status: 502 },
      )
    })

    await user.click(screen.getByRole('button', { name: 'Sign document' }))

    const alert = await screen.findByRole('alert')
    expect(alert).toHaveTextContent('Signing failed')
    expect(alert).toHaveTextContent('The signing service could not complete the request.')
    expect(alert).not.toHaveTextContent('pin-secret')
    expect(alert).not.toHaveTextContent('sat-secret')
    expect(screen.getByLabelText('PIN')).toHaveValue('')
    expect(screen.getByLabelText('Infocert-SAT')).toHaveValue('')
  })

  it('shows packaging values and all signature levels, disabling unsupported levels', async () => {
    await renderPage()
    const packaging = within(screen.getByLabelText('Packaging'))
    const level = within(screen.getByLabelText('Signature level'))

    expect(packaging.getByRole('option', { name: 'ENVELOPED' })).toBeInTheDocument()
    expect(packaging.getByRole('option', { name: 'ENVELOPING' })).toBeInTheDocument()
    expect(level.getByRole('option', { name: 'BASELINE-B' })).toBeInTheDocument()
    expect(level.getByRole('option', { name: 'BASELINE-B' })).toBeEnabled()
    expect(level.getByRole('option', { name: 'BASELINE-T — Not implemented' })).toBeDisabled()
    expect(level.getByRole('option', { name: 'BASELINE-LT — Not implemented' })).toBeDisabled()
    expect(level.getByRole('option', { name: 'BASELINE-LTV — Not implemented' })).toBeDisabled()
  })
})
