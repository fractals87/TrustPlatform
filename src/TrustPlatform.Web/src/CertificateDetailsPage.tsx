import { useEffect, useRef, useState, type FormEvent } from 'react'
import { getCertificateQueryDefaults, getConfiguredCertificate, type CertificateDetailResponse } from './api/certificateClient'

type QueryState = 'idle' | 'loading' | 'success' | 'error'

function formatExpiration(date: string | null | undefined): string {
  if (!date) {
    return 'Expiration date unavailable'
  }

  const parsed = new Date(date)
  if (Number.isNaN(parsed.getTime())) {
    return 'Expiration date unavailable'
  }

  return parsed.toLocaleString()
}

export default function CertificateDetailsPage() {
  const submitting = useRef(false)
  const [certificateId, setCertificateId] = useState('')
  const [xSignerId, setXSignerId] = useState('')
  const [defaultsLoading, setDefaultsLoading] = useState(true)
  const [defaultsError, setDefaultsError] = useState<string | null>(null)
  const [certificate, setCertificate] = useState<CertificateDetailResponse | null>(null)
  const [queryState, setQueryState] = useState<QueryState>('idle')
  const [error, setError] = useState<string | null>(null)
  const [isSubmitting, setIsSubmitting] = useState(false)

  useEffect(() => {
    const controller = new AbortController()
    void getCertificateQueryDefaults(controller.signal)
      .then((defaults) => {
        setCertificateId(defaults.certificateId)
        setXSignerId(defaults.xSignerId)
      })
      .catch((loadError: unknown) => {
        if (!controller.signal.aborted) {
          setDefaultsError(
            loadError instanceof Error
              ? loadError.message
              : 'Certificate query defaults are unavailable.',
          )
        }
      })
      .finally(() => {
        if (!controller.signal.aborted) {
          setDefaultsLoading(false)
        }
      })

    return () => controller.abort()
  }, [])

  const retrieveCertificate = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    if (!certificateId.trim() || !xSignerId.trim() || submitting.current) {
      return
    }

    submitting.current = true
    setIsSubmitting(true)
    setQueryState('loading')
    setError(null)
    setCertificate(null)

    try {
      const result = await getConfiguredCertificate(certificateId.trim(), xSignerId.trim())
      setCertificate(result)
      setQueryState('success')
    } catch (requestError: unknown) {
      setError(
        requestError instanceof Error
          ? requestError.message
          : 'The certificate details could not be loaded.',
      )
      setQueryState('error')
    } finally {
      setIsSubmitting(false)
      submitting.current = false
    }
  }

  return (
    <section aria-labelledby="certificate-details-heading" className="certificate-page">
      <p className="eyebrow">Certificate workflow</p>
      <h1 id="certificate-details-heading">Certificate details</h1>

      <form className="signing-form" onSubmit={(event) => void retrieveCertificate(event)}>
        <div className="form-field">
          <label htmlFor="certificate-details-x-signer-id">X-signer-id</label>
          <input
            autoComplete="off"
            disabled={defaultsLoading}
            id="certificate-details-x-signer-id"
            maxLength={256}
            onChange={(event) => setXSignerId(event.target.value)}
            required
            value={xSignerId}
          />
          <p>Sent in the SIGN API X-signer-id HTTP header.</p>
        </div>

        <div className="form-field">
          <label htmlFor="certificate-details-certificate-id">Certificate ID</label>
          <input
            autoComplete="off"
            disabled={defaultsLoading}
            id="certificate-details-certificate-id"
            maxLength={128}
            onChange={(event) => setCertificateId(event.target.value)}
            required
            value={certificateId}
          />
          <p>Used as the certificate ID path segment in the SIGN API request.</p>
        </div>

        {defaultsLoading && <p role="status">Loading certificate query defaults…</p>}
        {defaultsError && (
          <p className="field-error" role="alert">
            {defaultsError} Enter the certificate ID and X-signer-id manually.
          </p>
        )}

        <button
          className="primary-button"
          disabled={
            defaultsLoading ||
            isSubmitting ||
            certificateId.trim() === '' ||
            xSignerId.trim() === ''
          }
          type="submit"
        >
          {isSubmitting ? 'Retrieving certificate…' : 'Retrieve certificate details'}
        </button>
      </form>

      {queryState === 'loading' && <p role="status">Loading certificate details…</p>}

      {queryState === 'error' && error && (
        <div className="validation-server-error" role="alert">
          <p>{error}</p>
        </div>
      )}

      {queryState === 'success' && certificate && (
        <article className="certificate-card">
          <h2>Certificate ID</h2>
          <p>{certificate.certificateId}</p>
          <dl className="summary-list">
            <div>
              <dt>Subject</dt>
              <dd>{certificate.subject ?? '—'}</dd>
            </div>
            <div>
              <dt>Issuer</dt>
              <dd>{certificate.issuer ?? '—'}</dd>
            </div>
            <div>
              <dt>Status</dt>
              <dd>{certificate.status ?? '—'}</dd>
            </div>
            <div>
              <dt>Expiration date</dt>
              <dd>{formatExpiration(certificate.expirationDate)}</dd>
            </div>
            <div>
              <dt>Expiration state</dt>
              <dd>
                {certificate.isExpired === null || certificate.isExpired === undefined
                  ? 'Unknown'
                  : certificate.isExpired
                    ? 'Expired'
                    : 'Active'}
              </dd>
            </div>
          </dl>

          <div>
            <h3>Identifiers</h3>
            <dl className="summary-list">
              <div>
                <dt>Alias ID</dt>
                <dd>{certificate.aliasId ?? '—'}</dd>
              </div>
              <div>
                <dt>Key ID</dt>
                <dd>{certificate.keyId ?? '—'}</dd>
              </div>
              <div>
                <dt>DN qualifier ID</dt>
                <dd>{certificate.dnQualifierId ?? '—'}</dd>
              </div>
            </dl>
            {certificate.ids.length > 0 && (
              <ul className="timestamp-list">
                {certificate.ids.map((value) => (
                  <li key={value}>{value}</li>
                ))}
              </ul>
            )}
          </div>

          <div>
            <h3>Certificate data</h3>
            <p>{certificate.certificateAvailable ? 'Certificate data available' : 'Certificate data unavailable'}</p>
          </div>
        </article>
      )}
    </section>
  )
}
