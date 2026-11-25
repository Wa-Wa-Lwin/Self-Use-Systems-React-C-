import type { InsightTone, Verdict, VerdictLevel, Video } from '../api/types'
import { AlertIcon, CheckCircleIcon, GaugeIcon, InfoIcon, TrendingDownIcon } from './icons'
import { Card, ProgressBar, Spinner, cx } from './ui'

const levelStyles: Record<VerdictLevel, { panel: string; icon: string; bar: string }> = {
  high: {
    panel: 'bg-emerald-50 dark:bg-emerald-500/10',
    icon: 'bg-emerald-600 text-white',
    bar: 'bg-emerald-500',
  },
  good: {
    panel: 'bg-teal-50 dark:bg-teal-500/10',
    icon: 'bg-teal-600 text-white',
    bar: 'bg-teal-500',
  },
  low: {
    panel: 'bg-amber-50 dark:bg-amber-500/10',
    icon: 'bg-amber-500 text-white',
    bar: 'bg-amber-500',
  },
  minimal: {
    panel: 'bg-orange-50 dark:bg-orange-500/10',
    icon: 'bg-orange-500 text-white',
    bar: 'bg-orange-500',
  },
  none: {
    panel: 'bg-rose-50 dark:bg-rose-500/10',
    icon: 'bg-rose-600 text-white',
    bar: 'bg-rose-500',
  },
  unknown: {
    panel: 'bg-zinc-100 dark:bg-zinc-800/60',
    icon: 'bg-zinc-500 text-white',
    bar: 'bg-zinc-400',
  },
}

const toneIcon: Record<InsightTone, { Icon: typeof InfoIcon; className: string }> = {
  positive: { Icon: TrendingDownIcon, className: 'text-emerald-600 dark:text-emerald-400' },
  neutral: { Icon: InfoIcon, className: 'text-zinc-400 dark:text-zinc-500' },
  warning: { Icon: AlertIcon, className: 'text-amber-500' },
}

/** Answers "can it be compressed, and by how much?" plus the reasons why. */
export function VerdictCard({ video }: { video: Video }) {
  const { verdict, analysis } = video
  const recommended = video.presets.find((p) => p.recommended)

  return (
    <Card className="overflow-hidden">
      {verdict ? (
        <VerdictPanel verdict={verdict} recommendedName={recommended?.name ?? 'Recommended'} stillMeasuring={!analysis.done} />
      ) : (
        <div className="p-5 sm:p-6">
          <div className="flex items-center gap-3">
            <span className="grid size-10 place-items-center rounded-xl bg-indigo-50 text-indigo-600 dark:bg-indigo-500/15 dark:text-indigo-300">
              <Spinner className="size-5" />
            </span>
            <div>
              <p className="font-semibold">Checking how much this video can be compressed…</p>
              <p className="text-sm text-zinc-500 dark:text-zinc-400">Test-encoding a few seconds with each option.</p>
            </div>
          </div>
          <ProgressBar value={(analysis.completed / Math.max(1, analysis.total)) * 100 + 8} striped className="mt-4" />
        </div>
      )}

      {video.insights.length > 0 && (
        <ul className="divide-y divide-zinc-100 border-t border-zinc-100 dark:divide-zinc-800 dark:border-zinc-800">
          {video.insights.map((insight) => {
            const { Icon, className } = toneIcon[insight.tone]
            return (
              <li key={insight.title} className="flex gap-3 px-5 py-3 sm:px-6">
                <Icon size={18} className={cx('mt-0.5 shrink-0', className)} />
                <div className="text-sm">
                  <p className="font-medium">{insight.title}</p>
                  <p className="text-zinc-500 dark:text-zinc-400">{insight.detail}</p>
                </div>
              </li>
            )
          })}
        </ul>
      )}
    </Card>
  )
}

function VerdictPanel({ verdict, recommendedName, stillMeasuring }: { verdict: Verdict; recommendedName: string; stillMeasuring: boolean }) {
  const style = levelStyles[verdict.level]
  const Icon = verdict.level === 'high' || verdict.level === 'good' ? CheckCircleIcon : verdict.level === 'unknown' ? InfoIcon : GaugeIcon

  return (
    <div className={cx('p-5 sm:p-6', style.panel)}>
      <div className="flex items-start gap-3">
        <span className={cx('grid size-10 shrink-0 place-items-center rounded-xl', style.icon)}>
          <Icon size={20} />
        </span>
        <div className="min-w-0">
          <p className="text-xs font-medium tracking-wide text-zinc-500 uppercase dark:text-zinc-400">Compressibility</p>
          <h2 className="text-xl font-semibold tracking-tight">{verdict.headline}</h2>
          <p className="mt-1 text-sm text-zinc-700 dark:text-zinc-300">{verdict.summary}</p>
        </div>
      </div>

      {verdict.level !== 'unknown' && (
        <div className="mt-5 space-y-3">
          <SavingsRow label={`${recommendedName} (recommended)`} percent={verdict.recommendedSavingsPercent} bar={style.bar} />
          {verdict.bestSavingsPercent > verdict.recommendedSavingsPercent + 1 && (
            <SavingsRow label="Smallest option" percent={verdict.bestSavingsPercent} bar={style.bar} />
          )}
        </div>
      )}

      {stillMeasuring && (
        <p className="mt-4 flex items-center gap-2 text-xs text-zinc-500 dark:text-zinc-400">
          <Spinner className="size-3" /> Still measuring the other options…
        </p>
      )}
    </div>
  )
}

function SavingsRow({ label, percent, bar }: { label: string; percent: number; bar: string }) {
  const value = Math.max(0, Math.min(100, percent))
  return (
    <div>
      <div className="mb-1 flex justify-between text-sm">
        <span className="text-zinc-700 dark:text-zinc-300">{label}</span>
        <span className="font-semibold tabular-nums">{percent > 0 ? `${Math.round(percent)}% smaller` : 'No savings'}</span>
      </div>
      <div className="h-2 overflow-hidden rounded-full bg-white/80 ring-1 ring-black/5 dark:bg-zinc-950/40 dark:ring-white/5">
        <div className={cx('h-full rounded-full transition-[width] duration-700', bar)} style={{ width: `${value}%` }} />
      </div>
    </div>
  )
}
