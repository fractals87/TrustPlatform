import { useEffect, useRef, useState, type FormEvent } from 'react'
import { getCertificateQueryDefaults, getCertificates, type CertificateSummary } from './api/certificateClient'

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

export default function CertificatesListPage() {
  const submitting = useRef(false)
  const [certificates, setCertificates] = useState<CertificateSummary[]>([])
  const [xSignerId, setXSignerId] = useState('')
  const [defaultsLoading, setDefaultsLoading] = useState(true)
  const [defaultsError, setDefaultsError] = useState<string | null>(null)
  const [queryState, setQueryState] = useState<QueryState>('idle')
  const [error, setError] = useState<string | null>(null)
  const [isSubmitting, setIsSubmitting] = useState(false)

  useEffect(() => {
    const controller = new AbortController()
    void getCertificateQueryDefaults(controller.signal)
      .then((defaults) => {
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

  const retrieveCertificates = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    if (!xSignerId.trim() || submitting.current) {
      return
    }

    submitting.current = true
    setIsSubmitting(true)
    setQueryState('loading')
    setError(null)
    setCertificates([])

    try {
      const result = await getCertificates(xSignerId.trim())
      setCertificates(result.certificates)
      setQueryState('success')
    } catch (requestError: unknown) {
      setError(
        requestError instanceof Error
          ? requestError.message
          : 'The certificate list could not be loaded.',
      )
      setQueryState('error')
    } finally {
      setIsSubmitting(false)
      submitting.current = false
    }
  }

  return (
    <section aria-labelledby="certificates-list-heading" className="certificate-page">
      <p className="eyebrow">Certificate workflow</p>
      <h1 id="certificates-list-heading">Certificates</h1>

      <form className="signing-form" onSubmit={(event) => void retrieveCertificates(event)}>
        <div className="form-field">
          <label htmlFor="certificates-list-x-signer-id">X-signer-id</label>
          <input
            autoComplete="off"
            disabled={defaultsLoading}
            id="certificates-list-x-signer-id"
            maxLength={256}
            onChange={(event) => setXSignerId(event.target.value)}
            required
            value={xSignerId}
          />
          <p>Sent in the SIGN API X-signer-id HTTP header.</p>
        </div>

        {defaultsLoading && <p role="status">Loading certificate query defaults…</p>}
        {defaultsError && <p className="field-error" role="alert">{defaultsError} Enter the X-signer-id manually.</p>}

        <button
          className="primary-button"
          disabled={defaultsLoading || isSubmitting || xSignerId.trim() === ''}
          type="submit"
        >
          {isSubmitting ? 'Retrieving certificates…' : 'Retrieve certificates'}
        </button>
      </form>

      {queryState === 'loading' && <p role="status">Loading certificates…</p>}

      {queryState === 'error' && error && (
        <div className="validation-server-error" role="alert">
          <p>{error}</p>
        </div>
      )}

      {queryState === 'success' && certificates.length === 0 && (
        <p className="no-data">No certificates were returned for this X-signer-id.</p>
      )}

      {queryState === 'success' && certificates.length > 0 && (
        <div className="validation-result">
          {certificates.map((certificate, index) => {
            const primaryId =
              certificate.aliasId ??
              certificate.keyId ??
              certificate.dnQualifierId ??
              certificate.ids?.[0] ??
              `certificate-${index + 1}`

            return (
              <article className="certificate-card" key={primaryId || `certificate-${index}`}>
                <h2>{primaryId}</h2>
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
                  <div>
                    <dt>Subject</dt>
                    <dd>{certificate.subject ?? '—'}</dd>
                  </div>
                  <div>
                    <dt>Issuer</dt>
                    <dd>{certificate.issuer ?? '—'}</dd>
                  </div>
                  <div>
                    <dt>Provider status</dt>
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
                {certificate.ids && certificate.ids.length > 0 && (
                  <div>
                    <h3>Identifiers</h3>
                    <ul className="timestamp-list">
                      {certificate.ids.map((value) => (
                        <li key={value}>{value}</li>
                      ))}
                    </ul>
                  </div>
                )}
              </article>
            )
          })}
        </div>
      )}
    </section>
  )
}
