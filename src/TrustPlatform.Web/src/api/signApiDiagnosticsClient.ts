export type SignApiAuthenticationResult =
  | {
      authenticated: true
      tokenAcquired: boolean
      tokenType: 'Bearer'
      expiresIn: number
      cached: boolean
    }
  | { authenticated: false; error: string }

export async function testSignApiAuthentication(): Promise<SignApiAuthenticationResult> {
  let response: Response

  try {
    response = await fetch('/api/diagnostics/sign-auth', {
      method: 'POST',
      headers: { Accept: 'application/json' },
      credentials: 'same-origin',
      cache: 'no-store',
    })
  } catch {
    throw new Error('Unable to reach the TrustPlatform authentication diagnostic.')
  }

  let payload: unknown
  try {
    payload = await response.json()
  } catch {
    throw new Error('The TrustPlatform authentication diagnostic returned an invalid response.')
  }

  if (
    typeof payload !== 'object' ||
    payload === null ||
    !('authenticated' in payload) ||
    Object.keys(payload).some(
      (key) =>
        ![
          'authenticated',
          'tokenAcquired',
          'tokenType',
          'expiresIn',
          'cached',
          'error',
        ].includes(key),
    )
  ) {
    throw new Error('The TrustPlatform authentication diagnostic returned an invalid response.')
  }

  if (payload.authenticated === true) {
    if (!response.ok) {
      throw new Error('SIGN API authentication failed.')
    }

    if (
      !('tokenType' in payload) ||
      payload.tokenType !== 'Bearer' ||
      !('tokenAcquired' in payload) ||
      typeof payload.tokenAcquired !== 'boolean' ||
      !('expiresIn' in payload) ||
      typeof payload.expiresIn !== 'number' ||
      !Number.isSafeInteger(payload.expiresIn) ||
      payload.expiresIn <= 0 ||
      !('cached' in payload) ||
      typeof payload.cached !== 'boolean'
    ) {
      throw new Error('The TrustPlatform authentication diagnostic returned an invalid response.')
    }

    return {
      authenticated: true,
      tokenAcquired: payload.tokenAcquired,
      tokenType: 'Bearer',
      expiresIn: payload.expiresIn,
      cached: payload.cached,
    }
  }

  if (
    payload.authenticated === false &&
    'error' in payload &&
    (payload.error === 'SIGN API authentication is not configured.' ||
      payload.error === 'SIGN API authentication failed.')
  ) {
    return { authenticated: false, error: payload.error }
  }

  if (!response.ok) {
    throw new Error('SIGN API authentication failed.')
  }

  throw new Error('The TrustPlatform authentication diagnostic returned an invalid response.')
}
