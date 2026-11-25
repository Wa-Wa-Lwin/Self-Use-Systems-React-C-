import type { CompressionSettings, EstimateResult, Health, Job, Video } from './types'

const BASE = '/api'

export class ApiError extends Error {
  readonly status: number

  constructor(status: number, message: string) {
    super(message)
    this.name = 'ApiError'
    this.status = status
  }
}

/** Pulls the human-readable message out of an ASP.NET ProblemDetails body. */
function problemMessage(text: string, fallback: string): string {
  try {
    const body = JSON.parse(text) as { title?: string; detail?: string }
    return body.detail || body.title || fallback
  } catch {
    return fallback
  }
}

async function request<T>(path: string, init: RequestInit = {}): Promise<T> {
  let response: Response
  try {
    response = await fetch(BASE + path, {
      ...init,
      headers: init.body ? { 'Content-Type': 'application/json' } : undefined,
    })
  } catch (error) {
    if (isAbort(error)) throw error
    throw new ApiError(0, "Can't reach the server. Is the backend running?")
  }

  if (!response.ok) {
    throw new ApiError(response.status, problemMessage(await response.text(), `Request failed (${response.status})`))
  }
  if (response.status === 204) return undefined as T
  return (await response.json()) as T
}

export const api = {
  health: () => request<Health>('/health'),
  getVideo: (id: string) => request<Video>(`/videos/${id}`),
  deleteVideo: (id: string) => request<void>(`/videos/${id}`, { method: 'DELETE' }),
  estimate: (id: string, settings: CompressionSettings, signal?: AbortSignal) =>
    request<EstimateResult>(`/videos/${id}/estimate`, { method: 'POST', body: JSON.stringify(settings), signal }),
  startJob: (id: string, settings: CompressionSettings) =>
    request<Job>(`/videos/${id}/jobs`, { method: 'POST', body: JSON.stringify(settings) }),
  getJob: (id: string) => request<Job>(`/jobs/${id}`),
  cancelJob: (id: string) => request<Job>(`/jobs/${id}/cancel`, { method: 'POST' }),
}

export interface UploadProgress {
  loaded: number
  total: number
  bytesPerSecond: number
  /** True once all bytes are sent and the server is reading the video. */
  processing: boolean
}

/**
 * Uploads the raw file as the request body. XHR (not fetch) because it reports upload progress.
 */
export function uploadVideo(file: File, onProgress: (progress: UploadProgress) => void) {
  const xhr = new XMLHttpRequest()

  const promise = new Promise<Video>((resolve, reject) => {
    const started = performance.now()
    xhr.open('POST', `${BASE}/videos?fileName=${encodeURIComponent(file.name)}`)
    xhr.setRequestHeader('Content-Type', 'application/octet-stream')

    xhr.upload.onprogress = (event) => {
      const seconds = Math.max(0.001, (performance.now() - started) / 1000)
      onProgress({
        loaded: event.loaded,
        total: event.lengthComputable ? event.total : file.size,
        bytesPerSecond: event.loaded / seconds,
        processing: false,
      })
    }
    xhr.upload.onload = () => onProgress({ loaded: file.size, total: file.size, bytesPerSecond: 0, processing: true })

    xhr.onload = () => {
      if (xhr.status >= 200 && xhr.status < 300) {
        resolve(JSON.parse(xhr.responseText) as Video)
      } else {
        reject(new ApiError(xhr.status, problemMessage(xhr.responseText, `Upload failed (${xhr.status})`)))
      }
    }
    xhr.onerror = () => reject(new ApiError(0, "Upload failed: can't reach the server."))
    xhr.onabort = () => reject(new DOMException('Upload canceled', 'AbortError'))
    xhr.send(file)
  })

  return { promise, abort: () => xhr.abort() }
}

export function isAbort(error: unknown): boolean {
  return error instanceof DOMException && error.name === 'AbortError'
}

export function errorMessage(error: unknown): string {
  return error instanceof Error ? error.message : String(error)
}
