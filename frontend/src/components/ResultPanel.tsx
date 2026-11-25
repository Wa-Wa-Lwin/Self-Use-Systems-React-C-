import { useRef, useState } from 'react'
import type { Job } from '../api/types'
import { formatBytes, formatEta } from '../lib/format'
import { outputFileName, stemOf } from '../lib/filename'
import { AlertIcon, CheckCircleIcon, DownloadIcon, RefreshIcon, SlidersIcon } from './icons'
import { OutputNameField } from './OutputNameField'
import { Button, Card, buttonClass, cx } from './ui'

export function ResultPanel({
  job,
  outputName,
  onOutputNameChange,
  onTryAgain,
  onNewVideo,
}: {
  job: Job
  outputName: string
  onOutputNameChange: (name: string) => void
  onTryAgain: () => void
  onNewVideo: () => void
}) {
  const [previewFailed, setPreviewFailed] = useState(false)
  const downloadRef = useRef<HTMLAnchorElement>(null)
  const suggestedStem = stemOf(job.fileName)
  const fileName = outputFileName(outputName, suggestedStem)
  // The server sets Content-Disposition from ?name=, which browsers prefer over the download attribute.
  const downloadUrl = job.downloadUrl ? `${job.downloadUrl}&name=${encodeURIComponent(fileName)}` : null
  const output = job.outputBytes ?? 0
  const smaller = output < job.originalBytes
  const saved = job.originalBytes - output
  const percent = job.savingsPercent ?? 0

  return (
    <Card className="mx-auto max-w-3xl overflow-hidden">
      <div className={cx('px-6 py-8 text-center sm:px-8', smaller ? 'bg-emerald-50 dark:bg-emerald-500/10' : 'bg-amber-50 dark:bg-amber-500/10')}>
        <span
          className={cx(
            'mx-auto grid size-14 place-items-center rounded-2xl text-white shadow-lg',
            smaller ? 'bg-emerald-600 shadow-emerald-600/25' : 'bg-amber-500 shadow-amber-500/25',
          )}
        >
          {smaller ? <CheckCircleIcon size={28} /> : <AlertIcon size={28} />}
        </span>
        <h1 className="mt-4 text-2xl font-semibold tracking-tight sm:text-3xl">
          {smaller ? `Your video is ${Math.round(percent)}% smaller` : "The compressed file isn't smaller"}
        </h1>
        <p className="mt-2 text-zinc-600 dark:text-zinc-300">
          {smaller
            ? `Saved ${formatBytes(saved)} · finished in ${formatEta(job.elapsedSeconds)}`
            : 'This video was already efficiently encoded. Keep the original, or try a stronger option.'}
        </p>
      </div>

      <div className="space-y-6 p-6 sm:p-8">
        <div className="space-y-3">
          <SizeRow label="Original" bytes={job.originalBytes} max={Math.max(job.originalBytes, output)} className="bg-zinc-400 dark:bg-zinc-600" />
          <SizeRow
            label="Compressed"
            bytes={output}
            max={Math.max(job.originalBytes, output)}
            className={smaller ? 'bg-emerald-500' : 'bg-amber-500'}
          />
        </div>

        {job.previewUrl && (
          <div className="overflow-hidden rounded-xl bg-zinc-950">
            {previewFailed ? (
              <p className="p-6 text-center text-sm text-zinc-400">
                Your browser can&apos;t preview {job.plan.codec} video, but the file plays in most video players and on modern devices.
              </p>
            ) : (
              <video
                src={job.previewUrl}
                controls
                playsInline
                preload="metadata"
                className="aspect-video w-full object-contain"
                onError={() => setPreviewFailed(true)}
              />
            )}
          </div>
        )}

        <p className="text-sm text-zinc-500 dark:text-zinc-400">{job.plan.summary}</p>

        <OutputNameField
          id="result-output-name"
          value={outputName}
          suggestedStem={suggestedStem}
          onChange={onOutputNameChange}
          onEnter={() => downloadRef.current?.click()}
        />

        <div className="flex flex-wrap gap-3">
          {downloadUrl && (
            <a ref={downloadRef} href={downloadUrl} download={fileName} className={buttonClass('primary', 'lg')}>
              <DownloadIcon size={18} /> Download
            </a>
          )}
          <Button variant="secondary" size="lg" onClick={onTryAgain}>
            <SlidersIcon size={18} /> Try other settings
          </Button>
          <Button variant="ghost" size="lg" onClick={onNewVideo}>
            <RefreshIcon size={18} /> Compress another video
          </Button>
        </div>
      </div>
    </Card>
  )
}

function SizeRow({ label, bytes, max, className }: { label: string; bytes: number; max: number; className: string }) {
  return (
    <div className="grid grid-cols-[6rem_1fr_5rem] items-center gap-3 text-sm">
      <span className="text-zinc-500 dark:text-zinc-400">{label}</span>
      <div className="h-3 overflow-hidden rounded-full bg-zinc-100 dark:bg-zinc-800">
        <div className={cx('h-full rounded-full transition-[width] duration-700', className)} style={{ width: `${max > 0 ? Math.max(1, (bytes / max) * 100) : 0}%` }} />
      </div>
      <span className="text-right font-semibold tabular-nums">{formatBytes(bytes)}</span>
    </div>
  )
}
