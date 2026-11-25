import { describe, expect, it, vi } from 'vitest'
import { getHealth } from './healthClient'

describe('getHealth', () => {
  it('requests only the same-origin backend health endpoint', async () => {
    const fetchMock = vi
      .spyOn(globalThis, 'fetch')
      .mockResolvedValue(Response.json({ status: 'Healthy' }))

    await expect(getHealth()).resolves.toEqual({ status: 'Healthy' })

    expect(fetchMock).toHaveBeenCalledWith(
      '/api/health',
      expect.objectContaining({
        credentials: 'same-origin',
        method: 'GET',
      }),
    )
  })

  it('does not expose response details when the backend request fails', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(
      new Response('sensitive backend details', { status: 500 }),
    )

    await expect(getHealth()).rejects.toThrow(
      'The TrustPlatform backend health check failed.',
    )
  })
})
