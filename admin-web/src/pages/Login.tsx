import { useState } from 'react'
import type { FormEvent } from 'react'
import { api, ApiError, setToken } from '../api.ts'

interface Props {
  onLoggedIn: () => void
}

// Giriş: e-posta + parola + kimlik doğrulayıcı uygulamadaki 6 haneli kod (docs/05)
export function Login({ onLoggedIn }: Props) {
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [code, setCode] = useState('')
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(false)

  async function submit(event: FormEvent) {
    event.preventDefault()
    setBusy(true)
    setError('')
    try {
      const result = await api.post<{ token: string }>('/api/auth/login', { email, password, code })
      setToken(result.token)
      onLoggedIn()
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Giriş yapılamadı.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <main className="login">
      <h1>QU¿ Yönetim</h1>
      <form onSubmit={submit}>
        <label>
          E-posta
          <input type="email" value={email} onChange={(e) => setEmail(e.target.value)} autoComplete="username" autoFocus required />
        </label>
        <label>
          Parola
          <input type="password" value={password} onChange={(e) => setPassword(e.target.value)} autoComplete="current-password" required />
        </label>
        <label>
          Doğrulama kodu (6 hane)
          <input
            value={code}
            onChange={(e) => setCode(e.target.value)}
            inputMode="numeric"
            autoComplete="one-time-code"
            maxLength={7}
            required
          />
        </label>
        {error && <p className="error" role="alert">{error}</p>}
        <button type="submit" disabled={busy}>{busy ? 'Giriş yapılıyor…' : 'Giriş yap'}</button>
      </form>
    </main>
  )
}
