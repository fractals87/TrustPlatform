export type CertificateSummary = {
  ids?: string[] | null
  aliasId?: string | null
  keyId?: string | null
  dnQualifierId?: string | null
  subject?: string | null
  issuer?: string | null
  status?: string | null
  expirationDate?: string | null
  isExpired?: boolean | null
}

export type CertificateListResponse = {
  certificates: CertificateSummary[]
}

export type CertificateDetailResponse = {
  certificateId: string
  ids: string[]
  aliasId?: string | null
  keyId?: string | null
  dnQualifierId?: string | null
  subject?: string | null
  issuer?: string | null
  status?: string | null
  expirationDate?: string | null
  isExpired?: boolean | null
  certificateAvailable: boolean
}

export type CertificateQueryDefaults = {
  certificateId: string
  xSignerId: string
}

export type CertificateStatus =
  | 'active'
  | 'expired'
  | 'revoked'
  | 'suspended'
  | 'unknown'

export type CertificateChainItem = {
  certificate: string
  ids: string[]
  subject: string | null
  issuer: string | null
  status: CertificateStatus | string
  expirationDate: string | null
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null
}

async function parseJsonResponse(response: Response): Promise<Record<string, unknown>> {
  let payload: unknown

  try {
    payload = await response.json()
  } catch {
    throw new Error('The TrustPlatform certificate service returned an invalid response.')
  }

  if (!isRecord(payload)) {
    throw new Error('The TrustPlatform certificate service returned an invalid response.')
  }

  return payload
}

function apiErrorMessage(payload: Record<string, unknown>, fallback: string): string {
  const title = typeof payload.title === 'string' ? payload.title : fallback
  const message = typeof payload.message === 'string' ? payload.message : null
  return message && message !== title ? `${title}: ${message}` : title
}

export async function getCertificateQueryDefaults(
  signal?: AbortSignal,
): Promise<CertificateQueryDefaults> {
  let response: Response

  try {
    response = await fetch('/api/certificates/defaults', {
      method: 'GET',
      headers: { Accept: 'application/json' },
      credentials: 'same-origin',
      cache: 'no-store',
      signal,
    })
  } catch {
    throw new Error('Unable to reach the TrustPlatform certificate settings service.')
  }

  if (!response.ok) {
    const payload = await parseJsonResponse(response)
    throw new Error(apiErrorMessage(payload, 'Certificate query defaults are unavailable.'))
  }

  const payload = await parseJsonResponse(response)
  if (
    typeof payload.certificateId !== 'string' ||
    typeof payload.xSignerId !== 'string'
  ) {
    throw new Error('The TrustPlatform certificate defaults response was invalid.')
  }

  return {
    certificateId: payload.certificateId,
    xSignerId: payload.xSignerId,
  }
}

export async function getCertificates(xSignerId: string): Promise<CertificateListResponse> {
  let response: Response

  try {
    response = await fetch('/api/certificates', {
      method: 'POST',
      headers: {
        Accept: 'application/json',
        'Content-Type': 'application/json',
      },
      body: JSON.stringify({ xSignerId }),
      credentials: 'same-origin',
      cache: 'no-store',
    })
  } catch {
    throw new Error('Unable to reach the TrustPlatform certificate list service.')
  }

  if (!response.ok) {
    const payload = await parseJsonResponse(response)
    throw new Error(apiErrorMessage(payload, 'The certificate list request failed.'))
  }

  const payload = await parseJsonResponse(response)
  if (!('certificates' in payload) || !Array.isArray(payload.certificates)) {
    throw new Error('The TrustPlatform certificate list response was invalid.')
  }

  return {
    certificates: payload.certificates.map((certificate) => {
      if (!isRecord(certificate)) {
        throw new Error('The TrustPlatform certificate list response was invalid.')
      }

      return {
        ids: Array.isArray(certificate.ids) ? certificate.ids.filter((value): value is string => typeof value === 'string') : [],
        aliasId: typeof certificate.aliasId === 'string' ? certificate.aliasId : null,
        keyId: typeof certificate.keyId === 'string' ? certificate.keyId : null,
        dnQualifierId: typeof certificate.dnQualifierId === 'string' ? certificate.dnQualifierId : null,
        subject: typeof certificate.subject === 'string' ? certificate.subject : null,
        issuer: typeof certificate.issuer === 'string' ? certificate.issuer : null,
        status: typeof certificate.status === 'string' ? certificate.status : null,
        expirationDate: typeof certificate.expirationDate === 'string' ? certificate.expirationDate : null,
        isExpired: typeof certificate.isExpired === 'boolean' ? certificate.isExpired : null,
      }
    }),
  }
}

export async function getConfiguredCertificate(
  certificateId: string,
  xSignerId: string,
): Promise<CertificateDetailResponse> {
  let response: Response

  try {
    response = await fetch('/api/certificates/configured', {
      method: 'POST',
      headers: {
        Accept: 'application/json',
        'Content-Type': 'application/json',
      },
      body: JSON.stringify({ certificateId, xSignerId }),
      credentials: 'same-origin',
      cache: 'no-store',
    })
  } catch {
    throw new Error('Unable to reach the TrustPlatform configured-certificate service.')
  }

  if (!response.ok) {
    const payload = await parseJsonResponse(response)
    throw new Error(apiErrorMessage(payload, 'The configured certificate request failed.'))
  }

  const payload = await parseJsonResponse(response)
  if (!isRecord(payload) || typeof payload.certificateId !== 'string') {
    throw new Error('The TrustPlatform configured-certificate response was invalid.')
  }

  return {
    certificateId: payload.certificateId,
    ids: Array.isArray(payload.ids) ? payload.ids.filter((value): value is string => typeof value === 'string') : [],
    aliasId: typeof payload.aliasId === 'string' ? payload.aliasId : null,
    keyId: typeof payload.keyId === 'string' ? payload.keyId : null,
    dnQualifierId: typeof payload.dnQualifierId === 'string' ? payload.dnQualifierId : null,
    subject: typeof payload.subject === 'string' ? payload.subject : null,
    issuer: typeof payload.issuer === 'string' ? payload.issuer : null,
    status: typeof payload.status === 'string' ? payload.status : null,
    expirationDate: typeof payload.expirationDate === 'string' ? payload.expirationDate : null,
    isExpired: typeof payload.isExpired === 'boolean' ? payload.isExpired : null,
    certificateAvailable: typeof payload.certificateAvailable === 'boolean' ? payload.certificateAvailable : false,
  }
}

export async function getCertificateChain(
  certificateId: string,
  xSignerId: string,
  signal?: AbortSignal,
): Promise<CertificateChainItem[]> {
  let response: Response
  const encodedCertificateId = encodeURIComponent(certificateId.trim())

  try {
    response = await fetch(`/api/certificates/${encodedCertificateId}/chain`, {
      method: 'POST',
      headers: {
        Accept: 'application/json',
        'Content-Type': 'application/json',
      },
      body: JSON.stringify({ xSignerId: xSignerId.trim() }),
      credentials: 'same-origin',
      cache: 'no-store',
      signal,
    })
  } catch {
    throw new Error('Unable to reach the TrustPlatform certificate chain service.')
  }

  if (!response.ok) {
    const payload = await parseJsonResponse(response)
    throw new Error(apiErrorMessage(payload, 'The certificate chain request failed.'))
  }

  let payload: unknown
  try {
    payload = await response.json()
  } catch {
    throw new Error('The TrustPlatform certificate chain response was invalid.')
  }
  if (!Array.isArray(payload)) {
    throw new Error('The TrustPlatform certificate chain response was invalid.')
  }

  return payload.map((item) => {
    if (!isRecord(item)) {
      throw new Error('The TrustPlatform certificate chain response was invalid.')
    }

    return {
      certificate: typeof item.certificate === 'string' ? item.certificate : '',
      ids: Array.isArray(item.ids)
        ? item.ids.filter((value): value is string => typeof value === 'string')
        : [],
      subject: typeof item.subject === 'string' ? item.subject : null,
      issuer: typeof item.issuer === 'string' ? item.issuer : null,
      status: typeof item.status === 'string' ? item.status : 'unknown',
      expirationDate: typeof item.expirationDate === 'string' ? item.expirationDate : null,
    }
  })
}
