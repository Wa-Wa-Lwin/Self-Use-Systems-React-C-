import type { SizeEstimate } from '../api/types'
import { formatBytes } from '../lib/format'
import { SavingsBadge } from './Estimate'
import { ZapIcon } from './icons'
import { Button, Spinner } from './ui'

/** Sticky footer on the review screen: what will happen, and the button that does it. */
export function CompressBar({
  name,
  summary,
  original,
  estimate,
  estimating,
  disabled,
  busy,
  onCompress,
}: {
  name: string
  summary: string | null
  original: number
  estimate: SizeEstimate | null
  estimating: boolean
  disabled: boolean
  busy: boolean
  onCompress: () => void
}) {
  return (
    <div className="sticky bottom-0 z-20 border-t border-zinc-200 bg-white/90 backdrop-blur dark:border-zinc-800 dark:bg-zinc-950/90">
      <div className="mx-auto flex max-w-6xl flex-wrap items-center justify-between gap-x-6 gap-y-3 px-4 py-3.5 sm:px-6">
        <div className="min-w-0 flex-1">
          <p className="text-sm font-semibold">{name}</p>
          <p className="truncate text-xs text-zinc-500 dark:text-zinc-400">{summary ?? 'Choose an option above'}</p>
        </div>
        <div className="flex items-center gap-4">
          <div className="hidden items-center gap-2 text-sm tabular-nums sm:flex">
            <span className="text-zinc-500 dark:text-zinc-400">{formatBytes(original)}</span>
            <span className="text-zinc-400">→</span>
            {estimate ? (
              <>
                <span className="font-semibold">≈ {formatBytes(estimate.bytes)}</span>
                <SavingsBadge percent={estimate.savingsPercent} />
              </>
            ) : (
              <span className="flex items-center gap-1.5 text-zinc-500">
                {estimating && <Spinner className="size-3.5" />}
                {estimating ? 'estimating…' : '—'}
              </span>
            )}
          </div>
          <Button size="lg" onClick={onCompress} disabled={disabled || busy}>
            {busy ? <Spinner /> : <ZapIcon size={18} />}
            Compress video
          </Button>
        </div>
      </div>
    </div>
  )
}
