import type { CodecInfo, CompressionSettings, Health, MediaInfo, Video } from '../api/types'
import { roundNiceMb } from './format'

export const STANDARD_RESOLUTIONS = [2160, 1440, 1080, 720, 540, 480, 360, 240]

export const AUDIO_CHOICES = [
  { kbps: 192, label: '192 kbps · Excellent' },
  { kbps: 160, label: '160 kbps · Very good' },
  { kbps: 128, label: '128 kbps · Great' },
  { kbps: 96, label: '96 kbps · Good' },
  { kbps: 64, label: '64 kbps · Speech' },
  { kbps: 0, label: 'Remove audio' },
]

export const SPEED_CHOICES = [
  { value: 'fast', label: 'Faster', hint: 'Quick encode, slightly bigger file' },
  { value: 'balanced', label: 'Balanced', hint: 'Good speed and size' },
  { value: 'slow', label: 'Smallest', hint: 'Takes longer, squeezes a little more' },
] as const

export function shortSide(media: MediaInfo): number {
  return Math.min(media.video.width, media.video.height)
}

/** Resolution caps that would actually make this video smaller. */
export function resolutionChoices(media: MediaInfo): number[] {
  const short = shortSide(media)
  return STANDARD_RESOLUTIONS.filter((r) => r < short)
}

/** Frame-rate caps below the source frame rate. */
export function fpsChoices(media: MediaInfo): number[] {
  return [60, 30, 24].filter((fps) => media.video.fps > fps + 0.5)
}

export function availableCodecs(health: Health | null): CodecInfo[] {
  return health?.codecs.filter((c) => c.available) ?? []
}

/** Words for a CRF value, relative to the codec's default. */
export function qualityLabel(codec: CodecInfo, crf: number): string {
  const delta = crf - codec.crfDefault
  if (delta <= -5) return 'Excellent'
  if (delta <= -2) return 'Very good'
  if (delta <= 1) return 'Good'
  if (delta <= 4) return 'Fair'
  return 'Low'
}

export function suggestedTargetMb(video: Video): number {
  return Math.max(video.minTargetSizeMb, roundNiceMb((video.sizeBytes / 1_000_000) * 0.3))
}

export function defaultCustomSettings(video: Video): CompressionSettings {
  const base = video.presets.find((p) => p.recommended) ?? video.presets[0]
  return { ...base.settings }
}

/** Target size defaults to H.264: "fit under X MB" usually means sharing, where compatibility matters most. */
export function defaultTargetSettings(video: Video, health: Health | null): CompressionSettings {
  const codecs = availableCodecs(health)
  const codec = codecs.find((c) => c.id === 'h264') ?? codecs[0]
  return {
    codec: codec?.id ?? 'h264',
    mode: 'targetSize',
    crf: codec?.crfDefault ?? 23,
    targetSizeMb: suggestedTargetMb(video),
    autoResolution: true,
    maxResolution: null,
    maxFps: null,
    audioBitrateKbps: 96,
    speed: 'balanced',
  }
}
