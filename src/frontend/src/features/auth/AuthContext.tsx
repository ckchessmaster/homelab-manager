import { createContext, useContext } from 'react'
import type { AuthState } from './AuthTypes'

export const AuthContext = createContext<AuthState | null>(null)

export function useAuthUser(): AuthState {
  const context = useContext(AuthContext)
  if (!context) {
    throw new Error('useAuthUser must be used within an AuthProvider')
  }
  return context
}
