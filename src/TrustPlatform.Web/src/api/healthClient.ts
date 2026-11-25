export interface HealthResponse {
  status: 'Healthy'
}

export async function getHealth(signal?: AbortSignal): Promise<HealthResponse> {
  let response: Response

  try {
    response = await fetch('/api/health', {
      method: 'GET',
      headers: { Accept: 'application/json' },
      credentials: 'same-origin',
      cache: 'no-store',
      signal,
    })
  } catch {
    if (signal?.aborted) {
      throw new Error('Health check was cancelled.')
    }

    throw new Error('Unable to connect to the TrustPlatform backend.')
  }

  if (!response.ok) {
    throw new Error('The TrustPlatform backend health check failed.')
  }

  let payload: unknown

  try {
    payload = await response.json()
  } catch {
    throw new Error('The TrustPlatform backend returned an invalid health response.')
  }

  if (
    typeof payload !== 'object' ||
    payload === null ||
    !('status' in payload) ||
    payload.status !== 'Healthy'
  ) {
    throw new Error('The TrustPlatform backend returned an invalid health response.')
  }

  return { status: payload.status }
}
