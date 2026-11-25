// Sizes use decimal units (1 MB = 1,000,000 bytes), matching the server's target-size maths.

export function formatBytes(bytes: number | null | undefined): string {
  if (bytes == null || !Number.isFinite(bytes)) return '—'
  if (bytes < 1000) return `${Math.round(bytes)} B`
  const units = ['KB', 'MB', 'GB', 'TB']
  let value = bytes / 1000
  let unit = 0
  while (value >= 1000 && unit < units.length - 1) {
    value /= 1000
    unit++
  }
  const digits = value >= 100 ? 0 : value >= 10 ? 1 : 2
  return `${value.toFixed(digits)} ${units[unit]}`
}

export function formatDuration(seconds: number): string {
  const total = Math.max(0, Math.round(seconds))
  const h = Math.floor(total / 3600)
  const m = Math.floor((total % 3600) / 60)
  const s = total % 60
  const pad = (n: number) => String(n).padStart(2, '0')
  return h > 0 ? `${h}:${pad(m)}:${pad(s)}` : `${m}:${pad(s)}`
}

/** "45s", "3m 20s", "1h 05m" */
export function formatEta(seconds: number): string {
  const total = Math.max(1, Math.round(seconds))
  if (total < 60) return `${total}s`
  if (total < 3600) return `${Math.floor(total / 60)}m ${String(total % 60).padStart(2, '0')}s`
  return `${Math.floor(total / 3600)}h ${String(Math.floor((total % 3600) / 60)).padStart(2, '0')}m`
}

export function formatBitrate(bitsPerSecond: number): string {
  if (bitsPerSecond >= 1_000_000) return `${(bitsPerSecond / 1_000_000).toFixed(bitsPerSecond >= 10_000_000 ? 0 : 1)} Mbps`
  return `${Math.round(bitsPerSecond / 1000)} kbps`
}

export function formatFps(fps: number): string {
  return Math.abs(fps - Math.round(fps)) < 0.01 ? String(Math.round(fps)) : fps.toFixed(2)
}

/** "4K", "1080p", "720p"… based on the shorter side, so portrait videos are labelled correctly. */
export function resolutionLabel(width: number, height: number): string {
  const short = Math.min(width, height)
  if (short >= 2000) return '4K'
  if (short >= 1400) return '1440p'
  if (short >= 1000) return '1080p'
  if (short >= 700) return '720p'
  if (short >= 520) return '540p'
  if (short >= 470) return '480p'
  if (short >= 350) return '360p'
  return `${short}p`
}

/** Rounds a size in MB to something a person would type. */
export function roundNiceMb(mb: number): number {
  if (mb >= 100) return Math.round(mb / 10) * 10
  if (mb >= 10) return Math.round(mb)
  if (mb >= 1) return Math.round(mb * 10) / 10
  return Math.max(0.01, Math.round(mb * 100) / 100)
}

export function clamp(value: number, min: number, max: number): number {
  return Math.min(max, Math.max(min, value))
}
