import { describe, expect, it, vi } from 'vitest'
import { testSignApiAuthentication } from './signApiDiagnosticsClient'

describe('testSignApiAuthentication', () => {
  it('posts to the same-origin backend diagnostic and returns safe metadata', async () => {
    const fetchMock = vi.spyOn(globalThis, 'fetch').mockResolvedValue(
      Response.json({
        authenticated: true,
        tokenAcquired: true,
        tokenType: 'Bearer',
        expiresIn: 300,
        cached: true,
        error: null,
      }),
    )

    await expect(testSignApiAuthentication()).resolves.toEqual({
      tokenAcquired: true,
      authenticated: true,
      tokenType: 'Bearer',
      expiresIn: 300,
      cached: true,
    })

    expect(fetchMock).toHaveBeenCalledWith(
      '/api/diagnostics/sign-auth',
      expect.objectContaining({
        method: 'POST',
        credentials: 'same-origin',
      }),
    )
  })

  it('preserves a false cached value from the backend diagnostic', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(
      Response.json({
        authenticated: true,
        tokenAcquired: true,
        tokenType: 'Bearer',
        expiresIn: 300,
        cached: false,
      }),
    )

    await expect(testSignApiAuthentication()).resolves.toMatchObject({
      authenticated: true,
      tokenAcquired: true,
      cached: false,
    })
  })

  it('returns only the sanitized error provided by the backend', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(
      Response.json(
        {
          authenticated: false,
          tokenAcquired: false,
          tokenType: null,
          expiresIn: null,
          cached: false,
          error: 'SIGN API authentication failed.',
        },
        { status: 502 },
      ),
    )

    await expect(testSignApiAuthentication()).resolves.toEqual({
      authenticated: false,
      error: 'SIGN API authentication failed.',
    })
  })

  it('rejects malformed or token-bearing responses', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(
      Response.json({
        authenticated: true,
        tokenAcquired: true,
        tokenType: 'Bearer',
        expiresIn: 300,
        cached: true,
        access_token: 'must-not-be-consumed',
      }),
    )

    await expect(testSignApiAuthentication()).rejects.toThrow(
      'The TrustPlatform authentication diagnostic returned an invalid response.',
    )
  })
})
