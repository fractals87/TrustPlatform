import { useRef, useState, type DragEvent, type FormEvent } from 'react'
import {
  validateDocument,
  ValidationRequestError,
  type CertificateResult,
  type SignatureResult,
  type ValidationApiError,
  type ValidationMessage,
  type ValidationReport,
} from './api/validationClient'

const allowedFileExtensions = new Set(['.pdf', '.p7m', '.xml', '.p7s'])

function formatFileSize(value: number): string {
  if (value < 1024) {
    return `${value} B`
  }

  if (value < 1024 * 1024) {
    return `${(value / 1024).toFixed(1)} KB`
  }

  return `${(value / (1024 * 1024)).toFixed(2)} MB`
}

function formatEpochTime(value: string | number | null | undefined): string {
  if (value === null || value === undefined || value === '') {
    return '—'
  }

  const numericValue = typeof value === 'string' ? Number(value) : value
  if (!Number.isFinite(numericValue)) {
    return String(value)
  }

  const date = new Date(numericValue)
  if (Number.isNaN(date.getTime())) {
    return String(value)
  }

  return `${date.toLocaleString()} (${date.toISOString()})`
}

function normalizeWhitespace(value: string | null | undefined): string {
  if (!value) {
    return '—'
  }

  return value.replace(/\s+/g, ' ').trim()
}

function severityClass(level: string | null | undefined): string {
  const normalized = (level ?? 'INFO').toUpperCase()
  if (normalized === 'ERROR') {
    return 'message-error'
  }

  if (normalized === 'WARNING') {
    return 'message-warning'
  }

  return 'message-info'
}

function messageGroup(level: string | null | undefined): string {
  const normalized = (level ?? 'INFO').toUpperCase()
  if (normalized === 'ERROR' || normalized === 'WARNING' || normalized === 'INFO') {
    return normalized
  }

  return 'OTHER'
}

function MessageGroups({ messages }: { messages: ValidationMessage[] }) {
  const groups = ['ERROR', 'WARNING', 'INFO', 'OTHER']
  const presentGroups = groups
    .map((group) => ({
      group,
      items: messages.filter((message) => messageGroup(message.level) === group),
    }))
    .filter(({ items }) => items.length > 0)

  if (presentGroups.length === 0) {
    return <p className="no-data">No validation messages reported.</p>
  }

  return (
    <div className="message-groups">
      {presentGroups.map(({ group, items }) => (
        <div key={group} className="message-group">
          <h3>{group}</h3>
          <ul className="message-list">
            {items.map((message, itemIndex) => (
              <li className={severityClass(message.level)} key={`${group}-${itemIndex}`}>
                <strong>{message.section ?? 'General'}:</strong> {message.key ?? 'message'} — {message.value ?? '—'}
              </li>
            ))}
          </ul>
        </div>
      ))}
    </div>
  )
}

function ValidationPage() {
  const fileInputRef = useRef<HTMLInputElement>(null)
  const [selectedFile, setSelectedFile] = useState<File | null>(null)
  const [fileError, setFileError] = useState<string | null>(null)
  const [error, setError] = useState<ValidationApiError | null>(null)
  const [isSubmitting, setIsSubmitting] = useState(false)
  const [showRawResponse, setShowRawResponse] = useState(false)
  const [result, setResult] = useState<{
    valid: boolean
    validationReport?: ValidationReport | null
  } | null>(null)

  const chooseFile = (files: FileList | File[]) => {
    setError(null)
    setResult(null)
    if (!files || files.length !== 1) {
      setSelectedFile(null)
      setFileError('Select exactly one signed file.')
      return
    }

    const file = files[0]
    const extension = file.name.slice(file.name.lastIndexOf('.')).toLowerCase()
    if (file.size === 0) {
      setSelectedFile(null)
      setFileError('The selected file is empty.')
      return
    }

    if (!allowedFileExtensions.has(extension)) {
      setSelectedFile(null)
      setFileError('Supported TrustPlatform PoC formats are .pdf, .p7m, .xml, and .p7s.')
      return
    }

    setSelectedFile(file)
    setFileError(null)
  }

  const clearFile = () => {
    setSelectedFile(null)
    setFileError(null)
    setError(null)
    setResult(null)
    if (fileInputRef.current) {
      fileInputRef.current.value = ''
    }
  }

  const submit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    if (!selectedFile || isSubmitting) {
      return
    }

    setIsSubmitting(true)
    setError(null)
    setResult(null)
    setShowRawResponse(false)

    const formData = new FormData()
    formData.append('file', selectedFile)

    try {
      const payload = await validateDocument(formData)
      setResult(payload)
    } catch (submitError) {
      setError(
        submitError instanceof ValidationRequestError
          ? submitError.validationError
          : {
              title: 'Validation request failed',
              status: 0,
              message: 'The validation request could not be completed.',
            },
      )
    } finally {
      setIsSubmitting(false)
    }
  }

  const report = result?.validationReport

  return (
    <section aria-labelledby="validation-page-heading">
      <p className="eyebrow">Document validation</p>
      <h1 id="validation-page-heading">Validation</h1>

      <form className="validation-form" onSubmit={submit}>
        <div
          className="file-dropzone"
          onDragOver={(event: DragEvent<HTMLDivElement>) => event.preventDefault()}
          onDrop={(event: DragEvent<HTMLDivElement>) => {
            event.preventDefault()
            if (event.dataTransfer.files.length > 0) {
              chooseFile(event.dataTransfer.files)
            }
          }}
        >
          <p className="file-dropzone-title">Drag and drop one signed file here</p>
          <p className="file-dropzone-help">
            Supported TrustPlatform PoC formats: PDF, P7M, XML, and P7S. The configured upload limit is checked by the server.
          </p>
          <input
            accept=".pdf,.p7m,.xml,.p7s"
            aria-label="Select a signed document"
            className="visually-hidden-file-input"
            id="validation-file-input"
            onChange={(event) => {
              if (event.target.files) {
                chooseFile(event.target.files)
              }
              event.currentTarget.value = ''
            }}
            ref={fileInputRef}
            tabIndex={-1}
            type="file"
          />
          <button
            className="primary-button"
            onClick={() => fileInputRef.current?.click()}
            type="button"
          >
            {selectedFile ? 'Replace file' : 'Select file'}
          </button>
        </div>

        <div className="selected-file-panel">
          <span className="selected-file-label">Selected file:</span>
          {selectedFile ? (
            <>
              <p className="selected-file-name">{selectedFile.name}</p>
              <p className="selected-file-meta">
                {formatFileSize(selectedFile.size)} · {selectedFile.type || 'Unknown content type'}
              </p>
            </>
          ) : (
            <p className="selected-file-empty">No file selected.</p>
          )}
        </div>

        {fileError && <p className="error-message" role="alert">{fileError}</p>}

        <div className="validation-actions">
          {selectedFile && (
            <button className="text-button" onClick={clearFile} type="button">
              Remove
            </button>
          )}
          <button
            className="primary-button"
            disabled={!selectedFile || fileError !== null || isSubmitting}
            type="submit"
          >
            {isSubmitting ? 'Validating document...' : 'Validate document'}
          </button>
        </div>
      </form>

      {error && (
        <div className="validation-server-error" role="alert">
          <h2>{error.title}</h2>
          {import.meta.env.DEV && error.providerStatus !== undefined ? (
            <p>
              Provider status: {error.providerStatus}
              {error.providerReasonPhrase ? ` ${error.providerReasonPhrase}` : ''}
            </p>
          ) : (
            <p>HTTP status: {error.status}</p>
          )}
          {error.status === 0 && error.message && <p>{error.message}</p>}
          {import.meta.env.DEV && error.providerCode && (
            <p>Provider code: {error.providerCode}</p>
          )}
          {import.meta.env.DEV && error.message && <p>Message: {error.message}</p>}
          {import.meta.env.DEV && error.detail && <p>Detail: {error.detail}</p>}
          {error.correlationId && <p>Correlation ID: {error.correlationId}</p>}
          {import.meta.env.DEV &&
            (error.providerContentType ||
              error.providerReasonPhrase ||
              error.responseShape ||
              error.parsingError ||
              error.responseExcerpt) && (
              <details>
                <summary>Technical details</summary>
                {error.providerStatus !== undefined && (
                  <p>
                    Provider HTTP status: {error.providerStatus}
                    {error.providerReasonPhrase ? ` ${error.providerReasonPhrase}` : ''}
                  </p>
                )}
                {error.providerReasonPhrase && <p>Provider reason: {error.providerReasonPhrase}</p>}
                {error.providerContentType && <p>Provider Content-Type: {error.providerContentType}</p>}
                {error.responseShape && <p>Response body shape: {error.responseShape}</p>}
                {error.parsingError && <p>Parsing error: {error.parsingError}</p>}
                {error.responseExcerpt && (
                  <>
                    <p>Sanitized provider response:</p>
                    <pre className="validation-response-excerpt">{error.responseExcerpt}</pre>
                  </>
                )}
              </details>
            )}
        </div>
      )}

      {result && (
        <article className="validation-result" aria-live="polite">
          <div className={`validation-summary ${result.valid ? 'success' : 'error'}`}>
            <p className="validation-summary-title">
              {result.valid ? 'Document validation passed' : 'Document validation failed'}
            </p>
            <dl className="summary-list">
              <div>
                <dt>Overall validity</dt>
                <dd>{result.valid ? 'Valid' : 'Invalid'}</dd>
              </div>
              <div>
                <dt>Provider status</dt>
                <dd>{report?.status ?? 'Unknown'}</dd>
              </div>
              <div>
                <dt>Status details</dt>
                <dd>{report?.statusDetails ?? '—'}</dd>
              </div>
              <div>
                <dt>Document filename</dt>
                <dd>{report?.documentFileName ?? selectedFile?.name ?? '—'}</dd>
              </div>
              <div>
                <dt>Validation time</dt>
                <dd>{formatEpochTime(report?.validationTime)}</dd>
              </div>
              <div>
                <dt>Signatures</dt>
                <dd>
                  {String(report?.validSignaturesCount ?? 0)}/
                  {String(report?.signaturesCount ?? report?.signatures?.length ?? 0)}
                </dd>
              </div>
            </dl>
          </div>

          <section className="validation-section">
            <h2>Validation policy</h2>
            <dl className="summary-list compact">
              <div>
                <dt>Policy name</dt>
                <dd>{report?.signatureValidationPolicyName ?? '—'}</dd>
              </div>
              <div>
                <dt>Policy description</dt>
                <dd>{normalizeWhitespace(report?.signatureValidationPolicyDescription)}</dd>
              </div>
              <div>
                <dt>Policy OID</dt>
                <dd>{report?.signatureValidationPolicyOid ?? '—'}</dd>
              </div>
              <div>
                <dt>QSVS policy OID</dt>
                <dd>{report?.qsvsPolicyOid ?? '—'}</dd>
              </div>
            </dl>
          </section>

          <section className="validation-section">
            <h2>Signatures</h2>
            {report?.signatures && report.signatures.length > 0 ? (
              report.signatures.map((signature: SignatureResult, index: number) => (
                <article className="signature-card" key={signature.signatureId ?? `${index}`}>
                  <h3>Signature {index + 1}</h3>
                  <dl className="summary-list compact">
                    <div><dt>Signature ID</dt><dd>{signature.signatureId ?? '—'}</dd></div>
                    <div><dt>Signature level</dt><dd>{signature.signatureLevel ?? '—'}</dd></div>
                    <div><dt>Signature format</dt><dd>{signature.signatureFormat ?? '—'}</dd></div>
                    <div><dt>Qualification</dt><dd>{signature.signatureQualification ?? '—'}</dd></div>
                    <div><dt>Signer name</dt><dd>{signature.signerName ?? '—'}</dd></div>
                    <div><dt>Signing time</dt><dd>{formatEpochTime(signature.signingTime)}</dd></div>
                    <div><dt>Status</dt><dd>{signature.status ?? '—'}</dd></div>
                    <div><dt>Signing certificate ID</dt><dd>{signature.signingCertificateId ?? '—'}</dd></div>
                    <div><dt>Certificate chain</dt><dd>{signature.certificateChain?.join(', ') || '—'}</dd></div>
                    <div><dt>Timestamp IDs</dt><dd>{signature.timestampIds?.join(', ') || '—'}</dd></div>
                    <div><dt>Signed content type</dt><dd>{signature.signedAttributes?.contentType ?? '—'}</dd></div>
                    <div><dt>Claimed signing time</dt><dd>{formatEpochTime(signature.signedAttributes?.claimedSigningTime)}</dd></div>
                    <div><dt>Message digest</dt><dd>{signature.signedAttributes?.messageDigest ?? '—'}</dd></div>
                    <div><dt>Signature algorithm</dt><dd>{signature.signedAttributes?.signatureAlgorithm ?? '—'}</dd></div>
                  </dl>
                  {signature.messages && signature.messages.length > 0 && (
                    <section className="signature-messages">
                      <h4>Signature messages</h4>
                      <MessageGroups messages={signature.messages} />
                    </section>
                  )}
                </article>
              ))
            ) : (
              <p className="no-data">No signatures reported.</p>
            )}
          </section>

          <section className="validation-section">
            <h2>Validation messages</h2>
            <MessageGroups messages={report?.messages ?? []} />
          </section>

          <section className="validation-section">
            <h2>Certificates</h2>
            {report?.certificates && report.certificates.length > 0 ? (
              report.certificates.map((certificate: CertificateResult, index: number) => (
                <article className="certificate-card" key={certificate.certificateId ?? `${index}`}>
                  <h3>Certificate {index + 1}</h3>
                  <dl className="summary-list compact">
                    <div><dt>Certificate ID</dt><dd>{certificate.certificateId ?? '—'}</dd></div>
                    <div><dt>Subject DN</dt><dd>{certificate.subjectDN ?? '—'}</dd></div>
                    <div><dt>Issuer DN</dt><dd>{certificate.issuerDN ?? '—'}</dd></div>
                    <div><dt>Serial number</dt><dd>{certificate.serialNumber ?? '—'}</dd></div>
                    <div><dt>Validity start</dt><dd>{formatEpochTime(certificate.notBefore)}</dd></div>
                    <div><dt>Validity end</dt><dd>{formatEpochTime(certificate.notAfter)}</dd></div>
                    <div>
                      <dt>QC types</dt>
                      <dd>
                        {certificate.qcTypes && certificate.qcTypes.length > 0
                          ? certificate.qcTypes.map((qc, qcIndex) => (
                          <span key={`${qc.oid ?? 'qc'}-${qcIndex}`}>
                            {qc.description ?? '—'}{qc.oid ? ` (${qc.oid})` : ''}
                            {qcIndex < (certificate.qcTypes?.length ?? 0) - 1 ? ', ' : ''}
                          </span>
                          ))
                          : '—'}
                      </dd>
                    </div>
                  </dl>
                </article>
              ))
            ) : (
              <p className="no-data">No certificates reported.</p>
            )}
          </section>

          <section className="validation-section">
            <h2>Timestamps</h2>
            {report?.timestamps && report.timestamps.length > 0 ? (
              <ul className="timestamp-list">
                {report.timestamps.map((timestamp, index) => (
                  <li key={`${timestamp.id ?? 'timestamp'}-${index}`}>
                    <strong>{timestamp.type ?? 'Timestamp'}:</strong> {formatEpochTime(timestamp.dateTime)}
                  </li>
                ))}
              </ul>
            ) : (
              <p className="no-data">No timestamps reported</p>
            )}
          </section>

          <section aria-labelledby="validation-raw-response-heading" className="validation-section">
            <h2 id="validation-raw-response-heading">Raw validation response</h2>
            <button
              aria-controls={showRawResponse ? 'validation-raw-response-json' : undefined}
              aria-expanded={showRawResponse}
              className="secondary-button"
              onClick={() => setShowRawResponse((expanded) => !expanded)}
              type="button"
            >
              {showRawResponse ? 'Hide raw JSON' : 'Show raw JSON'}
            </button>
            {showRawResponse && (
              <pre className="validation-raw-json" id="validation-raw-response-json">
                <code>{JSON.stringify(result, null, 2)}</code>
              </pre>
            )}
          </section>
        </article>
      )}
    </section>
  )
}

export default ValidationPage
