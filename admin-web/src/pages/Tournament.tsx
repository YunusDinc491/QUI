import { useEffect, useState } from 'react'
import { api, ApiError } from '../api.ts'

interface PeriodRow {
  id: number
  startsAt: string
  endsAt: string
  phase: 'open' | 'review' | 'awaiting_distribution' | 'distributed'
  reviewDue: boolean
  participants: number
  totalPoints: number
}

interface Standing {
  rank: number | null
  userId: string
  name: string
  points: number
  matchCount: number
  disqualified: boolean
  disqualificationReason: string | null
}

interface Tier {
  rankFrom: number
  rankTo: number
  amount: number
}

interface Budget {
  perPeriodPoints: number
  perMonthPoints: number
  perMonthLira: number
}

const PHASE_TEXT: Record<PeriodRow['phase'], string> = {
  open: 'Açık',
  review: 'İnceleme zamanı (son 1 saat)',
  awaiting_distribution: 'Dağıtım bekliyor',
  distributed: 'Dağıtıldı',
}

const fmt = (iso: string) => new Date(iso.endsWith('Z') ? iso : `${iso}Z`).toLocaleString('tr-TR')

// Turnuva yönetimi (docs/05 §6, docs/07 §2–3): dönemler, inceleme sıralaması, men, ödül tablosu ve bütçe göstergesi
export function Tournament() {
  const [periods, setPeriods] = useState<PeriodRow[]>([])
  const [selected, setSelected] = useState<number | null>(null)
  const [standings, setStandings] = useState<Standing[]>([])
  const [tiers, setTiers] = useState<Tier[]>([])
  const [budget, setBudget] = useState<Budget | null>(null)
  const [error, setError] = useState('')
  const [info, setInfo] = useState('')
  const [reload, setReload] = useState(0)

  useEffect(() => {
    api.get<PeriodRow[]>('/api/tournament/periods')
      .then((list) => {
        setPeriods(list)
        setSelected((current) => current ?? list[0]?.id ?? null)
      })
      .catch((e) => setError(e instanceof ApiError ? e.message : 'Dönemler alınamadı.'))
    api.get<{ tiers: Tier[]; budget: Budget }>('/api/tournament/rewards')
      .then((r) => { setTiers(r.tiers); setBudget(r.budget) })
      .catch(() => undefined)
  }, [reload])

  useEffect(() => {
    if (selected === null) return
    api.get<Standing[]>(`/api/tournament/periods/${selected}/standings?take=100`)
      .then(setStandings)
      .catch((e) => setError(e instanceof ApiError ? e.message : 'Sıralama alınamadı.'))
  }, [selected, reload])

  const period = periods.find((p) => p.id === selected)
  const locked = period?.phase === 'distributed'

  async function disqualify(row: Standing) {
    const reason = window.prompt(`${row.name} bu turnuvadan men edilsin mi? Gerekçe:`)
    if (!reason?.trim() || selected === null) return
    try {
      await api.post(`/api/tournament/periods/${selected}/disqualify`, { userId: row.userId, reason })
      setError('')
      setReload((n) => n + 1)
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Men uygulanamadı.')
    }
  }

  async function lift(row: Standing) {
    if (selected === null) return
    try {
      await api.del(`/api/tournament/periods/${selected}/disqualify/${row.userId}`)
      setReload((n) => n + 1)
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Men kaldırılamadı.')
    }
  }

  function updateTier(index: number, field: keyof Tier, value: number) {
    setTiers((current) => current.map((t, i) => (i === index ? { ...t, [field]: value } : t)))
  }

  async function saveTiers() {
    try {
      const result = await api.put<{ tiers: Tier[]; budget: Budget }>('/api/tournament/rewards', { tiers })
      setTiers(result.tiers)
      setBudget(result.budget)
      setError('')
      setInfo('Ödül tablosu kaydedildi.')
    } catch (e) {
      setInfo('')
      setError(e instanceof ApiError ? e.message : 'Kaydedilemedi.')
    }
  }

  return (
    <section>
      <h2>Turnuva</h2>
      {error && <p className="error" role="alert">{error}</p>}
      {info && <p className="success" role="status">{info}</p>}

      <h3>Dönemler</h3>
      <table className="list">
        <thead><tr><th>Dönem</th><th>Başlangıç</th><th>Bitiş</th><th>Durum</th><th>Katılımcı</th><th>Toplam puan</th><th></th></tr></thead>
        <tbody>
          {periods.map((p) => (
            <tr key={p.id} className={p.id === selected ? 'row-duplicate' : ''}>
              <td>#{p.id}</td>
              <td>{fmt(p.startsAt)}</td>
              <td>{fmt(p.endsAt)}</td>
              <td>{p.reviewDue ? <span className="badge warn">{PHASE_TEXT[p.phase]}</span> : PHASE_TEXT[p.phase]}</td>
              <td>{p.participants}</td>
              <td>{p.totalPoints}</td>
              <td><button type="button" onClick={() => setSelected(p.id)}>İncele</button></td>
            </tr>
          ))}
        </tbody>
      </table>

      {period && (
        <>
          <h3>Sıralama — dönem #{period.id} {locked && <span className="muted">(dağıtıldı; men değiştirilemez)</span>}</h3>
          <table className="list">
            <thead><tr><th>Sıra</th><th>Oyuncu</th><th>Puan</th><th>Maç</th><th>Durum</th><th></th></tr></thead>
            <tbody>
              {standings.map((s) => (
                <tr key={s.userId} className={s.disqualified ? 'row-error' : ''}>
                  <td>{s.rank ?? '—'}</td>
                  <td>{s.name}</td>
                  <td>{s.points}</td>
                  <td>{s.matchCount}</td>
                  <td>{s.disqualified ? `Men: ${s.disqualificationReason}` : ''}</td>
                  <td>
                    {!locked && (s.disqualified
                      ? <button type="button" onClick={() => lift(s)}>Meni kaldır</button>
                      : <button type="button" onClick={() => disqualify(s)}>Men et</button>)}
                  </td>
                </tr>
              ))}
              {standings.length === 0 && <tr><td colSpan={6} className="muted">Bu dönemde henüz puan yok.</td></tr>}
            </tbody>
          </table>
        </>
      )}

      <h3>Ödül tablosu</h3>
      <table className="list">
        <thead><tr><th>Sıra (başlangıç)</th><th>Sıra (bitiş)</th><th>Hediye puanı</th><th></th></tr></thead>
        <tbody>
          {tiers.map((t, i) => (
            <tr key={i}>
              <td><input type="number" min={1} value={t.rankFrom} onChange={(e) => updateTier(i, 'rankFrom', Number(e.target.value))} /></td>
              <td><input type="number" min={1} value={t.rankTo} onChange={(e) => updateTier(i, 'rankTo', Number(e.target.value))} /></td>
              <td><input type="number" min={1} value={t.amount} onChange={(e) => updateTier(i, 'amount', Number(e.target.value))} /></td>
              <td><button type="button" onClick={() => setTiers((c) => c.filter((_, j) => j !== i))}>Sil</button></td>
            </tr>
          ))}
        </tbody>
      </table>
      <div className="inline-form">
        <button type="button" className="secondary" onClick={() => setTiers((c) => [...c, { rankFrom: (c.at(-1)?.rankTo ?? 0) + 1, rankTo: (c.at(-1)?.rankTo ?? 0) + 1, amount: 10 }])}>Satır ekle</button>
        <button type="button" onClick={saveTiers}>Ödül tablosunu kaydet</button>
      </div>
      {budget && (
        <p className="muted" data-testid="budget">
          Bütçe göstergesi: bir turda en fazla <strong>{budget.perPeriodPoints}</strong> puan, ayda (15 tur) <strong>{budget.perMonthPoints}</strong> puan
          ≈ <strong>{budget.perMonthLira.toLocaleString('tr-TR')} TL</strong> (1 puan = 0,05 TL).
        </p>
      )}
    </section>
  )
}
