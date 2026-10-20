import { useEffect, useState } from 'react'

// Admin API adresi: .env içinde VITE_ADMIN_API_URL ile değiştirilir (bkz. .env.example)
const API_URL = import.meta.env.VITE_ADMIN_API_URL ?? 'http://localhost:5090'

type Status = 'checking' | 'ok' | 'error'

const STATUS_TEXT: Record<Status, string> = {
  checking: 'kontrol ediliyor...',
  ok: 'ÇALIŞIYOR',
  error: 'ULAŞILAMADI',
}

// Faz 0 iskeleti: ham ve işlevsel ekran (tasarım en sona bırakıldı, K-53)
function App() {
  const [status, setStatus] = useState<Status>('checking')

  useEffect(() => {
    fetch(`${API_URL}/health`)
      .then((response) => response.text())
      .then((text) => setStatus(text === 'Healthy' ? 'ok' : 'error'))
      .catch(() => setStatus('error'))
  }, [])

  return (
    <main>
      <h1>QU¿ Yönetim Paneli</h1>
      <p>Faz 0 iskeleti. Giriş, soru yönetimi ve diğer bölümler sonraki fazlarda eklenecek.</p>
      <p>
        Admin API ({API_URL}): <strong data-testid="api-status">{STATUS_TEXT[status]}</strong>
      </p>
    </main>
  )
}

export default App
