import { useRef, useState, type DragEvent, type ReactNode } from 'react'
import type { UploadProgress } from '../api/client'
import type { Health } from '../api/types'
import { formatBytes } from '../lib/format'
import { FilmIcon, GaugeIcon, ShieldIcon, SlidersIcon, UploadIcon } from './icons'
import { Button, Card, Notice, ProgressBar, cx } from './ui'

export function UploadDropzone({
  health,
  disabled,
  error,
  onFile,
}: {
  health: Health | null
  disabled: boolean
  error: string | null
  onFile: (file: File) => void
}) {
  const [dragging, setDragging] = useState(false)
  const inputRef = useRef<HTMLInputElement>(null)
  const accept = ['video/*', ...(health?.acceptedExtensions ?? [])].join(',')

  const open = () => {
    if (!disabled) inputRef.current?.click()
  }
  const onDrop = (event: DragEvent) => {
    event.preventDefault()
    setDragging(false)
    const file = event.dataTransfer.files?.[0]
    if (file && !disabled) onFile(file)
  }

  return (
    <div className="mx-auto max-w-2xl">
      <div className="mb-8 text-center">
        <h1 className="text-3xl font-semibold tracking-tight sm:text-4xl">Make your videos smaller</h1>
        <p className="mx-auto mt-3 max-w-lg text-zinc-600 dark:text-zinc-400">
          Upload a video and we&apos;ll check how much it can be compressed, then you choose the size and quality that suit you.
        </p>
      </div>

      <div
        role="button"
        tabIndex={disabled ? -1 : 0}
        aria-disabled={disabled}
        aria-label="Choose a video to upload"
        onClick={open}
        onKeyDown={(event) => {
          if (event.key === 'Enter' || event.key === ' ') {
            event.preventDefault()
            open()
          }
        }}
        onDragOver={(event) => {
          event.preventDefault()
          if (!disabled) setDragging(true)
        }}
        onDragLeave={() => setDragging(false)}
        onDrop={onDrop}
        className={cx(
          'group flex cursor-pointer flex-col items-center justify-center rounded-3xl border-2 border-dashed px-6 py-14 text-center transition sm:py-16',
          'focus-visible:outline-2 focus-visible:outline-offset-4 focus-visible:outline-indigo-600',
          dragging
            ? 'border-indigo-500 bg-indigo-50 dark:bg-indigo-500/10'
            : 'border-zinc-300 bg-white hover:border-indigo-400 hover:bg-indigo-50/40 dark:border-zinc-700 dark:bg-zinc-900 dark:hover:border-indigo-500/60 dark:hover:bg-indigo-500/5',
          disabled && 'cursor-not-allowed opacity-60 hover:border-zinc-300 hover:bg-white dark:hover:border-zinc-700 dark:hover:bg-zinc-900',
        )}
      >
        <span className="grid size-16 place-items-center rounded-2xl bg-indigo-600 text-white shadow-lg shadow-indigo-600/25 transition group-hover:scale-105">
          <UploadIcon size={28} />
        </span>
        <p className="mt-6 text-lg font-semibold">{dragging ? 'Drop it!' : 'Drop a video here'}</p>
        <p className="mt-1 text-sm text-zinc-500 dark:text-zinc-400">
          or <span className="font-medium text-indigo-600 dark:text-indigo-400">browse your files</span>
        </p>
        <p className="mt-4 text-xs text-zinc-400 dark:text-zinc-500">
          MP4, MOV, MKV, WebM, AVI and more{health ? ` · up to ${formatBytes(health.maxUploadBytes)}` : ''}
        </p>
      </div>
      <input
        ref={inputRef}
        type="file"
        accept={accept}
        className="hidden"
        onChange={(event) => {
          const file = event.target.files?.[0]
          event.target.value = ''
          if (file) onFile(file)
        }}
      />

      {error && (
        <Notice tone="error" className="mt-4">
          {error}
        </Notice>
      )}

      <div className="mt-10 grid gap-4 sm:grid-cols-3">
        <Feature icon={<GaugeIcon size={18} />} title="Real measurements">
          We test-encode samples of your video to predict the result before you commit.
        </Feature>
        <Feature icon={<SlidersIcon size={18} />} title="Your choice">
          Pick a preset, an exact target size, or fine-tune every setting.
        </Feature>
        <Feature icon={<ShieldIcon size={18} />} title="Private">
          Files stay on your own server and are deleted automatically.
        </Feature>
      </div>
    </div>
  )
}

function Feature({ icon, title, children }: { icon: ReactNode; title: string; children: ReactNode }) {
  return (
    <div className="rounded-2xl p-4">
      <span className="grid size-9 place-items-center rounded-xl bg-white text-indigo-600 shadow-sm ring-1 ring-zinc-200 dark:bg-zinc-900 dark:text-indigo-400 dark:ring-zinc-800">
        {icon}
      </span>
      <p className="mt-3 font-medium">{title}</p>
      <p className="mt-1 text-sm text-zinc-500 dark:text-zinc-400">{children}</p>
    </div>
  )
}

export function UploadingCard({ file, progress, onCancel }: { file: File; progress: UploadProgress; onCancel: () => void }) {
  const percent = progress.total > 0 ? (progress.loaded / progress.total) * 100 : 0
  return (
    <Card className="mx-auto max-w-xl p-6 sm:p-8">
      <div className="flex items-center gap-4">
        <span className="grid size-12 shrink-0 place-items-center rounded-xl bg-indigo-50 text-indigo-600 dark:bg-indigo-500/15 dark:text-indigo-300">
          <FilmIcon />
        </span>
        <div className="min-w-0 flex-1">
          <p className="truncate font-medium" title={file.name}>
            {file.name}
          </p>
          <p className="text-sm text-zinc-500 dark:text-zinc-400">{formatBytes(file.size)}</p>
        </div>
      </div>

      <div className="mt-6">
        <div className="mb-2 flex justify-between text-sm">
          <span className="font-medium">{progress.processing ? 'Reading video details…' : 'Uploading…'}</span>
          {!progress.processing && <span className="text-zinc-500 tabular-nums">{Math.floor(percent)}%</span>}
        </div>
        <ProgressBar value={progress.processing ? 100 : percent} striped={progress.processing} />
        <p className="mt-2 text-xs text-zinc-500 tabular-nums dark:text-zinc-400">
          {progress.processing
            ? 'Checking the codec, resolution and bitrate.'
            : `${formatBytes(progress.loaded)} of ${formatBytes(progress.total)}${
                progress.bytesPerSecond > 0 ? ` · ${formatBytes(progress.bytesPerSecond)}/s` : ''
              }`}
        </p>
      </div>

      <div className="mt-6 flex justify-end">
        <Button variant="ghost" onClick={onCancel}>
          Cancel
        </Button>
      </div>
    </Card>
  )
}
