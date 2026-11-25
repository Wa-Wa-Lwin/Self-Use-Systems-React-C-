import type { QualityHint, SizeEstimate } from '../api/types'
import { formatBytes } from '../lib/format'
import type { EstimateState } from '../lib/hooks'
import { Badge, Notice, Spinner, cx } from './ui'

/** "−68%" in green, or a red "+12%" when the result would be bigger than the original. */
export function SavingsBadge({ percent, large = false }: { percent: number; large?: boolean }) {
  const size = large ? 'text-sm px-2.5 py-1' : ''
  if (percent >= 1) return <Badge tone="emerald" className={size}>−{Math.round(percent)}%</Badge>
  if (percent > -1)
    return (
      <Badge tone="zinc" className={size}>
        No change
      </Badge>
    )
  return (
    <Badge tone="rose" className={size}>
      +{Math.round(-percent)}% larger
    </Badge>
  )
}

/** Bar showing the estimated size as a share of the original. */
export function SizeBar({ bytes, original, className }: { bytes: number; original: number; className?: string }) {
  const ratio = original > 0 ? bytes / original : 1
  return (
    <div className={cx('h-1.5 overflow-hidden rounded-full bg-zinc-200 dark:bg-zinc-800', className)}>
      <div
        className={cx('h-full rounded-full transition-[width] duration-500', ratio >= 1 ? 'bg-rose-500' : 'bg-emerald-500')}
        style={{ width: `${Math.max(2, Math.min(100, ratio * 100))}%` }}
      />
    </div>
  )
}

export function rangeText(estimate: SizeEstimate): string {
  if (estimate.method === 'exact') return 'Measured on the whole video'
  if (estimate.method === 'target') return `Within ${formatBytes(estimate.lowBytes)}–${formatBytes(estimate.highBytes)}`
  return `Likely ${formatBytes(estimate.lowBytes)}–${formatBytes(estimate.highBytes)}`
}

const qualityStyles: Record<QualityHint, { tone: 'emerald' | 'amber' | 'rose'; label: string }> = {
  excellent: { tone: 'emerald', label: 'Excellent quality' },
  good: { tone: 'emerald', label: 'Good quality' },
  fair: { tone: 'amber', label: 'Fair quality' },
  poor: { tone: 'rose', label: 'Low quality' },
}

/** Result box for the target-size and custom options (which are estimated on demand). */
export function EstimatePanel({ state, original, waitingText }: { state: EstimateState; original: number; waitingText: string }) {
  const { loading, result, error } = state

  if (error) return <Notice tone="error">{error}</Notice>
  if (!result) {
    return (
      <div className="flex items-center gap-2 rounded-xl bg-zinc-50 p-4 text-sm text-zinc-500 dark:bg-zinc-800/50 dark:text-zinc-400">
        <Spinner /> {waitingText}
      </div>
    )
  }
  if (!result.valid || !result.estimate) {
    return (
      <div className={cx(loading && 'opacity-60')}>
        <Notice tone="warning">{result.error ?? 'These settings cannot be used.'}</Notice>
      </div>
    )
  }

  const { estimate, plan } = result
  return (
    <div className={cx('rounded-xl bg-zinc-50 p-4 transition-opacity dark:bg-zinc-800/50', loading && 'opacity-60')} aria-busy={loading}>
      <div className="flex flex-wrap items-end justify-between gap-2">
        <div>
          <p className="text-xs font-medium tracking-wide text-zinc-500 uppercase dark:text-zinc-400">Estimated result</p>
          <p className="text-2xl font-semibold tabular-nums">≈ {formatBytes(estimate.bytes)}</p>
        </div>
        <div className="flex items-center gap-2">
          {plan?.qualityHint && <Badge tone={qualityStyles[plan.qualityHint].tone}>{qualityStyles[plan.qualityHint].label}</Badge>}
          <SavingsBadge percent={estimate.savingsPercent} large />
        </div>
      </div>
      <SizeBar bytes={estimate.bytes} original={original} className="mt-3" />
      <p className="mt-2 flex items-center gap-2 text-xs text-zinc-500 dark:text-zinc-400">
        {loading && <Spinner className="size-3" />}
        {loading ? 'Updating…' : `${rangeText(estimate)} · ${plan?.summary ?? ''}`}
      </p>
      {plan?.warnings.map((warning) => (
        <Notice key={warning} tone="warning" className="mt-3">
          {warning}
        </Notice>
      ))}
    </div>
  )
}
