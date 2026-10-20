import { useEffect, useRef, useState } from 'react'
import type { KeyboardEvent } from 'react'
import { api, ApiError, DIFFICULTY_LABEL, MODES } from '../api.ts'
import type { CategoryListItem, DifficultyName, Duplicate, Question, StatusName } from '../api.ts'
import { loadPrefs, navigate, savePrefs } from '../route.ts'

interface Props {
  /** Düzenleme modunda soru kimliği; yoksa yeni soru. */
  questionId?: number
}

const LETTERS = ['A', 'B', 'C', 'D']
const EMPTY_OPTIONS = ['', '', '', '']

// Akıcı soru girişi (docs/05 §2): kategori ve zorluk hatırlanır, Ctrl+Enter = "kaydet ve yeni ekle",
// Enter bir sonraki alana geçer, yazarken benzer soru uyarısı çıkar.
export function QuestionForm({ questionId }: Props) {
  const editing = questionId !== undefined
  const [prefs] = useState(loadPrefs)

  const [categories, setCategories] = useState<CategoryListItem[]>([])
  const [categoryId, setCategoryId] = useState<number | null>(prefs.categoryId)
  const [difficulty, setDifficulty] = useState<DifficultyName>(prefs.difficulty)
  const [text, setText] = useState('')
  const [options, setOptions] = useState<string[]>(EMPTY_OPTIONS)
  const [correctIndex, setCorrectIndex] = useState(0)
  const [explanation, setExplanation] = useState('')
  const [status, setStatus] = useState<StatusName>('Draft')

  const [error, setError] = useState('')
  const [info, setInfo] = useState('')
  // Benzer sorular, hangi metin/kategori için bulunduysa onunla birlikte tutulur; metin değişince eski uyarı kendiliğinden düşer
  const [dupState, setDupState] = useState<{ key: string; list: Duplicate[] }>({ key: '', list: [] })
  const dupKey = `${categoryId}|${text}`
  const duplicates = dupState.key === dupKey ? dupState.list : []
  const [saving, setSaving] = useState(false)

  const textRef = useRef<HTMLTextAreaElement>(null)
  const optionRefs = useRef<(HTMLInputElement | null)[]>([])
  const explanationRef = useRef<HTMLTextAreaElement>(null)

  // Kategoriler
  useEffect(() => {
    api.get<CategoryListItem[]>('/api/categories').then(setCategories).catch(() => setError('Kategoriler alınamadı.'))
  }, [])

  // Düzenleme: mevcut soruyu yükle
  useEffect(() => {
    if (questionId === undefined) return
    api.get<Question>(`/api/questions/${questionId}`).then((q) => {
      setCategoryId(q.categoryId)
      setDifficulty(q.difficulty)
      setText(q.text)
      setOptions(q.options)
      setCorrectIndex(q.correctIndex)
      setExplanation(q.explanation ?? '')
      setStatus(q.status)
    }).catch((e) => setError(e instanceof ApiError ? e.message : 'Soru alınamadı.'))
  }, [questionId])

  // Yazarken benzer soru uyarısı (kısa gecikmeyle)
  useEffect(() => {
    if (categoryId === null || text.trim().length < 8) return
    const key = `${categoryId}|${text}`
    const timer = window.setTimeout(() => {
      api.post<{ duplicates: Duplicate[] }>('/api/questions/check-duplicate', { categoryId, text, excludeId: questionId ?? null })
        .then((r) => setDupState({ key, list: r.duplicates }))
        .catch(() => undefined)
    }, 500)
    return () => window.clearTimeout(timer)
  }, [text, categoryId, questionId])

  async function save(andNew: boolean, ignoreDuplicates = false) {
    if (categoryId === null) {
      setError('Önce bir kategori seçin.')
      return
    }
    setSaving(true)
    setError('')
    setInfo('')
    const payload = { categoryId, text, options, correctIndex, difficulty, explanation, status, ignoreDuplicates }
    try {
      const saved = editing
        ? await api.put<Question>(`/api/questions/${questionId}`, payload)
        : await api.post<Question>('/api/questions', payload)
      savePrefs({ categoryId, difficulty })

      if (editing) {
        navigate('/questions')
      } else if (andNew) {
        setText('')
        setOptions(EMPTY_OPTIONS)
        setCorrectIndex(0)
        setExplanation('')
        setInfo(`Kaydedildi (#${saved.id}). Sıradaki soruyu girebilirsiniz.`)
        textRef.current?.focus()
      } else {
        navigate('/questions')
      }
    } catch (e) {
      if (e instanceof ApiError) {
        setError(e.message)
        if (e.code === 'DUPLICATE_SUSPECTED') setDupState({ key: dupKey, list: e.duplicates ?? [] })
      } else {
        setError('Kaydedilemedi.')
      }
    } finally {
      setSaving(false)
    }
  }

  function onKeyDown(event: KeyboardEvent) {
    if (event.key === 'Enter' && (event.ctrlKey || event.metaKey)) {
      event.preventDefault()
      void save(!editing)
    }
  }

  function setOption(index: number, value: string) {
    setOptions((current) => current.map((o, i) => (i === index ? value : o)))
  }

  // Enter: bir sonraki şıka / açıklamaya geç
  function optionEnter(index: number, event: KeyboardEvent) {
    if (event.key !== 'Enter' || event.ctrlKey || event.metaKey) return
    event.preventDefault()
    if (index < 3) optionRefs.current[index + 1]?.focus()
    else explanationRef.current?.focus()
  }

  const duplicateWarning = duplicates.length > 0

  return (
    <section onKeyDown={onKeyDown}>
      <h2>{editing ? `Soruyu düzenle (#${questionId})` : 'Yeni soru'}</h2>

      <div className="row">
        <label>
          Kategori
          <select value={categoryId ?? ''} onChange={(e) => setCategoryId(e.target.value ? Number(e.target.value) : null)}>
            <option value="">— seçin —</option>
            {MODES.map((mode) => (
              <optgroup key={mode.id} label={mode.name}>
                {categories.filter((c) => c.category.modeId === mode.id).map((c) => (
                  <option key={c.category.id} value={c.category.id}>
                    {c.category.name}{c.category.isPublished ? '' : ' (gizli)'}
                  </option>
                ))}
              </optgroup>
            ))}
          </select>
        </label>
        <fieldset className="radios">
          <legend>Zorluk</legend>
          {(['Easy', 'Medium', 'Hard'] as DifficultyName[]).map((d) => (
            <label key={d}>
              <input type="radio" name="difficulty" checked={difficulty === d} onChange={() => setDifficulty(d)} />
              {DIFFICULTY_LABEL[d]}
            </label>
          ))}
        </fieldset>
        <label>
          Durum
          <select value={status} onChange={(e) => setStatus(e.target.value as StatusName)}>
            <option value="Draft">Taslak</option>
            <option value="Published">Yayında</option>
            <option value="Archived">Arşiv</option>
          </select>
        </label>
      </div>

      <label>
        Soru metni <span className="muted">({text.trim().length}/500)</span>
        <textarea ref={textRef} value={text} onChange={(e) => setText(e.target.value)} rows={3} maxLength={500} autoFocus />
      </label>

      {duplicateWarning && (
        <div className="warning" role="alert">
          <strong>Benzer soru var:</strong>
          <ul>
            {duplicates.map((d) => (
              <li key={d.questionId}>
                <a href={`#/questions/${d.questionId}`} target="_blank" rel="noreferrer">#{d.questionId}</a> (%{Math.round(d.similarity * 100)}) — {d.text}
              </li>
            ))}
          </ul>
          <button type="button" onClick={() => save(!editing, true)} disabled={saving}>Yine de kaydet</button>
        </div>
      )}

      <fieldset className="options">
        <legend>Şıklar (doğru olanı işaretleyin)</legend>
        {LETTERS.map((letter, index) => (
          <label key={letter} className="option">
            <input type="radio" name="correct" checked={correctIndex === index} onChange={() => setCorrectIndex(index)} aria-label={`${letter} doğru şık`} />
            <span className="letter">{letter}</span>
            <input
              ref={(el) => { optionRefs.current[index] = el }}
              value={options[index]}
              onChange={(e) => setOption(index, e.target.value)}
              onKeyDown={(e) => optionEnter(index, e)}
              maxLength={150}
              placeholder={`${letter} şıkkı`}
            />
          </label>
        ))}
      </fieldset>

      <label>
        Açıklama <span className="muted">(isteğe bağlı)</span>
        <textarea ref={explanationRef} value={explanation} onChange={(e) => setExplanation(e.target.value)} rows={2} maxLength={1000} />
      </label>

      {error && !duplicateWarning && <p className="error" role="alert">{error}</p>}
      {info && <p className="success" role="status">{info}</p>}

      <div className="buttons">
        {!editing && <button type="button" onClick={() => save(true)} disabled={saving}>Kaydet ve yeni ekle (Ctrl+Enter)</button>}
        <button type="button" onClick={() => save(false)} disabled={saving}>{editing ? 'Kaydet (Ctrl+Enter)' : 'Kaydet'}</button>
        <button type="button" className="secondary" onClick={() => navigate('/questions')}>Vazgeç</button>
      </div>
    </section>
  )
}
