import { useEffect, useState } from 'react'
import { api, AUTH_LOST_EVENT, getToken, setToken } from './api.ts'
import { useRoute } from './route.ts'
import { Audit } from './pages/Audit.tsx'
import { Categories } from './pages/Categories.tsx'
import { Import } from './pages/Import.tsx'
import { Login } from './pages/Login.tsx'
import { QuestionForm } from './pages/QuestionForm.tsx'
import { Questions } from './pages/Questions.tsx'
import { Tournament } from './pages/Tournament.tsx'

// Yönetim paneli kabuğu: giriş kapısı + üst menü + sayfa seçimi.
// Tasarım en sona bırakıldı (K-53); ekranlar işlevsel ve sade.
function App() {
  const [loggedIn, setLoggedIn] = useState(() => getToken() !== null)
  const [email, setEmail] = useState('')
  const route = useRoute()

  useEffect(() => {
    const onLost = () => setLoggedIn(false)
    window.addEventListener(AUTH_LOST_EVENT, onLost)
    return () => window.removeEventListener(AUTH_LOST_EVENT, onLost)
  }, [])

  useEffect(() => {
    if (!loggedIn) return
    api.get<{ email: string }>('/api/auth/me').then((me) => setEmail(me.email)).catch(() => undefined)
  }, [loggedIn])

  if (!loggedIn) return <Login onLoggedIn={() => setLoggedIn(true)} />

  function logout() {
    setToken(null)
    setLoggedIn(false)
  }

  const [section, id] = route.segments
  const active = section ?? 'questions'

  let page
  if (active === 'categories') page = <Categories />
  else if (active === 'import') page = <Import />
  else if (active === 'audit') page = <Audit />
  else if (active === 'tournament') page = <Tournament />
  else if (id === 'new') page = <QuestionForm />
  else if (id && /^\d+$/.test(id)) page = <QuestionForm key={id} questionId={Number(id)} />
  else {
    const categoryId = route.params.get('categoryId')
    page = <Questions key={categoryId ?? 'all'} initialCategoryId={categoryId ? Number(categoryId) : undefined} />
  }

  return (
    <div className="shell">
      <header>
        <strong className="brand">QU¿ Yönetim</strong>
        <nav>
          <a href="#/questions" className={active === 'questions' && id !== 'new' ? 'active' : ''}>Sorular</a>
          <a href="#/questions/new" className={id === 'new' ? 'active' : ''}>Yeni soru</a>
          <a href="#/categories" className={active === 'categories' ? 'active' : ''}>Kategoriler</a>
          <a href="#/import" className={active === 'import' ? 'active' : ''}>Toplu yükleme</a>
          <a href="#/tournament" className={active === 'tournament' ? 'active' : ''}>Turnuva</a>
          <a href="#/audit" className={active === 'audit' ? 'active' : ''}>Denetim</a>
        </nav>
        <span className="who">{email}</span>
        <button type="button" className="secondary" onClick={logout}>Çıkış</button>
      </header>
      <main>{page}</main>
    </div>
  )
}

export default App
