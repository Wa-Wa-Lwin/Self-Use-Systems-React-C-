import type { Health } from '../api/types'
import { CheckIcon, LogoIcon } from './icons'
import { cx } from './ui'

const STEPS = ['Upload', 'Choose', 'Compress', 'Download'] as const

export function Header({ step, health, healthError }: { step: number; health: Health | null; healthError: string | null }) {
  return (
    <header className="border-b border-zinc-200/80 bg-white/80 backdrop-blur dark:border-zinc-800 dark:bg-zinc-950/80">
      <div className="mx-auto flex max-w-6xl items-center justify-between gap-4 px-4 py-3.5 sm:px-6">
        <div className="flex items-center gap-2.5">
          <span className="grid size-9 place-items-center rounded-xl bg-indigo-600 text-white shadow-sm shadow-indigo-600/30">
            <LogoIcon size={18} strokeWidth={2.4} />
          </span>
          <div className="leading-tight">
            <p className="font-semibold">Video Compressor</p>
            <p className="text-xs text-zinc-500 dark:text-zinc-400">Smaller files, same video</p>
          </div>
        </div>

        <ol className="hidden items-center gap-1 text-sm md:flex" aria-label="Progress">
          {STEPS.map((label, index) => (
            <li key={label} className="flex items-center gap-1" aria-current={index === step ? 'step' : undefined}>
              <span
                className={cx(
                  'flex items-center gap-2 rounded-full px-3 py-1',
                  index === step && 'bg-indigo-50 font-medium text-indigo-700 dark:bg-indigo-500/15 dark:text-indigo-300',
                  index < step && 'text-zinc-700 dark:text-zinc-300',
                  index > step && 'text-zinc-400 dark:text-zinc-500',
                )}
              >
                <span
                  className={cx(
                    'grid size-5 place-items-center rounded-full text-[11px] font-semibold',
                    index <= step ? 'bg-indigo-600 text-white' : 'bg-zinc-200 text-zinc-500 dark:bg-zinc-800 dark:text-zinc-400',
                  )}
                >
                  {index < step ? <CheckIcon size={12} strokeWidth={3} /> : index + 1}
                </span>
                {label}
              </span>
              {index < STEPS.length - 1 && <span className="h-px w-4 bg-zinc-200 dark:bg-zinc-800" />}
            </li>
          ))}
        </ol>

        <ServerStatus health={health} healthError={healthError} />
      </div>
    </header>
  )
}

function ServerStatus({ health, healthError }: { health: Health | null; healthError: string | null }) {
  const [dot, text] = healthError
    ? ['bg-rose-500', 'Server offline']
    : !health
      ? ['bg-zinc-400 animate-pulse', 'Connecting…']
      : health.ffmpegAvailable
        ? ['bg-emerald-500', `FFmpeg ${health.ffmpegVersion?.split('-')[0] ?? ''}`.trim()]
        : ['bg-amber-500', 'FFmpeg missing']

  return (
    <span className="flex items-center gap-2 rounded-full bg-zinc-100 px-3 py-1 text-xs font-medium text-zinc-600 dark:bg-zinc-900 dark:text-zinc-400">
      <span className={cx('size-2 rounded-full', dot)} />
      {text}
    </span>
  )
}
