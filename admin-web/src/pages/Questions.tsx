import { useEffect, useState } from 'react'
import { api, ApiError, DIFFICULTY_LABEL, MODES, STATUS_LABEL } from '../api.ts'
import type { CategoryListItem, DifficultyName, QuestionPage, StatusName } from '../api.ts'
import { navigate } from '../route.ts'

interface Props {
  initialCategoryId?: number
}

const PAGE_SIZE = 25

// Liste / filtre / arama / durum değiştirme (docs/05 §2). Silme yok; arşive alınır.
export function Questions({ initialCategoryId }: Props) {
  const [categories, setCategories] = useState<CategoryListItem[]>([])
  const [categoryId, setCategoryId] = useState<number | ''>(initialCategoryId ?? '')
  const [difficulty, setDifficulty] = useState<DifficultyName | ''>('')
  const [status, setStatus] = useState<StatusName | ''>('')
  const [search, setSearch] = useState('')
  const [appliedSearch, setAppliedSearch] = useState('')
  const [page, setPage] = useState(1)
  const [data, setData] = useState<QuestionPage | null>(null)
  const [error, setError] = useState('')

  useEffect(() => {
    api.get<CategoryListItem[]>('/api/categories').then(setCategories).catch(() => undefined)
  }, [])

  // Liste değişince (filtre, sayfa, durum değişimi) yeniden okunur; reload sayacı elle yenileme içindir
  const [reload, setReload] = useState(0)
  useEffect(() => {
    const params = new URLSearchParams({ page: String(page), pageSize: String(PAGE_SIZE) })
    if (categoryId !== '') params.set('categoryId', String(categoryId))
    if (difficulty) params.set('difficulty', difficulty)
    if (status) params.set('status', status)
    if (appliedSearch) params.set('search', appliedSearch)
    api.get<QuestionPage>(`/api/questions?${params}`)
      .then((d) => { setData(d); setError('') })
      .catch((e) => setError(e instanceof ApiError ? e.message : 'Liste alınamadı.'))
  }, [page, categoryId, difficulty, status, appliedSearch, reload])

  const categoryName = (id: number) => categories.find((c) => c.category.id === id)?.category.name ?? `#${id}`
  const pageCount = data ? Math.max(1, Math.ceil(data.total / data.pageSize)) : 1

  async function changeStatus(id: number, next: StatusName) {
    try {
      await api.post(`/api/questions/${id}/status`, { status: next })
      setReload((n) => n + 1)
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Durum değiştirilemedi.')
    }
  }

  function resetPageThen<T>(setter: (value: T) => void) {
    return (value: T) => {
      setPage(1)
      setter(value)
    }
  }

  return (
    <section>
      <div className="title-row">
        <h2>Sorular {data && <span className="muted">({data.total})</span>}</h2>
        <button type="button" onClick={() => navigate('/questions/new')}>+ Yeni soru</button>
      </div>

      <form
        className="inline-form"
        onSubmit={(e) => {
          e.preventDefault()
          setPage(1)
          setAppliedSearch(search)
        }}
      >
        <select value={categoryId} onChange={(e) => resetPageThen(setCategoryId)(e.target.value ? Number(e.target.value) : '')} aria-label="Kategori">
          <option value="">Tüm kategoriler</option>
          {MODES.map((mode) => (
            <optgroup key={mode.id} label={mode.name}>
              {categories.filter((c) => c.category.modeId === mode.id).map((c) => (
                <option key={c.category.id} value={c.category.id}>{c.category.name}</option>
              ))}
            </optgroup>
          ))}
        </select>
        <select value={difficulty} onChange={(e) => resetPageThen(setDifficulty)(e.target.value as DifficultyName | '')} aria-label="Zorluk">
          <option value="">Tüm zorluklar</option>
          {(Object.keys(DIFFICULTY_LABEL) as DifficultyName[]).map((d) => <option key={d} value={d}>{DIFFICULTY_LABEL[d]}</option>)}
        </select>
        <select value={status} onChange={(e) => resetPageThen(setStatus)(e.target.value as StatusName | '')} aria-label="Durum">
          <option value="">Tüm durumlar</option>
          {(Object.keys(STATUS_LABEL) as StatusName[]).map((s) => <option key={s} value={s}>{STATUS_LABEL[s]}</option>)}
        </select>
        <input value={search} onChange={(e) => setSearch(e.target.value)} placeholder="Soru metninde ara" />
        <button type="submit">Ara</button>
      </form>

      {error && <p className="error" role="alert">{error}</p>}

      <table className="list">
        <thead>
          <tr><th>#</th><th>Kategori</th><th>Soru</th><th>Zorluk</th><th>Durum</th><th title="Doğru oranı (gösterilme)">Doğru oranı</th><th>Bildirim</th><th></th></tr>
        </thead>
        <tbody>
          {data?.items.map((q) => {
            const rate = q.shownCount > 0 ? Math.round((q.correctCount / q.shownCount) * 100) : null
            return (
              <tr key={q.id}>
                <td>{q.id}</td>
                <td>{categoryName(q.categoryId)}</td>
                <td className="text">{q.text}</td>
                <td>{DIFFICULTY_LABEL[q.difficulty]}</td>
                <td><span className={`badge ${q.status === 'Published' ? 'ok' : ''}`}>{STATUS_LABEL[q.status]}</span></td>
                <td>{rate === null ? '—' : `%${rate} (${q.shownCount})`}</td>
                <td>{q.reportCount > 0 ? <span className="badge warn">{q.reportCount}</span> : '—'}</td>
                <td className="actions">
                  <a href={`#/questions/${q.id}`}>Düzenle</a>
                  {q.status !== 'Published' && <button type="button" onClick={() => changeStatus(q.id, 'Published')}>Yayınla</button>}
                  {q.status === 'Published' && <button type="button" onClick={() => changeStatus(q.id, 'Draft')}>Taslağa al</button>}
                  {q.status !== 'Archived' && <button type="button" onClick={() => changeStatus(q.id, 'Archived')}>Arşivle</button>}
                </td>
              </tr>
            )
          })}
          {data && data.items.length === 0 && <tr><td colSpan={8} className="muted">Bu filtreyle soru yok.</td></tr>}
        </tbody>
      </table>

      <div className="pager">
        <button type="button" disabled={page <= 1} onClick={() => setPage(page - 1)}>‹ Önceki</button>
        <span>Sayfa {page} / {pageCount}</span>
        <button type="button" disabled={page >= pageCount} onClick={() => setPage(page + 1)}>Sonraki ›</button>
      </div>
    </section>
  )
}
