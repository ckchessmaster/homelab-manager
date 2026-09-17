/**
 * Utility functions for detecting, encoding, and decoding connection strings (DSNs)
 * and Kubernetes Secret values.
 */

// Supported connection string URI schemes
const URI_SCHEME_REGEX = /^(postgresql|postgres|mysql|mariadb|mongodb|mongodb\+srv|redis|rediss|amqp|amqps|http|https):\/\//i

export interface ParsedUriCredentials {
  scheme: string
  user: string
  password?: string
  rest: string
}

/**
 * Checks if a string looks like a standard database or service connection string.
 */
export function isConnectionString(val: string): boolean {
  if (!val || typeof val !== 'string') return false
  return URI_SCHEME_REGEX.test(val.trim())
}

/**
 * Checks if a string contains percent-encoded sequences (e.g. %20, %40, %23).
 */
export function hasPercentEncoding(val: string): boolean {
  if (!val || typeof val !== 'string') return false
  return /%[0-9A-Fa-f]{2}/.test(val)
}

/**
 * Parses user:password credentials from a connection string.
 * Uses greedy match up to the last @ before the host to handle passwords containing '@'.
 */
export function parseUriCredentials(uri: string): ParsedUriCredentials | null {
  if (!isConnectionString(uri)) return null

  // Match: (scheme://)(username)(:password)?@(host...)
  // We match the scheme, username up to first colon, password up to last @ before host
  const match = uri.match(/^([a-zA-Z0-9+.-]+:\/\/)([^:]+)(?::(.*))?@([^/@]+(?::[0-9]+)?(?:\/.*)?)$/)
  if (!match) return null

  return {
    scheme: match[1],
    user: match[2],
    password: match[3],
    rest: match[4],
  }
}

/**
 * Safely URL-encodes only the password component in a connection string (DSN).
 * If the password is already partially or fully percent-encoded, it decodes first to prevent double-encoding.
 */
export function encodeUriPassword(uri: string): string {
  const parsed = parseUriCredentials(uri)
  if (!parsed || parsed.password === undefined) return uri

  // Decode first to handle already percent-encoded values without double-encoding
  let decoded = parsed.password
  try {
    decoded = decodeURIComponent(parsed.password)
  } catch {
    // Keep as is if malformed percent sequence
  }

  const encodedPassword = encodeURIComponent(decoded)
  return `${parsed.scheme}${parsed.user}:${encodedPassword}@${parsed.rest}`
}

/**
 * Decodes the password component in a connection string back to human-readable plaintext.
 */
export function decodeUriPassword(uri: string): string {
  const parsed = parseUriCredentials(uri)
  if (!parsed || parsed.password === undefined) return uri

  try {
    const decodedPassword = decodeURIComponent(parsed.password)
    return `${parsed.scheme}${parsed.user}:${decodedPassword}@${parsed.rest}`
  } catch {
    return uri
  }
}

/**
 * Checks if a connection string's password contains unencoded special characters
 * that would break standard URI parsing (e.g., @, :, /, ?, #, !, $, &, +, *).
 */
export function hasUnencodedUriPassword(uri: string): boolean {
  const parsed = parseUriCredentials(uri)
  if (!parsed || !parsed.password) return false

  // Check for reserved URI characters that are not part of a valid %XX sequence
  return /[@:/?#!$&+*]|%(?![0-9A-Fa-f]{2})/.test(parsed.password)
}

/**
 * Auto-encodes a secret value if it is a connection string with unencoded password characters.
 * Otherwise returns the raw value unchanged.
 */
export function autoEncodeSecretForSubmission(val: string): string {
  if (isConnectionString(val) && hasUnencodedUriPassword(val)) {
    return encodeUriPassword(val)
  }
  return val
}

/**
 * Auto-decodes a secret value for human display:
 * - If it's a connection string with encoded credentials, decodes the password.
 * - If it's another string containing percent encoding, safely decodes it.
 */
export function autoDecodeSecretForDisplay(val: string): string {
  if (!val || typeof val !== 'string') return ''

  if (isConnectionString(val)) {
    return decodeUriPassword(val)
  }

  if (hasPercentEncoding(val)) {
    try {
      return decodeURIComponent(val)
    } catch {
      return val
    }
  }

  return val
}
