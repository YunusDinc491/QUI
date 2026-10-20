import { useState } from 'react'
import type { ChangeEvent } from 'react'
import { api, ApiError } from '../api.ts'
import type { ImportPreview } from '../api.ts'

const SAMPLE = [
  'mod;kategori;zorluk;soru;A;B;C;D;dogru;aciklama',
  'branş;Tarih;kolay;İstanbul hangi yılda fethedilmiştir?;1453;1071;1299;1517;A;Fatih Sultan Mehmet dönemi',
  'genel;;orta;Türkiye\'nin başkenti neresidir?;İstanbul;Ankara;İzmir;Bursa;B;',
].join('\r\n')

const STATUS_TEXT = { ok: 'Eklenebilir', error: 'Hatalı', duplicate: 'Tekrar şüphesi' } as const

// Toplu yükleme (docs/05 §2): önce önizleme (satır satır hata + tekrar uyarısı), sonra onayla-ekle.
export function Import() {
  const [csv, setCsv] = useState('')
  const [fileName, setFileName] = useState('')
  const [preview, setPreview] = useState<ImportPreview | null>(null)
  const [includeDuplicates, setIncludeDuplicates] = useState(false)
  const [publish, setPublish] = useState(false)
  const [error, setError] = useState('')
  const [result, setResult] = useState('')
  const [busy, setBusy] = useState(false)

  async function onFile(event: ChangeEvent<HTMLInputElement>) {
    const file = event.target.files?.[0]
    if (!file) return
    setFileName(file.name)
    setCsv(await file.text())
    setPreview(null)
    setResult('')
  }

  async function runPreview() {
    setBusy(true)
    setError('')
    setResult('')
    try {
      setPreview(await api.post<ImportPreview>('/api/questions/import/preview', { csv }))
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Önizleme alınamadı.')
    } finally {
      setBusy(false)
    }
  }

  async function commit() {
    setBusy(true)
    setError('')
    try {
      const r = await api.post<{ created: number; skippedInvalid: number; skippedDuplicates: number }>(
        '/api/questions/import/commit', { csv, includeDuplicates, publish })
      setResult(`${r.created} soru eklendi. Atlanan: ${r.skippedInvalid} hatalı, ${r.skippedDuplicates} tekrar şüphesi.`)
      setPreview(null)
      setCsv('')
      setFileName('')
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Yükleme başarısız.')
    } finally {
      setBusy(false)
    }
  }

  function downloadSample() {
    const url = URL.createObjectURL(new Blob(['﻿' + SAMPLE], { type: 'text/csv;charset=utf-8' }))
    const link = document.createElement('a')
    link.href = url
    link.download = 'ornek-sorular.csv'
    link.click()
    URL.revokeObjectURL(url)
  }

  const addable = preview ? preview.valid + (includeDuplicates ? preview.duplicates : 0) : 0

  return (
    <section>
      <h2>Toplu soru yükleme</h2>
      <p className="muted">
        CSV dosyası (Excel'de "CSV UTF-8" olarak kaydedin). Sütunlar: mod, kategori, zorluk (kolay/orta/zor), soru, A, B, C, D, dogru (A–D), aciklama.
        Kategoriler önceden eklenmiş olmalı. En fazla 500 satır.{' '}
        <button type="button" className="link" onClick={downloadSample}>Örnek dosyayı indir</button>
      </p>

      <div className="inline-form">
        <input type="file" accept=".csv,text/csv,text/plain" onChange={onFile} aria-label="CSV dosyası" />
        {fileName && <span className="muted">{fileName}</span>}
        <button type="button" onClick={runPreview} disabled={busy || !csv}>Önizle</button>
      </div>

      {error && <p className="error" role="alert">{error}</p>}
      {result && <p className="success" role="status">{result}</p>}

      {preview?.fatalError && <p className="error" role="alert">{preview.fatalError}</p>}

      {preview && !preview.fatalError && (
        <>
          <p>
            <strong>{preview.total}</strong> satır: <span className="ok-text">{preview.valid} eklenebilir</span>,{' '}
            <span className="warn-text">{preview.duplicates} tekrar şüphesi</span>,{' '}
            <span className="error">{preview.invalid} hatalı</span>
          </p>
          <div className="inline-form">
            <label><input type="checkbox" checked={includeDuplicates} onChange={(e) => setIncludeDuplicates(e.target.checked)} /> Tekrar şüphelileri de ekle</label>
            <label><input type="checkbox" checked={publish} onChange={(e) => setPublish(e.target.checked)} /> Doğrudan yayınla (aksi halde taslak)</label>
            <button type="button" onClick={commit} disabled={busy || addable === 0}>{addable} soruyu ekle</button>
          </div>
          <table className="list">
            <thead><tr><th>Satır</th><th>Durum</th><th>Kategori</th><th>Soru</th><th>Ayrıntı</th></tr></thead>
            <tbody>
              {preview.rows.map((row) => (
                <tr key={row.line} className={`row-${row.status}`}>
                  <td>{row.line}</td>
                  <td>{STATUS_TEXT[row.status]}</td>
                  <td>{row.category || '—'}</td>
                  <td className="text">{row.text}</td>
                  <td>
                    {row.errors.map((m) => <div key={m} className="error">{m}</div>)}
                    {row.fileDuplicateOf && <div className="warn-text">{row.fileDuplicateOf}</div>}
                    {row.duplicates.map((d) => <div key={d.questionId} className="warn-text">Benzer: #{d.questionId} (%{Math.round(d.similarity * 100)})</div>)}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </>
      )}
    </section>
  )
}
