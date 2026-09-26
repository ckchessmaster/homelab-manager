import { useState, useEffect } from 'react'

/**
 * Custom hook to detect if a CSS media query matches.
 * Defaults to false if window is undefined or during initial render.
 */
export function useMediaQuery(query: string): boolean {
  const [matches, setMatches] = useState<boolean>(() => {
    if (typeof window !== 'undefined' && window.matchMedia) {
      return window.matchMedia(query).matches
    }
    return false
  })

  useEffect(() => {
    if (typeof window === 'undefined' || !window.matchMedia) {
      return
    }

    const mediaQueryList = window.matchMedia(query)
    setMatches(mediaQueryList.matches)

    const listener = (event: MediaQueryListEvent) => {
      setMatches(event.matches)
    }

    if (mediaQueryList.addEventListener) {
      mediaQueryList.addEventListener('change', listener)
    } else {
      // Compatibility for older browser environments
      mediaQueryList.addListener(listener)
    }

    return () => {
      if (mediaQueryList.removeEventListener) {
        mediaQueryList.removeEventListener('change', listener)
      } else {
        mediaQueryList.removeListener(listener)
      }
    }
  }, [query])

  return matches
}

/**
 * Convenience hook to detect mobile viewport (<768px).
 */
export function useIsMobile(maxPixelWidth = 768): boolean {
  return useMediaQuery(`(max-width: ${maxPixelWidth - 1}px)`)
}
