// Mirrors the DTOs in backend/VideoCompression.Api/Endpoints/Dtos.cs (enums are camelCase strings).

export type CodecId = 'h264' | 'h265' | 'av1'
export type RateMode = 'quality' | 'targetSize'
export type EncodeSpeed = 'fast' | 'balanced' | 'slow'

export interface CompressionSettings {
  codec: CodecId
  mode: RateMode
  /** Constant-quality value; lower = better quality, bigger file. */
  crf: number
  /** Target size in MB (1 MB = 1,000,000 bytes); target-size mode only. */
  targetSizeMb: number | null
  /** Target-size mode: let the server lower the resolution when the bitrate would be too thin. */
  autoResolution: boolean
  /** Cap for the shorter side of the frame (720 = 720p); null keeps the original. */
  maxResolution: number | null
  maxFps: number | null
  /** AAC bitrate per track; 0 removes the audio. */
  audioBitrateKbps: number
  speed: EncodeSpeed
}

export interface CodecInfo {
  id: CodecId
  label: string
  available: boolean
  crfMin: number
  crfMax: number
  crfDefault: number
  compatibility: string
  description: string
}

export interface Health {
  ffmpegAvailable: boolean
  ffmpegVersion: string | null
  message: string | null
  codecs: CodecInfo[]
  maxUploadBytes: number
  acceptedExtensions: string[]
}

export interface VideoStream {
  index: number
  codec: string
  codecLabel: string
  width: number
  height: number
  fps: number
  bitrateBps: number
  bitrateEstimated: boolean
  pixelFormat: string | null
  bitDepth: number
  isHdr: boolean
  rotation: number
}

export interface AudioStream {
  index: number
  codec: string
  codecLabel: string
  bitrateBps: number | null
  channels: number
  sampleRate: number
  lossless: boolean
  language: string | null
}

export interface MediaInfo {
  container: string
  durationSeconds: number
  sizeBytes: number
  bitrateBps: number
  video: VideoStream
  audio: AudioStream[]
}

export type InsightTone = 'positive' | 'neutral' | 'warning'

export interface Insight {
  tone: InsightTone
  title: string
  detail: string
}

export type VerdictLevel = 'high' | 'good' | 'low' | 'minimal' | 'none' | 'unknown'

export interface Verdict {
  level: VerdictLevel
  headline: string
  summary: string
  recommendedSavingsPercent: number
  bestSavingsPercent: number
  suggestedPresetId: string | null
}

export type EstimateMethod = 'exact' | 'sampled' | 'target'

export interface SizeEstimate {
  bytes: number
  lowBytes: number
  highBytes: number
  savingsPercent: number
  method: EstimateMethod
}

export type QualityHint = 'excellent' | 'good' | 'fair' | 'poor'

export interface Plan {
  codec: string
  width: number
  height: number
  fps: number
  rateControl: string
  audio: string
  summary: string
  qualityHint: QualityHint | null
  warnings: string[]
}

export type PresetStatus = 'pending' | 'measuring' | 'done' | 'failed' | 'unavailable'

export interface Preset {
  id: string
  name: string
  tagline: string
  description: string
  recommended: boolean
  settings: CompressionSettings
  plan: Plan | null
  status: PresetStatus
  estimate: SizeEstimate | null
  error: string | null
}

export interface Video {
  id: string
  fileName: string
  sizeBytes: number
  uploadedAt: string
  media: MediaInfo
  insights: Insight[]
  presets: Preset[]
  analysis: { done: boolean; completed: number; total: number }
  verdict: Verdict | null
  minTargetSizeMb: number
}

export interface EstimateResult {
  valid: boolean
  error: string | null
  plan: Plan | null
  estimate: SizeEstimate | null
  minTargetSizeMb: number | null
}

export type JobStatus = 'queued' | 'running' | 'completed' | 'failed' | 'canceled'

export interface Job {
  id: string
  videoId: string
  status: JobStatus
  /** 0–100 */
  progress: number
  stage: string
  /** Encoding speed relative to real time (2 = twice as fast as playback). */
  speed: number | null
  etaSeconds: number | null
  elapsedSeconds: number
  originalBytes: number
  outputBytes: number | null
  savingsPercent: number | null
  error: string | null
  plan: Plan
  fileName: string
  downloadUrl: string | null
  previewUrl: string | null
}

/** Which option the user picked on the review screen. */
export type Choice = { kind: 'preset'; id: string } | { kind: 'target' } | { kind: 'custom' }
