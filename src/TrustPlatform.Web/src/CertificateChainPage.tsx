import { useEffect, useRef, useState, type FormEvent } from 'react'
import {
  getCertificateChain,
  getCertificateQueryDefaults,
  type CertificateChainItem,
} from './api/certificateClient'

type ChainState = 'idle' | 'loading' | 'success' | 'error'

function formatExpiration(date: string | null): string {
  if (!date) {
    return 'Expiration date unavailable'
  }

  const parsed = new Date(date)
  return Number.isNaN(parsed.getTime())
    ? 'Expiration date unavailable'
    : parsed.toLocaleString()
}

export default function CertificateChainPage() {
  const submitting = useRef(false)
  const [certificateId, setCertificateId] = useState('')
  const [xSignerId, setXSignerId] = useState('')
  const [chain, setChain] = useState<CertificateChainItem[]>([])
  const [chainState, setChainState] = useState<ChainState>('idle')
  const [error, setError] = useState<string | null>(null)
  const [isSubmitting, setIsSubmitting] = useState(false)
  const [defaultsLoading, setDefaultsLoading] = useState(true)

  useEffect(() => {
    const controller = new AbortController()
    void getCertificateQueryDefaults(controller.signal)
      .then((defaults) => {
        setCertificateId(defaults.certificateId)
        setXSignerId(defaults.xSignerId)
      })
      .finally(() => {
        if (!controller.signal.aborted) {
          setDefaultsLoading(false)
        }
      })

    return () => controller.abort()
  }, [])

  const retrieveChain = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    if (!certificateId.trim() || !xSignerId.trim() || submitting.current) {
      return
    }

    submitting.current = true
    setIsSubmitting(true)
    setChainState('loading')
    setError(null)
    setChain([])

    try {
      const result = await getCertificateChain(certificateId, xSignerId)
      setChain(result)
      setChainState('success')
    } catch (requestError: unknown) {
      setError(
        requestError instanceof Error
          ? requestError.message
          : 'The certificate chain could not be loaded.',
      )
      setChainState('error')
    } finally {
      setIsSubmitting(false)
      submitting.current = false
    }
  }

  const rawResponse = chain

  return (
    <section aria-labelledby="certificate-chain-heading" className="certificate-page">
      <p className="eyebrow">Certificate workflow</p>
      <h1 id="certificate-chain-heading">Certificate Chain</h1>

      <form className="signing-form" onSubmit={(event) => void retrieveChain(event)}>
        <div className="form-field">
          <label htmlFor="certificate-chain-certificate-id">Certificate ID</label>
          <input
            autoComplete="off"
            id="certificate-chain-certificate-id"
            maxLength={128}
            onChange={(event) => setCertificateId(event.target.value)}
            required
            value={certificateId}
          />
        </div>

        <div className="form-field">
          <label htmlFor="certificate-chain-x-signer-id">X-signer-id</label>
          <input
            autoComplete="off"
            disabled={defaultsLoading}
            id="certificate-chain-x-signer-id"
            maxLength={256}
            onChange={(event) => setXSignerId(event.target.value)}
            required
            value={xSignerId}
          />
          <p>Sent in the SIGN API X-signer-id HTTP header.</p>
        </div>

        <button
          className="primary-button"
          disabled={defaultsLoading || isSubmitting || certificateId.trim() === '' || xSignerId.trim() === ''}
          type="submit"
        >
          {isSubmitting ? 'Retrieving chain…' : 'Retrieve certificate chain'}
        </button>
      </form>

      {chainState === 'loading' && <p role="status">Loading certificate chain…</p>}

      {chainState === 'error' && error && (
        <div className="validation-server-error" role="alert">
          <p>{error}</p>
        </div>
      )}

      {chainState === 'success' && chain.length === 0 && (
        <p className="no-data">Nessun certificato presente nella catena restituita dal provider.</p>
      )}

      {chainState === 'success' && chain.length > 0 && (
        <div className="validation-result">
          {chain.map((item, index) => (
            <article className="certificate-card" key={`${item.ids[0] ?? 'certificate'}-${index}`}>
              <h2>Chain position {index + 1}</h2>
              <dl className="summary-list">
                <div>
                  <dt>Subject</dt>
                  <dd>{item.subject ?? '—'}</dd>
                </div>
                <div>
                  <dt>Issuer</dt>
                  <dd>{item.issuer ?? '—'}</dd>
                </div>
                <div>
                  <dt>Status</dt>
                  <dd>{item.status || 'unknown'}</dd>
                </div>
                <div>
                  <dt>Expiration date</dt>
                  <dd>{formatExpiration(item.expirationDate)}</dd>
                </div>
              </dl>

              <div>
                <h3>IDs</h3>
                {item.ids.length > 0 ? (
                  <ul className="timestamp-list">
                    {item.ids.map((id) => (
                      <li key={id}>{id}</li>
                    ))}
                  </ul>
                ) : (
                  <p>No identifiers were returned.</p>
                )}
              </div>

              <details>
                <summary>Certificate content</summary>
                <pre className="validation-raw-json">
                  <code>{item.certificate || 'Certificate content unavailable'}</code>
                </pre>
              </details>
            </article>
          ))}

          <details>
            <summary>Raw JSON Response</summary>
            <pre className="validation-raw-json">
              <code>{JSON.stringify(rawResponse, null, 2)}</code>
            </pre>
          </details>
        </div>
      )}
    </section>
  )
}
