import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { ApiError, api, errorMessage, isAbort, uploadVideo, type UploadProgress } from './api/client'
import type { Choice, CompressionSettings, Health, Job, SizeEstimate, Video } from './api/types'
import { CompressBar } from './components/CompressBar'
import { Header } from './components/Header'
import { RefreshIcon } from './components/icons'
import { JobProgress } from './components/JobProgress'
import { OptionsPanel } from './components/OptionsPanel'
import { ResultPanel } from './components/ResultPanel'
import { Button, Notice } from './components/ui'
import { UploadDropzone, UploadingCard } from './components/UploadScreen'
import { VerdictCard } from './components/VerdictCard'
import { VideoOverview } from './components/VideoOverview'
import { stemOf } from './lib/filename'
import { formatBytes } from './lib/format'
import { useEstimate, usePolling } from './lib/hooks'
import { defaultCustomSettings, defaultTargetSettings } from './lib/options'

type Phase = 'select' | 'uploading' | 'review' | 'compressing' | 'done'

const STEP: Record<Phase, number> = { select: 0, uploading: 0, review: 1, compressing: 2, done: 3 }

interface Selection {
  name: string
  settings: CompressionSettings | null
  summary: string | null
  estimate: SizeEstimate | null
  estimating: boolean
  usable: boolean
}

export default function App() {
  const [health, setHealth] = useState<Health | null>(null)
  const [healthError, setHealthError] = useState<string | null>(null)
  const [phase, setPhase] = useState<Phase>('select')
  const [file, setFile] = useState<File | null>(null)
  const [previewUrl, setPreviewUrl] = useState<string | null>(null)
  const [upload, setUpload] = useState<UploadProgress | null>(null)
  const [video, setVideo] = useState<Video | null>(null)
  const [job, setJob] = useState<Job | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [choice, setChoice] = useState<Choice>({ kind: 'preset', id: 'balanced' })
  const [target, setTarget] = useState<CompressionSettings | null>(null)
  const [custom, setCustom] = useState<CompressionSettings | null>(null)
  const [starting, setStarting] = useState(false)
  const [canceling, setCanceling] = useState(false)
  /** Name for the compressed file (without .mp4); null until the first job suggests one. Kept across retries. */
  const [outputName, setOutputName] = useState<string | null>(null)
  const userChose = useRef(false)
  const abortUpload = useRef<(() => void) | null>(null)

  // ---- server status -------------------------------------------------------------------------

  const checkHealth = useCallback(async () => {
    try {
      const result = await api.health()
      setHealth(result)
      setHealthError(null)
      return result
    } catch (e) {
      setHealthError(errorMessage(e))
      return null
    }
  }, [])

  useEffect(() => {
    void checkHealth()
  }, [checkHealth])

  // While the server or FFmpeg is missing, keep checking so the page recovers by itself.
  usePolling(
    async () => !(await checkHealth())?.ffmpegAvailable,
    4000,
    healthError !== null || (health !== null && !health.ffmpegAvailable),
  )

  // Free the local preview's memory when it is replaced or the page goes away.
  useEffect(() => () => {
    if (previewUrl) URL.revokeObjectURL(previewUrl)
  }, [previewUrl])

  const clearVideo = () => {
    setVideo(null)
    setJob(null)
    setFile(null)
    setPreviewUrl(null)
    setTarget(null)
    setCustom(null)
    setOutputName(null)
    setPhase('select')
  }

  const expired = () => {
    clearVideo()
    setError('That upload is no longer on the server (it expired or the server restarted). Please upload the video again.')
  }

  // ---- upload --------------------------------------------------------------------------------

  const handleFile = async (selected: File) => {
    setError(null)
    const extension = selected.name.includes('.') ? `.${selected.name.split('.').pop()!.toLowerCase()}` : ''
    if (health && !health.acceptedExtensions.includes(extension)) {
      setError(`"${extension || selected.name}" isn't a supported video format. Try MP4, MOV, MKV, WebM or AVI.`)
      return
    }
    if (health && selected.size > health.maxUploadBytes) {
      setError(`This file is ${formatBytes(selected.size)}; the upload limit is ${formatBytes(health.maxUploadBytes)}.`)
      return
    }
    if (selected.size === 0) {
      setError('This file is empty.')
      return
    }

    setFile(selected)
    setPreviewUrl(URL.createObjectURL(selected))
    setUpload({ loaded: 0, total: selected.size, bytesPerSecond: 0, processing: false })
    setPhase('uploading')

    const { promise, abort } = uploadVideo(selected, setUpload)
    abortUpload.current = abort
    try {
      const uploaded = await promise
      setVideo(uploaded)
      setTarget(defaultTargetSettings(uploaded, health))
      setCustom(defaultCustomSettings(uploaded))
      const recommended = uploaded.presets.find((p) => p.recommended && p.status !== 'unavailable') ?? uploaded.presets.find((p) => p.status !== 'unavailable')
      setChoice(recommended ? { kind: 'preset', id: recommended.id } : { kind: 'custom' })
      userChose.current = false
      setPhase('review')
    } catch (e) {
      clearVideo()
      if (!isAbort(e)) setError(errorMessage(e))
    } finally {
      abortUpload.current = null
      setUpload(null)
    }
  }

  // ---- analysis (the server measures each preset in the background) -------------------------

  usePolling(
    async () => {
      if (!video) return false
      try {
        const fresh = await api.getVideo(video.id)
        setVideo(fresh)
        return !fresh.analysis.done
      } catch (e) {
        if (e instanceof ApiError && e.status === 404) {
          expired()
          return false
        }
        throw e
      }
    },
    1200,
    video !== null && !video.analysis.done && (phase === 'review' || phase === 'compressing' || phase === 'done'),
  )

  // Once the verdict is in, pre-select the option it suggests, unless the user already picked one.
  const suggested = video?.verdict?.suggestedPresetId
  useEffect(() => {
    if (!suggested || userChose.current) return
    setChoice((current) => (current.kind === 'preset' && current.id === suggested ? current : { kind: 'preset', id: suggested }))
  }, [suggested])

  const choose = (next: Choice) => {
    userChose.current = true
    setChoice(next)
  }

  const reviewing = phase === 'review' && video !== null
  const targetEstimate = useEstimate(video?.id ?? '', reviewing && choice.kind === 'target' ? target : null, 250)
  const customEstimate = useEstimate(video?.id ?? '', reviewing && choice.kind === 'custom' ? custom : null, 600)

  const selection = useMemo<Selection | null>(() => {
    if (!video) return null
    if (choice.kind === 'preset') {
      const preset = video.presets.find((p) => p.id === choice.id)
      if (!preset) return null
      return {
        name: `${preset.name} preset`,
        settings: preset.settings,
        summary: preset.plan?.summary ?? null,
        estimate: preset.estimate,
        estimating: preset.status === 'pending' || preset.status === 'measuring',
        usable: preset.status !== 'unavailable',
      }
    }
    const state = choice.kind === 'target' ? targetEstimate : customEstimate
    const settings = choice.kind === 'target' ? target : custom
    const result = state.result
    return {
      name: choice.kind === 'target' ? `Target size · ${settings?.targetSizeMb ?? '?'} MB` : 'Custom settings',
      settings,
      summary: result?.plan?.summary ?? null,
      estimate: result?.valid ? result.estimate : null,
      estimating: state.loading || (!result && !state.error),
      // Settings the server rejected (no plan) can't be used; a failed measurement alone shouldn't block compressing.
      usable: !result || result.valid || result.plan !== null,
    }
  }, [video, choice, target, custom, targetEstimate, customEstimate])

  // ---- compression job -----------------------------------------------------------------------

  const compress = async () => {
    if (!video || !selection?.settings) return
    setStarting(true)
    setError(null)
    try {
      const started = await api.startJob(video.id, selection.settings)
      setJob(started)
      setOutputName((current) => current ?? stemOf(started.fileName))
      setPhase('compressing')
      window.scrollTo({ top: 0, behavior: 'smooth' })
    } catch (e) {
      if (e instanceof ApiError && e.status === 404) expired()
      else setError(errorMessage(e))
    } finally {
      setStarting(false)
    }
  }

  const backToOptions = () => {
    setJob(null)
    setPhase('review')
  }

  usePolling(
    async () => {
      if (!job) return false
      let fresh: Job
      try {
        fresh = await api.getJob(job.id)
      } catch (e) {
        if (e instanceof ApiError && e.status === 404) {
          expired()
          return false
        }
        throw e
      }
      setJob(fresh)
      if (fresh.status === 'completed') setPhase('done')
      if (fresh.status === 'canceled') backToOptions()
      return fresh.status === 'queued' || fresh.status === 'running'
    },
    700,
    phase === 'compressing' && job !== null && (job.status === 'queued' || job.status === 'running'),
  )

  const cancelJob = async () => {
    if (!job) return
    setCanceling(true)
    try {
      const updated = await api.cancelJob(job.id)
      if (updated.status === 'canceled') backToOptions()
      else setJob(updated)
    } catch (e) {
      setError(errorMessage(e))
    } finally {
      setCanceling(false)
    }
  }

  const startOver = () => {
    abortUpload.current?.()
    if (video) void api.deleteVideo(video.id).catch(() => undefined)
    setError(null)
    clearVideo()
  }

  // ---- render --------------------------------------------------------------------------------

  return (
    <div className="flex min-h-screen flex-col">
      <Header step={STEP[phase]} health={health} healthError={healthError} />

      <main className="mx-auto w-full max-w-6xl flex-1 px-4 py-8 sm:px-6 sm:py-12">
        <ServerBanner health={health} healthError={healthError} onRetry={() => void checkHealth()} />

        {phase === 'select' && (
          <UploadDropzone health={health} disabled={!health?.ffmpegAvailable} error={error} onFile={(f) => void handleFile(f)} />
        )}

        {phase === 'uploading' && file && upload && <UploadingCard file={file} progress={upload} onCancel={() => abortUpload.current?.()} />}

        {phase === 'review' && video && target && custom && (
          <div className="space-y-6">
            <div className="flex flex-wrap items-center justify-between gap-3">
              <div>
                <h1 className="text-2xl font-semibold tracking-tight">Can it be compressed?</h1>
                <p className="text-sm text-zinc-500 dark:text-zinc-400">Here&apos;s what we found, and your options for making it smaller.</p>
              </div>
              <Button variant="ghost" onClick={startOver}>
                <RefreshIcon size={16} /> Use a different video
              </Button>
            </div>

            {error && <Notice tone="error">{error}</Notice>}

            <div className="grid items-start gap-6 lg:grid-cols-[minmax(0,5fr)_minmax(0,7fr)]">
              <div className="lg:sticky lg:top-6">
                <VideoOverview video={video} previewUrl={previewUrl} />
              </div>
              <div className="space-y-6">
                <VerdictCard video={video} />
                <OptionsPanel
                  video={video}
                  health={health}
                  choice={choice}
                  onChoose={choose}
                  target={target}
                  onTargetChange={setTarget}
                  targetEstimate={targetEstimate}
                  custom={custom}
                  onCustomChange={setCustom}
                  customEstimate={customEstimate}
                />
              </div>
            </div>
          </div>
        )}

        {phase === 'compressing' && job && (
          <JobProgress
            job={job}
            canceling={canceling}
            onCancel={() => void cancelJob()}
            onBack={backToOptions}
            outputName={outputName ?? stemOf(job.fileName)}
            onOutputNameChange={setOutputName}
          />
        )}

        {phase === 'done' && job && (
          <ResultPanel
            job={job}
            outputName={outputName ?? stemOf(job.fileName)}
            onOutputNameChange={setOutputName}
            onTryAgain={backToOptions}
            onNewVideo={startOver}
          />
        )}
      </main>

      {phase === 'review' && video && selection && (
        <CompressBar
          name={selection.name}
          summary={selection.summary}
          original={video.sizeBytes}
          estimate={selection.estimate}
          estimating={selection.estimating}
          disabled={!selection.usable || !selection.settings}
          busy={starting}
          onCompress={() => void compress()}
        />
      )}
    </div>
  )
}

function ServerBanner({ health, healthError, onRetry }: { health: Health | null; healthError: string | null; onRetry: () => void }) {
  if (healthError) {
    return (
      <Notice tone="error" className="mx-auto mb-8 max-w-2xl">
        <p className="font-medium">Can&apos;t reach the API server.</p>
        <p className="mt-1">
          Start the backend with <code className="rounded bg-black/5 px-1 dark:bg-white/10">dotnet run --project backend/VideoCompression.Api</code>.
          This page reconnects automatically.
        </p>
        <Button variant="secondary" size="sm" className="mt-3" onClick={onRetry}>
          Check again
        </Button>
      </Notice>
    )
  }
  if (health && !health.ffmpegAvailable) {
    return (
      <Notice tone="warning" className="mx-auto mb-8 max-w-2xl">
        <p className="font-medium">FFmpeg isn&apos;t installed on the server.</p>
        <p className="mt-1">{health.message}</p>
        <Button variant="secondary" size="sm" className="mt-3" onClick={onRetry}>
          Check again
        </Button>
      </Notice>
    )
  }
  return null
}
