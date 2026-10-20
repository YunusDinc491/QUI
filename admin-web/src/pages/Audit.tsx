import { useEffect, useState } from 'react'
import { api, ApiError } from '../api.ts'
import type { AuditItem } from '../api.ts'

const PAGE_SIZE = 50

// Sunucu UTC zamanı döndürür; "Z" eki yoksa ekleyip yerel saate çevir
function formatTime(value: string): string {
  const utc = /(Z|[+-]\d\d:\d\d)$/.test(value) ? value : `${value}Z`
  return new Date(utc).toLocaleString('tr-TR')
}

// Denetim kaydı (docs/05 §9): kim, ne, ne zaman, önce/sonra. Salt okunur.
export function Audit() {
  const [data, setData] = useState<{ items: AuditItem[]; total: number } | null>(null)
  const [page, setPage] = useState(1)
  const [entity, setEntity] = useState('')
  const [error, setError] = useState('')

  useEffect(() => {
    const params = new URLSearchParams({ page: String(page), pageSize: String(PAGE_SIZE) })
    if (entity) params.set('entity', entity)
    api.get<{ items: AuditItem[]; total: number }>(`/api/audit-logs?${params}`)
      .then((d) => { setData(d); setError('') })
      .catch((e) => setError(e instanceof ApiError ? e.message : 'Kayıtlar alınamadı.'))
  }, [page, entity])

  const pageCount = data ? Math.max(1, Math.ceil(data.total / PAGE_SIZE)) : 1

  return (
    <section>
      <h2>Denetim kaydı {data && <span className="muted">({data.total})</span>}</h2>
      <div className="inline-form">
        <select value={entity} onChange={(e) => { setPage(1); setEntity(e.target.value) }} aria-label="Varlık">
          <option value="">Tümü</option>
          <option value="question">Sorular</option>
          <option value="category">Kategoriler</option>
          <option value="admin_user">Yönetici hesapları</option>
        </select>
      </div>
      {error && <p className="error" role="alert">{error}</p>}
      <table className="list">
        <thead><tr><th>Zaman</th><th>Yönetici</th><th>İşlem</th><th>Varlık</th><th>Önce</th><th>Sonra / ayrıntı</th></tr></thead>
        <tbody>
          {data?.items.map((l) => (
            <tr key={l.id}>
              <td>{formatTime(l.createdAt)}</td>
              <td>{l.adminEmail ?? '—'}</td>
              <td>{l.action}</td>
              <td>{l.entity}{l.entityId ? ` #${l.entityId}` : ''}</td>
              <td className="json">{l.before ?? ''}</td>
              <td className="json">{l.after ?? l.detail ?? ''}</td>
            </tr>
          ))}
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
