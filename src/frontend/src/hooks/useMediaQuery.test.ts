import { renderHook, act } from '@testing-library/react'
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { useMediaQuery, useIsMobile } from './useMediaQuery'

describe('useMediaQuery & useIsMobile', () => {
  let listeners: ((e: { matches: boolean }) => void)[] = []
  let matchesValue = false

  beforeEach(() => {
    listeners = []
    matchesValue = false

    window.matchMedia = vi.fn().mockImplementation((query: string) => ({
      matches: matchesValue,
      media: query,
      onchange: null,
      addListener: (fn: (e: { matches: boolean }) => void) => listeners.push(fn),
      removeListener: vi.fn(),
      addEventListener: (_event: string, fn: (e: { matches: boolean }) => void) => listeners.push(fn),
      removeEventListener: vi.fn(),
      dispatchEvent: vi.fn(),
    }))
  })

  it('returns initial match value false', () => {
    const { result } = renderHook(() => useMediaQuery('(max-width: 767px)'))
    expect(result.current).toBe(false)
  })

  it('updates state when media query changes', () => {
    const { result } = renderHook(() => useMediaQuery('(max-width: 767px)'))
    expect(result.current).toBe(false)

    act(() => {
      listeners.forEach((listener) => listener({ matches: true }))
    })

    expect(result.current).toBe(true)
  })

  it('useIsMobile uses (max-width: 767px) by default', () => {
    renderHook(() => useIsMobile())
    expect(window.matchMedia).toHaveBeenCalledWith('(max-width: 767px)')
  })
})
