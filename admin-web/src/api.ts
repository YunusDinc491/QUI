// Admin API istemcisi. Oturum belirteci sekme kapanınca silinsin diye sessionStorage'da tutulur.

export const API_URL: string = import.meta.env.VITE_ADMIN_API_URL ?? 'http://localhost:5090'

const TOKEN_KEY = 'qui-admin-token'

export function getToken(): string | null {
  try {
    return sessionStorage.getItem(TOKEN_KEY)
  } catch {
    return null
  }
}

export function setToken(token: string | null): void {
  try {
    if (token) sessionStorage.setItem(TOKEN_KEY, token)
    else sessionStorage.removeItem(TOKEN_KEY)
  } catch {
    // depolama kapalıysa oturum yalnızca bu sayfa açıkken sürer
  }
}

export interface Duplicate {
  questionId: number
  categoryId: number
  text: string
  similarity: number
}

export class ApiError extends Error {
  status: number
  code: string
  duplicates?: Duplicate[]

  constructor(status: number, code: string, message: string, duplicates?: Duplicate[]) {
    super(message)
    this.status = status
    this.code = code
    this.duplicates = duplicates
  }
}

export const AUTH_LOST_EVENT = 'qui-auth-lost'

async function request<T>(method: string, path: string, body?: unknown): Promise<T> {
  const headers: Record<string, string> = {}
  const token = getToken()
  if (token) headers.Authorization = `Bearer ${token}`
  if (body !== undefined) headers['Content-Type'] = 'application/json'

  let response: Response
  try {
    response = await fetch(`${API_URL}${path}`, {
      method,
      headers,
      body: body === undefined ? undefined : JSON.stringify(body),
    })
  } catch {
    throw new ApiError(0, 'NETWORK', 'Sunucuya ulaşılamadı. Admin API çalışıyor mu?')
  }

  if (response.status === 204) return undefined as T
  const text = await response.text()
  const data = text ? JSON.parse(text) : undefined

  if (!response.ok) {
    // Giriş ekranındaki 401 "hatalı bilgi"dir; oturum düşmesi yalnızca belirteç varken sayılır
    if (response.status === 401 && token) {
      setToken(null)
      window.dispatchEvent(new Event(AUTH_LOST_EVENT))
    }
    throw new ApiError(
      response.status,
      data?.code ?? 'ERROR',
      data?.message ?? data?.error ?? `İstek başarısız (${response.status}).`,
      data?.duplicates,
    )
  }
  return data as T
}

/** Görsel yükler (PNG/JPEG/WebP, en çok 1 MB); herkese açık adresi döndürür. */
export async function uploadImage(file: File, folder: 'categories' | 'avatars' | 'frames'): Promise<string> {
  const form = new FormData()
  form.append('file', file)
  let response: Response
  try {
    response = await fetch(`${API_URL}/api/uploads/image?folder=${folder}`, {
      method: 'POST',
      headers: { Authorization: `Bearer ${getToken() ?? ''}` },
      body: form,
    })
  } catch {
    throw new ApiError(0, 'NETWORK', 'Sunucuya ulaşılamadı. Admin API çalışıyor mu?')
  }
  const data = await response.json().catch(() => undefined)
  if (!response.ok) throw new ApiError(response.status, data?.code ?? 'ERROR', data?.message ?? 'Görsel yüklenemedi.')
  return data.url as string
}

export const api = {
  get: <T>(path: string) => request<T>('GET', path),
  post: <T>(path: string, body?: unknown) => request<T>('POST', path, body ?? {}),
  put: <T>(path: string, body: unknown) => request<T>('PUT', path, body),
  del: <T>(path: string) => request<T>('DELETE', path),
}

// ---- Tipler ----

export type DifficultyName = 'Easy' | 'Medium' | 'Hard'
export type StatusName = 'Draft' | 'Published' | 'Archived'

export const DIFFICULTY_LABEL: Record<DifficultyName, string> = { Easy: 'Kolay', Medium: 'Orta', Hard: 'Zor' }
export const STATUS_LABEL: Record<StatusName, string> = { Draft: 'Taslak', Published: 'Yayında', Archived: 'Arşiv' }

/** Modlar sabittir (sunucuda 1–4 kimlikle tohumlanır). */
export const MODES = [
  { id: 1, code: 'general', name: 'Genel sorular' },
  { id: 2, code: 'branch', name: 'Branş seçmeli' },
  { id: 3, code: 'kpss', name: 'KPSS tarzı' },
  { id: 4, code: 'fan', name: 'Fanlara özel' },
] as const

export interface Category {
  id: number
  modeId: number
  modeCode: string
  name: string
  imageUrl: string | null
  colorHex: string | null
  order: number
  isPublished: boolean
}

export interface CategoryCounts {
  easy: number
  medium: number
  hard: number
  draft: number
  archived: number
}

export interface CategoryListItem {
  category: Category
  counts: CategoryCounts
}

export interface Question {
  id: number
  categoryId: number
  text: string
  options: string[]
  correctIndex: number
  difficulty: DifficultyName
  explanation: string | null
  status: StatusName
  shownCount: number
  correctCount: number
  reportCount: number
  createdAt: string
  updatedAt: string
}

export interface QuestionPage {
  items: Question[]
  total: number
  page: number
  pageSize: number
}

export interface QuestionPayload {
  categoryId: number
  text: string
  options: string[]
  correctIndex: number
  difficulty: DifficultyName
  explanation: string
  status: StatusName
  ignoreDuplicates: boolean
}

export interface ImportRow {
  line: number
  status: 'ok' | 'error' | 'duplicate'
  errors: string[]
  duplicates: Duplicate[]
  fileDuplicateOf: string | null
  mode: string
  category: string
  text: string
}

export interface ImportPreview {
  fatalError: string | null
  total: number
  valid: number
  invalid: number
  duplicates: number
  rows: ImportRow[]
}

export interface AuditItem {
  id: number
  adminEmail: string | null
  action: string
  entity: string
  entityId: string | null
  before: string | null
  after: string | null
  detail: string | null
  createdAt: string
}
