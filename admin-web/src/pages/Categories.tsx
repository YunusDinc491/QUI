import { useEffect, useState } from 'react'
import type { FormEvent } from 'react'
import { api, ApiError, MODES, uploadImage } from '../api.ts'
import type { CategoryListItem } from '../api.ts'

// Hedef öneri (docs/05): kategori başına 40 kolay / 30 orta / 30 zor. Engel değil, yalnızca bilgi.
const TARGET = { easy: 40, medium: 30, hard: 30 }

export function Categories() {
  const [items, setItems] = useState<CategoryListItem[]>([])
  const [error, setError] = useState('')
  const [editingId, setEditingId] = useState<number | null>(null)

  const [reload, setReload] = useState(0)
  const load = () => setReload((n) => n + 1)

  useEffect(() => {
    api.get<CategoryListItem[]>('/api/categories')
      .then((list) => { setItems(list); setError('') })
      .catch((e) => setError(e instanceof ApiError ? e.message : 'Liste alınamadı.'))
  }, [reload])

  async function togglePublish(item: CategoryListItem) {
    try {
      await api.post(`/api/categories/${item.category.id}/${item.category.isPublished ? 'unpublish' : 'publish'}`)
      load()
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'İşlem başarısız.')
    }
  }

  return (
    <section>
      <h2>Kategoriler</h2>
      {error && <p className="error" role="alert">{error}</p>}
      <NewCategory onCreated={load} />
      {MODES.map((mode) => {
        const list = items.filter((i) => i.category.modeId === mode.id)
        return (
          <div key={mode.id} className="mode-block">
            <h3>{mode.name}</h3>
            {list.length === 0 && <p className="muted">Henüz kategori yok.</p>}
            <table>
              <tbody>
                {list.map((item) => (
                  <tr key={item.category.id}>
                    <td>
                      {editingId === item.category.id ? (
                        <EditCategory item={item} onDone={() => { setEditingId(null); load() }} />
                      ) : (
                        <>
                          <span className="swatch" style={{ background: item.category.colorHex ?? 'transparent' }} />
                          <strong>{item.category.name}</strong>
                          {item.category.id === 1 && <span className="muted"> (genel modun tek kategorisi)</span>}
                        </>
                      )}
                    </td>
                    <td className="counts">
                      <Count label="Kolay" value={item.counts.easy} target={TARGET.easy} />
                      <Count label="Orta" value={item.counts.medium} target={TARGET.medium} />
                      <Count label="Zor" value={item.counts.hard} target={TARGET.hard} />
                      <span className="muted">Taslak {item.counts.draft} · Arşiv {item.counts.archived}</span>
                    </td>
                    <td>{item.category.isPublished ? <span className="badge ok">Yayında</span> : <span className="badge">Gizli</span>}</td>
                    <td className="actions">
                      <button type="button" onClick={() => setEditingId(editingId === item.category.id ? null : item.category.id)}>Düzenle</button>
                      <button type="button" onClick={() => togglePublish(item)}>{item.category.isPublished ? 'Gizle' : 'Yayınla'}</button>
                      <a href={`#/questions?categoryId=${item.category.id}`}>Sorular</a>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )
      })}
    </section>
  )
}

function Count({ label, value, target }: { label: string; value: number; target: number }) {
  return (
    <span className={value >= target ? 'count full' : 'count'} title={`Yayındaki ${label.toLowerCase()} soru (hedef ${target})`}>
      {label} {value}/{target}
    </span>
  )
}

function NewCategory({ onCreated }: { onCreated: () => void }) {
  const [modeId, setModeId] = useState(2)
  const [name, setName] = useState('')
  const [colorHex, setColorHex] = useState('')
  const [error, setError] = useState('')

  async function submit(event: FormEvent) {
    event.preventDefault()
    try {
      await api.post('/api/categories', { modeId, name, colorHex: colorHex || null })
      setName('')
      setError('')
      onCreated()
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Eklenemedi.')
    }
  }

  return (
    <form className="inline-form" onSubmit={submit}>
      <select value={modeId} onChange={(e) => setModeId(Number(e.target.value))} aria-label="Mod">
        {MODES.filter((m) => m.id !== 1).map((m) => <option key={m.id} value={m.id}>{m.name}</option>)}
      </select>
      <input value={name} onChange={(e) => setName(e.target.value)} placeholder="Yeni kategori adı (örn. Tarih)" required />
      <input value={colorHex} onChange={(e) => setColorHex(e.target.value)} placeholder="#1E88E5 (isteğe bağlı)" size={18} />
      <button type="submit">Kategori ekle</button>
      {error && <span className="error" role="alert">{error}</span>}
    </form>
  )
}

function EditCategory({ item, onDone }: { item: CategoryListItem; onDone: () => void }) {
  const [name, setName] = useState(item.category.name)
  const [colorHex, setColorHex] = useState(item.category.colorHex ?? '')
  const [imageUrl, setImageUrl] = useState(item.category.imageUrl ?? '')
  const [error, setError] = useState('')

  async function save(event: FormEvent) {
    event.preventDefault()
    try {
      await api.put(`/api/categories/${item.category.id}`, {
        modeId: item.category.modeId, name, colorHex: colorHex || null, imageUrl: imageUrl || null, order: item.category.order,
      })
      onDone()
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Kaydedilemedi.')
    }
  }

  return (
    <form className="inline-form" onSubmit={save}>
      <input value={name} onChange={(e) => setName(e.target.value)} required />
      <input value={colorHex} onChange={(e) => setColorHex(e.target.value)} placeholder="#RRGGBB" size={10} />
      <input value={imageUrl} onChange={(e) => setImageUrl(e.target.value)} placeholder="Görsel adresi" />
      <input
        type="file"
        accept="image/png,image/jpeg,image/webp"
        aria-label="Görsel yükle"
        onChange={async (e) => {
          const file = e.target.files?.[0]
          if (!file) return
          try {
            setImageUrl(await uploadImage(file, 'categories'))
            setError('')
          } catch (err) {
            setError(err instanceof ApiError ? err.message : 'Görsel yüklenemedi.')
          }
        }}
      />
      <button type="submit">Kaydet</button>
      {error && <span className="error" role="alert">{error}</span>}
    </form>
  )
}
