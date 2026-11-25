import type { Job } from '../api/types'
import { formatEta } from '../lib/format'
import { stemOf } from '../lib/filename'
import { AlertIcon, ArrowLeftIcon } from './icons'
import { OutputNameField } from './OutputNameField'
import { Button, Card, Notice, ProgressBar, Spinner } from './ui'

export function JobProgress({
  job,
  onCancel,
  onBack,
  canceling,
  outputName,
  onOutputNameChange,
}: {
  job: Job
  onCancel: () => void
  onBack: () => void
  canceling: boolean
  outputName: string
  onOutputNameChange: (name: string) => void
}) {
  const failed = job.status === 'failed'

  return (
    <Card className="mx-auto max-w-2xl p-6 sm:p-8">
      <div className="flex items-center gap-3">
        <span
          className={
            failed
              ? 'grid size-11 place-items-center rounded-xl bg-rose-100 text-rose-600 dark:bg-rose-500/15 dark:text-rose-300'
              : 'grid size-11 place-items-center rounded-xl bg-indigo-50 text-indigo-600 dark:bg-indigo-500/15 dark:text-indigo-300'
          }
        >
          {failed ? <AlertIcon /> : <Spinner className="size-5" />}
        </span>
        <div className="min-w-0">
          <h1 className="text-xl font-semibold tracking-tight">{failed ? 'Compression failed' : 'Compressing your video'}</h1>
          <p className="truncate text-sm text-zinc-500 dark:text-zinc-400">{job.plan.summary}</p>
        </div>
      </div>

      {failed ? (
        <>
          <Notice tone="error" className="mt-6">
            {job.error ?? 'FFmpeg stopped unexpectedly.'}
          </Notice>
          <div className="mt-6 flex justify-end">
            <Button variant="secondary" onClick={onBack}>
              <ArrowLeftIcon size={16} /> Back to options
            </Button>
          </div>
        </>
      ) : (
        <>
          <div className="mt-8 flex items-end justify-between gap-4">
            <span className="text-5xl font-semibold tracking-tight tabular-nums">
              {Math.floor(job.progress)}
              <span className="text-2xl text-zinc-400">%</span>
            </span>
            <span className="pb-1.5 text-right text-sm text-zinc-500 dark:text-zinc-400">{job.status === 'queued' ? 'Waiting for a free slot…' : job.stage}</span>
          </div>
          <ProgressBar value={job.progress} striped className="mt-3 h-3" />

          <dl className="mt-6 grid grid-cols-3 gap-4 text-sm">
            <Stat label="Speed" value={job.speed ? `${job.speed.toFixed(job.speed >= 10 ? 0 : 1)}× real-time` : '—'} />
            <Stat label="Elapsed" value={job.elapsedSeconds > 0 ? formatEta(job.elapsedSeconds) : '—'} />
            <Stat label="Remaining" value={job.etaSeconds != null ? `~${formatEta(job.etaSeconds)}` : 'Estimating…'} />
          </dl>

          <div className="mt-6 rounded-xl border border-dashed border-zinc-200 p-4 dark:border-zinc-700">
            <p className="mb-3 text-sm text-zinc-600 dark:text-zinc-400">While you wait, choose a name for the compressed file.</p>
            <OutputNameField id="job-output-name" value={outputName} suggestedStem={stemOf(job.fileName)} onChange={onOutputNameChange} />
          </div>

          <div className="mt-6 flex items-center justify-between gap-4">
            <p className="text-xs text-zinc-500 dark:text-zinc-400">You can keep this tab open; the work happens on the server.</p>
            <Button variant="ghost" onClick={onCancel} disabled={canceling}>
              {canceling && <Spinner />} Cancel
            </Button>
          </div>
        </>
      )}
    </Card>
  )
}

function Stat({ label, value }: { label: string; value: string }) {
  return (
    <div className="rounded-xl bg-zinc-50 p-3 dark:bg-zinc-800/50">
      <dt className="text-xs text-zinc-500 dark:text-zinc-400">{label}</dt>
      <dd className="mt-0.5 font-medium tabular-nums">{value}</dd>
    </div>
  )
}
