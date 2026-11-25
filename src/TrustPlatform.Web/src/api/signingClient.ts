export type SigningOptions = {
  maxFileSizeBytes: number
  allowedExtensions: string[]
  allowedContentTypes: string[]
  packagingValues: string[]
  signatureLevelValues: string[]
}

export type AutomaticCadesDevelopmentDefaults = {
  certificateId: string
  xSignerId: string
  pin: string
  infocertSat: string
  packaging: string
  signatureLevel: string
}

export type AutomaticPadesDevelopmentDefaults = {
  certificateId: string
  xSignerId: string
  pin: string
  infocertSat: string
  packaging: string
  signatureLevel: string
  isVisible: boolean
  isPdfa: boolean
  page: number
  llx: number
  lly: number
  urx: number
  ury: number
}

export type AutomaticCadesSigningResult = {
  blob: Blob
  requestId: string | null
  fileName: string
}

function isStringArray(value: unknown): value is string[] {
  return Array.isArray(value) && value.every((item) => typeof item === 'string')
}

function isString(value: unknown): value is string {
  return typeof value === 'string'
}

async function safeErrorMessage(response: Response): Promise<string> {
  try {
    const body: unknown = await response.json()
    if (
      typeof body === 'object' &&
      body !== null &&
      'error' in body &&
      typeof body.error === 'string'
    ) {
      return body.error
    }
  } catch {
    // Use the stable fallback below for non-JSON or malformed errors.
  }

  return 'The signing request could not be completed.'
}

export async function getSigningOptions(
  signal?: AbortSignal,
): Promise<SigningOptions> {
  let response: Response
  try {
    response = await fetch('/api/signing/options', {
      credentials: 'same-origin',
      signal,
    })
  } catch {
    throw new Error('Signing options are unavailable.')
  }

  if (!response.ok) {
    throw new Error(await safeErrorMessage(response))
  }

  let options: unknown
  try {
    options = await response.json()
  } catch {
    throw new Error('Signing options are unavailable.')
  }

  if (
    typeof options !== 'object' ||
    options === null ||
    !('maxFileSizeBytes' in options) ||
    typeof options.maxFileSizeBytes !== 'number' ||
    !Number.isSafeInteger(options.maxFileSizeBytes) ||
    options.maxFileSizeBytes <= 0 ||
    !('allowedExtensions' in options) ||
    !isStringArray(options.allowedExtensions) ||
    !('allowedContentTypes' in options) ||
    !isStringArray(options.allowedContentTypes) ||
    !('packagingValues' in options) ||
    !isStringArray(options.packagingValues) ||
    !('signatureLevelValues' in options) ||
    !isStringArray(options.signatureLevelValues)
  ) {
    throw new Error('Signing options are unavailable.')
  }

  return {
    maxFileSizeBytes: options.maxFileSizeBytes,
    allowedExtensions: options.allowedExtensions,
    allowedContentTypes: options.allowedContentTypes,
    packagingValues: options.packagingValues,
    signatureLevelValues: options.signatureLevelValues,
  }
}

export async function getAutomaticCadesDevelopmentDefaults(
  signal?: AbortSignal,
): Promise<AutomaticCadesDevelopmentDefaults> {
  let response: Response
  try {
    response = await fetch('/api/signing/automatic/cades/defaults', {
      cache: 'no-store',
      credentials: 'same-origin',
      signal,
    })
  } catch {
    throw new Error('Development signing defaults are unavailable.')
  }

  if (!response.ok) {
    throw new Error('Development signing defaults are unavailable.')
  }

  let defaults: unknown
  try {
    defaults = await response.json()
  } catch {
    throw new Error('Development signing defaults are unavailable.')
  }

  if (
    typeof defaults !== 'object' ||
    defaults === null ||
    !('certificateId' in defaults) ||
    !isString(defaults.certificateId) ||
    !('xSignerId' in defaults) ||
    !isString(defaults.xSignerId) ||
    !('pin' in defaults) ||
    !isString(defaults.pin) ||
    !('infocertSat' in defaults) ||
    !isString(defaults.infocertSat) ||
    !('packaging' in defaults) ||
    !isString(defaults.packaging) ||
    !('signatureLevel' in defaults) ||
    !isString(defaults.signatureLevel)
  ) {
    throw new Error('Development signing defaults are unavailable.')
  }

  return {
    certificateId: defaults.certificateId,
    xSignerId: defaults.xSignerId,
    pin: defaults.pin,
    infocertSat: defaults.infocertSat,
    packaging: defaults.packaging,
    signatureLevel: defaults.signatureLevel,
  }
}

export async function getAutomaticPadesDevelopmentDefaults(
  signal?: AbortSignal,
): Promise<AutomaticPadesDevelopmentDefaults> {
  let response: Response
  try {
    response = await fetch('/api/signing/automatic/pades/defaults', {
      cache: 'no-store',
      credentials: 'same-origin',
      signal,
    })
  } catch {
    throw new Error('Development PAdES signing defaults are unavailable.')
  }

  if (!response.ok) {
    throw new Error('Development PAdES signing defaults are unavailable.')
  }

  let defaults: unknown
  try {
    defaults = await response.json()
  } catch {
    throw new Error('Development PAdES signing defaults are unavailable.')
  }

  if (
    typeof defaults !== 'object' ||
    defaults === null ||
    !('certificateId' in defaults) || !isString(defaults.certificateId) ||
    !('xSignerId' in defaults) || !isString(defaults.xSignerId) ||
    !('pin' in defaults) || !isString(defaults.pin) ||
    !('infocertSat' in defaults) || !isString(defaults.infocertSat) ||
    !('packaging' in defaults) || !isString(defaults.packaging) ||
    !('signatureLevel' in defaults) || !isString(defaults.signatureLevel) ||
    !('isVisible' in defaults) || typeof defaults.isVisible !== 'boolean' ||
    !('isPdfa' in defaults) || typeof defaults.isPdfa !== 'boolean' ||
    !('page' in defaults) || typeof defaults.page !== 'number' ||
    !('llx' in defaults) || typeof defaults.llx !== 'number' ||
    !('lly' in defaults) || typeof defaults.lly !== 'number' ||
    !('urx' in defaults) || typeof defaults.urx !== 'number' ||
    !('ury' in defaults) || typeof defaults.ury !== 'number'
  ) {
    throw new Error('Development PAdES signing defaults are unavailable.')
  }

  return {
    certificateId: defaults.certificateId,
    xSignerId: defaults.xSignerId,
    pin: defaults.pin,
    infocertSat: defaults.infocertSat,
    packaging: defaults.packaging,
    signatureLevel: defaults.signatureLevel,
    isVisible: defaults.isVisible,
    isPdfa: defaults.isPdfa,
    page: defaults.page,
    llx: defaults.llx,
    lly: defaults.lly,
    urx: defaults.urx,
    ury: defaults.ury,
  }
}

export async function signAutomaticCades(
  formData: FormData,
): Promise<AutomaticCadesSigningResult> {
  let response: Response
  try {
    response = await fetch('/api/signing/automatic/cades', {
      method: 'POST',
      body: formData,
      credentials: 'same-origin',
    })
  } catch {
    throw new Error('The signing service could not be reached.')
  }

  if (!response.ok) {
    throw new Error(await safeErrorMessage(response))
  }

  return {
    blob: await response.blob(),
    requestId: response.headers.get('X-TrustPlatform-Request-Id'),
    fileName: getDownloadFileName(response.headers.get('Content-Disposition')),
  }
}

export async function signAutomaticPades(
  formData: FormData,
): Promise<AutomaticCadesSigningResult> {
  let response: Response
  try {
    response = await fetch('/api/signing/automatic/pades', {
      method: 'POST',
      body: formData,
      credentials: 'same-origin',
    })
  } catch {
    throw new Error('The signing service could not be reached.')
  }

  if (!response.ok) {
    throw new Error(await safeErrorMessage(response))
  }

  return {
    blob: await response.blob(),
    requestId: response.headers.get('X-TrustPlatform-Request-Id'),
    fileName: getDownloadFileName(response.headers.get('Content-Disposition')),
  }
}

function getDownloadFileName(contentDisposition: string | null): string {
  const match = contentDisposition?.match(/filename="?([^";]+)"?/i)
  const fileName = match?.[1]
  return fileName && /^[a-zA-Z0-9._-]+$/.test(fileName)
    ? fileName
    : 'signed-document.p7m'
}
