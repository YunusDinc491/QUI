import { useEffect, useState } from 'react'

// Basit hash yönlendirme (#/questions?categoryId=5). Ek paket gerekmez.

export interface Route {
  segments: string[]
  params: URLSearchParams
}

export function parseHash(hash: string): Route {
  const raw = hash.replace(/^#\/?/, '')
  const [path, query = ''] = raw.split('?')
  return { segments: path.split('/').filter(Boolean), params: new URLSearchParams(query) }
}

export function useRoute(): Route {
  const [hash, setHash] = useState(window.location.hash)
  useEffect(() => {
    const onChange = () => setHash(window.location.hash)
    window.addEventListener('hashchange', onChange)
    return () => window.removeEventListener('hashchange', onChange)
  }, [])
  return parseHash(hash)
}

export function navigate(path: string): void {
  window.location.hash = path
}

// Kategori ve zorluk seçimi, art arda soru girerken hatırlansın diye saklanır (yalnızca tercih; gizli veri değil)
const PREFS_KEY = 'qui-admin-question-prefs'

export interface QuestionPrefs {
  categoryId: number | null
  difficulty: 'Easy' | 'Medium' | 'Hard'
}

export function loadPrefs(): QuestionPrefs {
  try {
    const parsed = JSON.parse(localStorage.getItem(PREFS_KEY) ?? 'null')
    if (parsed && typeof parsed === 'object') return { categoryId: parsed.categoryId ?? null, difficulty: parsed.difficulty ?? 'Easy' }
  } catch {
    // bozuk/erişilemeyen depolama: varsayılana dön
  }
  return { categoryId: null, difficulty: 'Easy' }
}

export function savePrefs(prefs: QuestionPrefs): void {
  try {
    localStorage.setItem(PREFS_KEY, JSON.stringify(prefs))
  } catch {
    // önemli değil
  }
}
