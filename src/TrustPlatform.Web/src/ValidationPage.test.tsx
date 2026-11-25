import { fireEvent, render, screen } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import ValidationPage from './ValidationPage'

describe('ValidationPage', () => {
  const fetchMock = vi.fn<typeof fetch>()

  beforeEach(() => {
    vi.stubGlobal('fetch', fetchMock)
    fetchMock.mockReset()
  })

  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('uses one custom file button and accepts the current PoC formats', async () => {
    fetchMock.mockResolvedValue(
      Response.json({
        valid: false,
        validationReport: {
          status: 'INVALID',
          statusDetails: 'Signature is invalid',
          documentFileName: 'signed.xml',
          validationTime: '1735689600000',
          signaturesCount: 1,
          validSignaturesCount: 0,
          messages: [
            { level: 'ERROR', section: 'signature', key: 'validity', value: 'invalid' },
            { level: 'WARNING', section: 'certificate', key: 'expiry', value: 'near expiry' },
            { level: 'INFO', section: 'timestamp', key: 'present', value: 'yes' },
            { level: 'NOTICE', section: 'provider', key: 'note', value: 'additional information' },
          ],
          signatures: [
            {
              signatureId: 'signature-1',
              signatureLevel: 'BASELINE-B',
              signatureFormat: 'XAdES',
              signatureQualification: 'QES',
              signingTime: '1735689600000',
              signerName: 'Example Signer',
              status: 'INVALID',
              signingCertificateId: 'certificate-1',
              certificateChain: ['certificate-1', 'root-1'],
              timestampIds: ['timestamp-1'],
              signedAttributes: {
                contentType: 'application/xml',
                claimedSigningTime: '1735689600000',
                messageDigest: 'digest-value',
                signatureAlgorithm: 'RSA',
              },
            },
          ],
          certificates: [
            {
              certificateId: 'certificate-1',
              subjectDN: 'CN=Example',
              issuerDN: 'CN=Issuer',
              serialNumber: '123',
              notBefore: '1735689600000',
              notAfter: '1767225600000',
              qcTypes: [{ description: 'Qualified certificate', oid: '0.4.0.1862.1.1' }],
            },
          ],
          timestamps: [],
        },
      }),
    )

    render(<ValidationPage />)

    expect(screen.getAllByRole('button', { name: 'Select file' })).toHaveLength(1)
    const input = screen.getByLabelText('Select a signed document') as HTMLInputElement
    expect(input).toHaveClass('visually-hidden-file-input')
    expect(input).toHaveAttribute('tabindex', '-1')
    expect(input.accept).toBe('.pdf,.p7m,.xml,.p7s')

    fireEvent.change(input, {
      target: { files: [new File(['signed content'], 'signed.xml', { type: 'application/xml' })] },
    })

    expect(screen.getByText('signed.xml')).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: 'Validate document' }))

    expect(await screen.findByText('Document validation failed')).toBeInTheDocument()
    expect(screen.getByText('Signature is invalid')).toBeInTheDocument()
    expect(screen.getByText('certificate-1, root-1')).toBeInTheDocument()
    expect(screen.getByText('timestamp-1')).toBeInTheDocument()
    expect(screen.getByText(/Qualified certificate.*0\.4\.0\.1862\.1\.1/)).toBeInTheDocument()
    expect(screen.getByText('No timestamps reported')).toBeInTheDocument()
    expect(screen.getByText('ERROR')).toBeInTheDocument()
    expect(screen.getByText('WARNING')).toBeInTheDocument()
    expect(screen.getByText('INFO')).toBeInTheDocument()
    expect(screen.getByText('OTHER')).toBeInTheDocument()
    expect(screen.getAllByText(/2025-01-01T00:00:00\.000Z/).length).toBeGreaterThan(1)
    expect(document.body).not.toHaveTextContent('certificate-base64')
    expect(fetchMock).toHaveBeenCalledWith(
      '/api/validation/signature',
      expect.objectContaining({ method: 'POST', credentials: 'same-origin' }),
    )
  })

  it('shows only safe provider error fields in the development error panel', async () => {
    fetchMock.mockResolvedValue(
      Response.json(
        {
          title: 'Validation provider request failed',
          status: 502,
          providerStatus: 400,
          providerReasonPhrase: 'Bad Request',
          providerContentType: 'application/problem+json',
          providerCode: 'INVALID_REQUEST',
          message: 'signedDocument.data is required',
          detail: 'The signed document content is missing.',
          correlationId: 'request-correlation-1',
        },
        { status: 502 },
      ),
    )

    render(<ValidationPage />)
    const input = screen.getByLabelText('Select a signed document')
    fireEvent.change(input, {
      target: { files: [new File(['signed content'], 'signed.p7m')] },
    })
    fireEvent.click(screen.getByRole('button', { name: 'Validate document' }))

    const errorPanel = await screen.findByRole('alert')
    expect(errorPanel).toHaveTextContent('Provider status: 400 Bad Request')
    expect(errorPanel).toHaveTextContent('Provider code: INVALID_REQUEST')
    expect(errorPanel).toHaveTextContent('Message: signedDocument.data is required')
    expect(errorPanel).toHaveTextContent('Correlation ID: request-correlation-1')
    expect(errorPanel).not.toHaveTextContent('certificate-base64')
  })

  it('renders a numeric-epoch TOTAL_FAILED provider response as a completed result', async () => {
    fetchMock.mockResolvedValue(
      Response.json({
        valid: false,
        validationReport: {
          validationTime: 1794320342761,
          documentFileName: 'sample_cades_enveloping_b-signed.xml.p7m',
          signaturesCount: 1,
          validSignaturesCount: 0,
          status: 'TOTAL_FAILED',
          statusDetails: 'The available information is sufficient to ascertain the signature to be TOTAL-FAILED',
          signatures: [
            {
              signatureId: 'S-test',
              signatureLevel: 'CAdES_BASELINE_B',
              signatureFormat: 'CAdES',
              signatureQualification: 'INDETERMINATE_QESIG',
              signingTime: 1632317803000,
              signerName: 'Test Signer',
              status: 'INDETERMINATE',
              messages: [
                {
                  level: 'ERROR',
                  section: 'ADES',
                  key: 'BBB_XCV_SUB_ANS',
                  value: 'The certificate validation is not conclusive!',
                },
              ],
              certificateChain: [],
              timestampIds: [],
              signedAttributes: {
                contentType: '1.2.840.113549.1.7.1',
                claimedSigningTime: '2021-09-22T13:36:43Z',
                messageDigest: 'test',
                signatureAlgorithm: 'RSA with SHA256',
              },
            },
          ],
          timestamps: [],
          certificates: [],
        },
      }),
    )

    render(<ValidationPage />)
    fireEvent.change(screen.getByLabelText('Select a signed document'), {
      target: { files: [new File(['signed content'], 'sample.p7m')] },
    })
    fireEvent.click(screen.getByRole('button', { name: 'Validate document' }))

    expect(await screen.findByText('Document validation failed')).toBeInTheDocument()
    expect(screen.getByText('TOTAL_FAILED')).toBeInTheDocument()
    expect(screen.getAllByRole('listitem')[0]).toHaveTextContent(
      'The certificate validation is not conclusive!',
    )
    expect(screen.getByText('Signatures', { selector: 'dt' }).parentElement).toHaveTextContent('0/1')
    expect(screen.getByText('S-test')).toBeInTheDocument()
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  })

  it('shows the safe response as formatted JSON and collapses it for every new validation', async () => {
    const safeResponse = {
      valid: false,
      validationReport: {
        signatureValidationPolicyName: 'QES AdESQC TL based',
        signatureValidationPolicyDescription: 'Example policy',
        signatureValidationPolicyOid: '0.4.0.191724.1.1',
        qsvsPolicyOid: '1.3.76.36.1.1.90',
        validationTime: 1794320342761,
        documentFileName: 'sample.p7m',
        signaturesCount: 0,
        validSignaturesCount: 0,
        status: 'TOTAL_FAILED',
        statusDetails: 'Validation failed',
        signatures: [],
        timestamps: [],
        certificates: [{ certificateId: 'certificate-1' }],
      },
    }
    fetchMock.mockResolvedValue(Response.json(safeResponse))
    const { container } = render(<ValidationPage />)
    fireEvent.change(screen.getByLabelText('Select a signed document'), {
      target: { files: [new File(['signed content'], 'sample.p7m')] },
    })
    const submitButton = screen.getByRole('button', { name: 'Validate document' })
    fireEvent.click(submitButton)

    expect(await screen.findByRole('button', { name: 'Show raw JSON' })).toHaveAttribute(
      'aria-expanded',
      'false',
    )
    expect(container.querySelector('pre code')).not.toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: 'Show raw JSON' }))
    const code = container.querySelector('pre code')
    expect(code).toBeInTheDocument()
    expect(code?.textContent).toBe(JSON.stringify(safeResponse, null, 2))
    expect(code?.textContent).toContain('"signatureValidationPolicyName"')
    expect(code?.textContent).not.toContain('"data"')
    expect(code?.textContent).not.toContain('certificate-base64')
    expect(screen.getByRole('button', { name: 'Hide raw JSON' })).toHaveAttribute(
      'aria-expanded',
      'true',
    )

    fireEvent.click(submitButton)
    expect(await screen.findByRole('button', { name: 'Show raw JSON' })).toHaveAttribute(
      'aria-expanded',
      'false',
    )
    expect(container.querySelector('pre code')).not.toBeInTheDocument()
  })

  it('shows sanitized parse diagnostics in a Development-only expandable section', async () => {
    fetchMock.mockResolvedValue(
      Response.json(
        {
          title: 'Validation provider response failed',
          status: 502,
          providerStatus: 200,
          providerReasonPhrase: 'OK',
          providerContentType: 'application/json',
          responseShape: 'JSON object; valid: String',
          parsingError: 'The JSON value could not be converted to System.Boolean.',
          responseExcerpt: '{\n  "valid": "bad"\n}',
          correlationId: 'parse-correlation-1',
        },
        { status: 502 },
      ),
    )

    render(<ValidationPage />)
    fireEvent.change(screen.getByLabelText('Select a signed document'), {
      target: { files: [new File(['signed content'], 'sample.p7m')] },
    })
    fireEvent.click(screen.getByRole('button', { name: 'Validate document' }))

    const errorPanel = await screen.findByRole('alert')
    fireEvent.click(screen.getByText('Technical details'))
    expect(errorPanel).toHaveTextContent('Provider HTTP status: 200 OK')
    expect(errorPanel).toHaveTextContent('Provider Content-Type: application/json')
    expect(errorPanel).toHaveTextContent('Parsing error: The JSON value could not be converted to System.Boolean.')
    expect(errorPanel).toHaveTextContent('"valid": "bad"')
    expect(errorPanel).toHaveTextContent('Correlation ID: parse-correlation-1')
  })

  it('rejects unsupported extensions and clears an earlier selection', async () => {
    render(<ValidationPage />)
    const input = screen.getByLabelText('Select a signed document')

    fireEvent.change(input, {
      target: { files: [new File(['signed content'], 'signed.pdf')] },
    })
    expect(screen.getByText('signed.pdf')).toBeInTheDocument()

    fireEvent.change(input, {
      target: { files: [new File(['unsupported'], 'document.txt')] },
    })

    expect(screen.getByRole('alert')).toHaveTextContent('Supported TrustPlatform PoC formats')
    expect(screen.queryByText('signed.pdf')).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Validate document' })).toBeDisabled()
  })
})
