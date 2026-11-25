export type ValidationMessage = {
  level?: string | null
  section?: string | null
  key?: string | null
  value?: string | null
}

export type SignedAttributes = {
  contentType?: string | null
  claimedSigningTime?: string | null
  messageDigest?: string | null
  signatureAlgorithm?: string | null
}

export type QcType = {
  description?: string | null
  oid?: string | null
}

export type CertificateResult = {
  certificateId?: string | null
  subjectDN?: string | null
  issuerDN?: string | null
  serialNumber?: string | null
  notBefore?: string | number | null
  notAfter?: string | number | null
  qcTypes?: QcType[] | null
}

export type SignatureResult = {
  signatureId?: string | null
  signatureLevel?: string | null
  signatureFormat?: string | null
  signatureQualification?: string | null
  signingTime?: string | number | null
  signerName?: string | null
  status?: string | null
  messages?: ValidationMessage[] | null
  signingCertificateId?: string | null
  certificateChain?: string[] | null
  timestampIds?: string[] | null
  signedAttributes?: SignedAttributes | null
}

export type TimestampResult = {
  id?: string | null
  type?: string | null
  dateTime?: string | number | null
  value?: string | null
}

export type ValidationReport = {
  signatureValidationPolicyName?: string | null
  signatureValidationPolicyDescription?: string | null
  signatureValidationPolicyOid?: string | null
  qsvsPolicyOid?: string | null
  validationTime?: string | number | null
  documentFileName?: string | null
  signaturesCount?: number | null
  validSignaturesCount?: number | null
  status?: string | null
  statusDetails?: string | null
  signatures?: SignatureResult[] | null
  timestamps?: TimestampResult[] | null
  certificates?: CertificateResult[] | null
  messages?: ValidationMessage[] | null
}

export type ValidationResponse = {
  valid: boolean
  validationReport?: ValidationReport | null
}

export type ValidationApiError = {
  title: string
  status: number
  correlationId?: string
  providerStatus?: number
  providerReasonPhrase?: string
  providerContentType?: string
  providerCode?: string
  message?: string
  detail?: string
  responseShape?: string
  parsingError?: string
  responseExcerpt?: string
}

export class ValidationRequestError extends Error {
  readonly validationError: ValidationApiError

  constructor(validationError: ValidationApiError) {
    super(validationError.message ?? validationError.title)
    this.validationError = validationError
    this.name = 'ValidationRequestError'
  }
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null
}

function stringProperty(value: Record<string, unknown>, key: string): string | undefined {
  return typeof value[key] === 'string' ? value[key] : undefined
}

function numberProperty(value: Record<string, unknown>, key: string): number | undefined {
  return typeof value[key] === 'number' && Number.isSafeInteger(value[key])
    ? value[key]
    : undefined
}

async function safeError(response: Response): Promise<ValidationApiError> {
  let payload: unknown
  try {
    payload = await response.json()
  } catch {
    return {
      title: 'Validation request failed',
      status: response.status,
      message: 'The validation request could not be completed.',
    }
  }

  if (!isRecord(payload)) {
    return {
      title: 'Validation request failed',
      status: response.status,
      message: 'The validation request could not be completed.',
    }
  }

  if (typeof payload.title === 'string' && numberProperty(payload, 'status') !== undefined) {
    return {
      title: payload.title,
      status: numberProperty(payload, 'status') ?? response.status,
      correlationId: stringProperty(payload, 'correlationId'),
      providerStatus: numberProperty(payload, 'providerStatus'),
      providerReasonPhrase: stringProperty(payload, 'providerReasonPhrase'),
      providerContentType: stringProperty(payload, 'providerContentType'),
      providerCode: stringProperty(payload, 'providerCode'),
      message: stringProperty(payload, 'message'),
      detail: stringProperty(payload, 'detail'),
      responseShape: stringProperty(payload, 'responseShape'),
      parsingError: stringProperty(payload, 'parsingError'),
      responseExcerpt: stringProperty(payload, 'responseExcerpt'),
    }
  }

  return {
    title: 'Validation request failed',
    status: response.status,
    message:
      typeof payload.error === 'string'
        ? payload.error
        : 'The validation request could not be completed.',
  }
}

export async function validateDocument(formData: FormData): Promise<ValidationResponse> {
  let response: Response
  try {
    response = await fetch('/api/validation/signature', {
      method: 'POST',
      body: formData,
      credentials: 'same-origin',
    })
  } catch {
    throw new ValidationRequestError({
      title: 'Validation request failed',
      status: 0,
      message: 'The validation service could not be reached.',
    })
  }

  if (!response.ok) {
    throw new ValidationRequestError(await safeError(response))
  }

  let payload: unknown
  try {
    payload = await response.json()
  } catch {
    throw new ValidationRequestError({
      title: 'Validation response failed',
      status: response.status,
      message: 'The validation service returned an invalid response.',
    })
  }

  if (
    !isRecord(payload) ||
    typeof payload.valid !== 'boolean' ||
    (payload.validationReport !== undefined &&
      payload.validationReport !== null &&
      !isRecord(payload.validationReport))
  ) {
    throw new ValidationRequestError({
      title: 'Validation response failed',
      status: response.status,
      message: 'The validation service returned an invalid response.',
    })
  }

  return payload as ValidationResponse
}
