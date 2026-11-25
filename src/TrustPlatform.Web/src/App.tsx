import { useCallback, useEffect, useState } from 'react'
import { getHealth } from './api/healthClient'
import { testSignApiAuthentication } from './api/signApiDiagnosticsClient'
import { testValidationApiAuthentication } from './api/validationApiDiagnosticsClient'
import SigningPage from './SigningPage'
import ValidationPage from './ValidationPage'
import CertificatesListPage from './CertificatesListPage'
import CertificateDetailsPage from './CertificateDetailsPage'
import CertificateChainPage from './CertificateChainPage'
import './App.css'

const navigationItems = [
  { id: 'home', label: 'Home' },
  { id: 'signing', label: 'Signing' },
  { id: 'validation', label: 'Validation' },
  { id: 'certificates-list', label: 'Certificates list' },
  { id: 'certificate-details', label: 'Certificate details' },
  { id: 'certificate-chain', label: 'Certificate chain' },
  { id: 'diagnostics', label: 'Diagnostics' },
] as const

type PageId = (typeof navigationItems)[number]['id']

type HealthState =
  | { status: 'loading' }
  | { status: 'success' }
  | { status: 'error'; message: string }

type SignApiAuthenticationState =
  | { status: 'idle' | 'loading' }
  | {
      status: 'success'
      tokenAcquired: boolean
      tokenType: 'Bearer'
      expiresIn: number
      cached: boolean
    }
  | { status: 'error'; message: string }

type ValidationApiAuthenticationState =
  | { status: 'idle' | 'loading' }
  | {
      status: 'success'
      tokenAcquired: boolean
      tokenType: 'Bearer'
      expiresIn: number
      scope: string
      cached: boolean
    }
  | { status: 'error'; message: string }

const placeholderContent: Record<Exclude<PageId, 'home' | 'diagnostics' | 'signing' | 'validation' | 'certificates-list' | 'certificate-details' | 'certificate-chain'>, string> = {
}

function App() {
  const [page, setPage] = useState<PageId>('home')
  const [health, setHealth] = useState<HealthState>({ status: 'loading' })
  const [authenticationService, setAuthenticationService] =
    useState<'SIGN API' | 'VALIDATION API'>('SIGN API')
  const [signApiAuthentication, setSignApiAuthentication] =
    useState<SignApiAuthenticationState>({ status: 'idle' })
  const [validationApiAuthentication, setValidationApiAuthentication] =
    useState<ValidationApiAuthenticationState>({ status: 'idle' })

  const requestHealth = useCallback(async (signal?: AbortSignal) => {
    try {
      await getHealth(signal)
      return { status: 'success' } as const
    } catch (error) {
      if (signal?.aborted) {
        return null
      }

      return {
        status: 'error',
        message:
          error instanceof Error
            ? error.message
            : 'The backend health check could not be completed.',
      } as const
    }
  }, [])

  const checkHealth = useCallback(async () => {
    setHealth({ status: 'loading' })
    const result = await requestHealth()
    if (result) {
      setHealth(result)
    }
  }, [requestHealth])

  const testSignAuthentication = useCallback(async () => {
    setSignApiAuthentication({ status: 'loading' })

    try {
      const result = await testSignApiAuthentication()
      if (result.authenticated) {
        setSignApiAuthentication({
          status: 'success',
          tokenAcquired: result.tokenAcquired,
          tokenType: result.tokenType,
          expiresIn: result.expiresIn,
          cached: result.cached,
        })
      } else {
        setSignApiAuthentication({ status: 'error', message: result.error })
      }
    } catch (error) {
      setSignApiAuthentication({
        status: 'error',
        message:
          error instanceof Error
            ? error.message
            : 'SIGN API authentication could not be tested.',
      })
    }
  }, [])

  const testValidationAuthentication = useCallback(async () => {
    setValidationApiAuthentication({ status: 'loading' })

    try {
      const result = await testValidationApiAuthentication()
      if (result.authenticated) {
        setValidationApiAuthentication({
          status: 'success',
          tokenAcquired: result.tokenAcquired,
          tokenType: result.tokenType,
          expiresIn: result.expiresIn,
          scope: result.scope,
          cached: result.cached,
        })
      } else {
        setValidationApiAuthentication({ status: 'error', message: result.error })
      }
    } catch (error) {
      setValidationApiAuthentication({
        status: 'error',
        message:
          error instanceof Error
            ? error.message
            : 'VALIDATION API authentication could not be tested.',
      })
    }
  }, [])

  const testAuthentication = useCallback(async () => {
    if (authenticationService === 'SIGN API') {
      await testSignAuthentication()
      return
    }

    await testValidationAuthentication()
  }, [authenticationService, testSignAuthentication, testValidationAuthentication])

  useEffect(() => {
    const controller = new AbortController()
    void requestHealth(controller.signal).then((result) => {
      if (result) {
        setHealth(result)
      }
    })

    return () => controller.abort()
  }, [requestHealth])

  return (
    <div className="app-shell">
      <header className="app-header">
        <a className="brand" href="#home" onClick={() => setPage('home')}>
          TrustPlatform
        </a>
        <span className="environment-label">Application skeleton</span>
      </header>

      <div className="app-layout">
        <nav className="main-navigation" aria-label="Main navigation">
          {navigationItems.map((item) => (
            <button
              aria-current={page === item.id ? 'page' : undefined}
              className="navigation-item"
              key={item.id}
              onClick={() => setPage(item.id)}
              type="button"
            >
              {item.label}
            </button>
          ))}
        </nav>

        <main className="page-content">
          {page === 'home' && (
            <section aria-labelledby="home-heading">
              <p className="eyebrow">TrustPlatform</p>
              <h1 id="home-heading">Welcome</h1>
              <p className="lead">
                A secure workspace for document signing and validation.
              </p>
              <HealthCard health={health} onRetry={() => void checkHealth()} />
            </section>
          )}

          {page === 'signing' && <SigningPage />}
          {page === 'validation' && <ValidationPage />}
          {page === 'certificates-list' && <CertificatesListPage />}
          {page === 'certificate-details' && <CertificateDetailsPage />}
          {page === 'certificate-chain' && <CertificateChainPage />}

          {page !== 'home' && page !== 'diagnostics' && page !== 'signing' && page !== 'validation' && page !== 'certificates-list' && page !== 'certificate-details' && page !== 'certificate-chain' && (
            <section aria-labelledby={`${page}-heading`}>
              <p className="eyebrow">Workspace</p>
              <h1 id={`${page}-heading`}>
                {navigationItems.find((item) => item.id === page)?.label}
              </h1>
              <p className="placeholder-message">{placeholderContent[page]}</p>
            </section>
          )}

          {page === 'diagnostics' && (
            <section aria-labelledby="diagnostics-heading">
              <p className="eyebrow">System status</p>
              <h1 id="diagnostics-heading">Diagnostics</h1>
              <p className="lead">
                Check connectivity to the TrustPlatform backend and SIGN API identity provider.
              </p>
              <HealthCard health={health} onRetry={() => void checkHealth()} />
              <section aria-labelledby="sign-auth-heading" className="health-card">
                <div className="health-card-heading">
                  <div>
                    <p className="eyebrow">Provider connectivity</p>
                    <h2 id="sign-auth-heading">Authentication service</h2>
                  </div>
                </div>
                <div className="diagnostic-controls">
                  <label htmlFor="authentication-service">Authentication service</label>
                  <select
                    id="authentication-service"
                    onChange={(event) => {
                      setAuthenticationService(event.target.value as 'SIGN API' | 'VALIDATION API')
                    }}
                    value={authenticationService}
                  >
                    <option value="SIGN API">SIGN API</option>
                    <option value="VALIDATION API">VALIDATION API</option>
                  </select>
                </div>
                <p className="placeholder-message">
                  {authenticationService === 'SIGN API'
                    ? 'Test the backend connection to the configured SIGN API identity provider.'
                    : 'Test the backend connection to the configured VALIDATION API identity provider.'}
                </p>
                <button
                  className="secondary-button"
                  disabled={
                    (authenticationService === 'SIGN API' && signApiAuthentication.status === 'loading') ||
                    (authenticationService === 'VALIDATION API' && validationApiAuthentication.status === 'loading')
                  }
                  onClick={() => void testAuthentication()}
                  type="button"
                >
                  {(authenticationService === 'SIGN API' && signApiAuthentication.status === 'loading') ||
                  (authenticationService === 'VALIDATION API' && validationApiAuthentication.status === 'loading')
                    ? 'Authenticating...'
                    : 'Test authentication'}
                </button>
                {authenticationService === 'SIGN API' && signApiAuthentication.status === 'loading' && (
                  <p role="status">Authenticating...</p>
                )}
                {authenticationService === 'VALIDATION API' && validationApiAuthentication.status === 'loading' && (
                  <p role="status">Authenticating...</p>
                )}
                {authenticationService === 'SIGN API' && signApiAuthentication.status === 'success' && (
                  <dl
                    aria-label="SIGN API authentication result"
                    className="sign-auth-result sign-auth-success"
                    role="status"
                  >
                    <div>
                      <dt>Authentication:</dt>
                      <dd>SUCCESS</dd>
                    </div>
                    <div>
                      <dt>Token acquired:</dt>
                      <dd>{signApiAuthentication.tokenAcquired ? 'YES' : 'NO'}</dd>
                    </div>
                    <div>
                      <dt>Token type:</dt>
                      <dd>{signApiAuthentication.tokenType}</dd>
                    </div>
                    <div>
                      <dt>Expires in:</dt>
                      <dd>{signApiAuthentication.expiresIn} seconds</dd>
                    </div>
                    <div>
                      <dt>Cached:</dt>
                      <dd>{signApiAuthentication.cached ? 'YES' : 'NO'}</dd>
                    </div>
                  </dl>
                )}
                {authenticationService === 'VALIDATION API' && validationApiAuthentication.status === 'success' && (
                  <dl
                    aria-label="VALIDATION API authentication result"
                    className="sign-auth-result sign-auth-success"
                    role="status"
                  >
                    <div>
                      <dt>Authentication:</dt>
                      <dd>SUCCESS</dd>
                    </div>
                    <div>
                      <dt>Token acquired:</dt>
                      <dd>{validationApiAuthentication.tokenAcquired ? 'YES' : 'NO'}</dd>
                    </div>
                    <div>
                      <dt>Token type:</dt>
                      <dd>{validationApiAuthentication.tokenType}</dd>
                    </div>
                    <div>
                      <dt>Expires in:</dt>
                      <dd>{validationApiAuthentication.expiresIn} seconds</dd>
                    </div>
                    <div>
                      <dt>Scope:</dt>
                      <dd>{validationApiAuthentication.scope}</dd>
                    </div>
                    <div>
                      <dt>Cached:</dt>
                      <dd>{validationApiAuthentication.cached ? 'YES' : 'NO'}</dd>
                    </div>
                  </dl>
                )}
                {authenticationService === 'SIGN API' && signApiAuthentication.status === 'error' && (
                  <div
                    className="sign-auth-result sign-auth-error"
                    role="alert"
                  >
                    <p>Authentication: FAILED</p>
                    <p>{signApiAuthentication.message}</p>
                  </div>
                )}
                {authenticationService === 'VALIDATION API' && validationApiAuthentication.status === 'error' && (
                  <div
                    className="sign-auth-result sign-auth-error"
                    role="alert"
                  >
                    <p>Authentication: FAILED</p>
                    <p>{validationApiAuthentication.message}</p>
                  </div>
                )}
              </section>
            </section>
          )}
        </main>
      </div>
    </div>
  )
}

function HealthCard({
  health,
  onRetry,
}: {
  health: HealthState
  onRetry: () => void
}) {
  return (
    <section aria-labelledby="health-heading" className="health-card">
      <div className="health-card-heading">
        <div>
          <p className="eyebrow">Backend</p>
          <h2 id="health-heading">API health</h2>
        </div>
        {health.status === 'success' && (
          <span className="health-indicator" aria-label="Healthy">
            Healthy
          </span>
        )}
      </div>

      {health.status === 'loading' && (
        <p role="status">Checking backend health…</p>
      )}

      {health.status === 'success' && (
        <p role="status">The backend is available.</p>
      )}

      {health.status === 'error' && (
        <div className="health-error" role="alert">
          <p>{health.message}</p>
          <button className="secondary-button" onClick={onRetry} type="button">
            Retry health check
          </button>
        </div>
      )}
    </section>
  )
}

export default App
