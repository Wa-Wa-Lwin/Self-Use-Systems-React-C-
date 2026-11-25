import type { ButtonHTMLAttributes, HTMLAttributes, ReactNode } from 'react'
import { AlertIcon, InfoIcon } from './icons'

export function cx(...classes: Array<string | false | null | undefined>): string {
  return classes.filter(Boolean).join(' ')
}

type Variant = 'primary' | 'secondary' | 'ghost'
type Size = 'sm' | 'md' | 'lg'

export function buttonClass(variant: Variant = 'primary', size: Size = 'md'): string {
  return cx(
    'inline-flex items-center justify-center gap-2 rounded-xl font-medium transition',
    'focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-indigo-600',
    'disabled:cursor-not-allowed disabled:opacity-50',
    size === 'lg' && 'px-5 py-3 text-base',
    size === 'md' && 'px-4 py-2.5 text-sm',
    size === 'sm' && 'px-3 py-1.5 text-sm',
    variant === 'primary' && 'bg-indigo-600 text-white shadow-sm hover:bg-indigo-500 disabled:hover:bg-indigo-600',
    variant === 'secondary' &&
      'bg-white text-zinc-900 shadow-sm ring-1 ring-zinc-300 ring-inset hover:bg-zinc-50 dark:bg-zinc-800 dark:text-zinc-100 dark:ring-zinc-700 dark:hover:bg-zinc-700',
    variant === 'ghost' &&
      'text-zinc-600 hover:bg-zinc-100 hover:text-zinc-900 dark:text-zinc-400 dark:hover:bg-zinc-800 dark:hover:text-zinc-100',
  )
}

export function Button({
  variant,
  size,
  className,
  type = 'button',
  ...props
}: ButtonHTMLAttributes<HTMLButtonElement> & { variant?: Variant; size?: Size }) {
  return <button type={type} className={cx(buttonClass(variant, size), className)} {...props} />
}

export function Card({ className, ...props }: HTMLAttributes<HTMLDivElement>) {
  return (
    <div
      className={cx('rounded-2xl border border-zinc-200 bg-white shadow-sm dark:border-zinc-800 dark:bg-zinc-900', className)}
      {...props}
    />
  )
}

type Tone = 'indigo' | 'emerald' | 'amber' | 'rose' | 'zinc'

const badgeTones: Record<Tone, string> = {
  indigo: 'bg-indigo-100 text-indigo-700 dark:bg-indigo-500/15 dark:text-indigo-300',
  emerald: 'bg-emerald-100 text-emerald-700 dark:bg-emerald-500/15 dark:text-emerald-300',
  amber: 'bg-amber-100 text-amber-800 dark:bg-amber-500/15 dark:text-amber-300',
  rose: 'bg-rose-100 text-rose-700 dark:bg-rose-500/15 dark:text-rose-300',
  zinc: 'bg-zinc-100 text-zinc-600 dark:bg-zinc-800 dark:text-zinc-300',
}

export function Badge({ tone = 'zinc', className, children }: { tone?: Tone; className?: string; children: ReactNode }) {
  return (
    <span className={cx('inline-flex items-center rounded-full px-2 py-0.5 text-xs font-medium whitespace-nowrap', badgeTones[tone], className)}>
      {children}
    </span>
  )
}

export function Chip({ children }: { children: ReactNode }) {
  return (
    <span className="inline-flex items-center rounded-md bg-zinc-100 px-1.5 py-0.5 text-xs text-zinc-600 dark:bg-zinc-800 dark:text-zinc-300">
      {children}
    </span>
  )
}

export function Spinner({ className }: { className?: string }) {
  return (
    <svg className={cx('size-4 animate-spin', className)} viewBox="0 0 24 24" fill="none" aria-hidden="true">
      <circle cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="3" className="opacity-25" />
      <path d="M22 12a10 10 0 0 0-10-10" stroke="currentColor" strokeWidth="3" strokeLinecap="round" />
    </svg>
  )
}

export function ProgressBar({
  value,
  striped = false,
  tone = 'indigo',
  className,
}: {
  value: number
  striped?: boolean
  tone?: 'indigo' | 'emerald' | 'rose'
  className?: string
}) {
  const fill = tone === 'emerald' ? 'bg-emerald-500' : tone === 'rose' ? 'bg-rose-500' : 'bg-indigo-600'
  return (
    <div
      className={cx('h-2.5 overflow-hidden rounded-full bg-zinc-200 dark:bg-zinc-800', className)}
      role="progressbar"
      aria-valuemin={0}
      aria-valuemax={100}
      aria-valuenow={Math.round(value)}
    >
      <div
        className={cx('h-full rounded-full transition-[width] duration-500 ease-out', fill, striped && 'progress-stripes')}
        style={{ width: `${Math.min(100, Math.max(0, value))}%` }}
      />
    </div>
  )
}

export function Notice({
  tone = 'info',
  className,
  children,
}: {
  tone?: 'info' | 'warning' | 'error'
  className?: string
  children: ReactNode
}) {
  const styles = {
    info: 'bg-zinc-50 text-zinc-700 ring-zinc-200 dark:bg-zinc-800/60 dark:text-zinc-300 dark:ring-zinc-700',
    warning: 'bg-amber-50 text-amber-900 ring-amber-200 dark:bg-amber-500/10 dark:text-amber-200 dark:ring-amber-500/30',
    error: 'bg-rose-50 text-rose-900 ring-rose-200 dark:bg-rose-500/10 dark:text-rose-200 dark:ring-rose-500/30',
  }[tone]
  const Icon = tone === 'info' ? InfoIcon : AlertIcon
  return (
    <div className={cx('flex gap-2.5 rounded-xl p-3 text-sm ring-1 ring-inset', styles, className)} role={tone === 'error' ? 'alert' : undefined}>
      <Icon size={18} className="mt-px shrink-0" />
      <div className="min-w-0">{children}</div>
    </div>
  )
}

export function Field({ label, htmlFor, children }: { label: ReactNode; htmlFor?: string; children: ReactNode }) {
  return (
    <div>
      <label htmlFor={htmlFor} className="mb-1.5 block text-sm font-medium text-zinc-800 dark:text-zinc-200">
        {label}
      </label>
      {children}
    </div>
  )
}

export const inputClass =
  'block w-full rounded-xl border-0 bg-white px-3 py-2 text-sm text-zinc-900 shadow-sm ring-1 ring-zinc-300 ring-inset ' +
  'focus:ring-2 focus:ring-indigo-600 focus:outline-none disabled:opacity-50 dark:bg-zinc-800 dark:text-zinc-100 dark:ring-zinc-700'

export function RadioDot({ checked }: { checked: boolean }) {
  return (
    <span
      className={cx(
        'grid size-5 shrink-0 place-items-center rounded-full border-2 transition',
        checked ? 'border-indigo-600 dark:border-indigo-400' : 'border-zinc-300 dark:border-zinc-600',
      )}
      aria-hidden="true"
    >
      <span className={cx('size-2.5 rounded-full bg-indigo-600 transition dark:bg-indigo-400', checked ? 'scale-100' : 'scale-0')} />
    </span>
  )
}

export function Segmented<T extends string>({
  value,
  options,
  onChange,
  label,
}: {
  value: T
  options: ReadonlyArray<{ value: T; label: string; hint?: string; disabled?: boolean }>
  onChange: (value: T) => void
  label: string
}) {
  return (
    <div role="radiogroup" aria-label={label} className="grid auto-cols-fr grid-flow-col gap-1 rounded-xl bg-zinc-100 p-1 dark:bg-zinc-800">
      {options.map((option) => (
        <button
          key={option.value}
          type="button"
          role="radio"
          aria-checked={value === option.value}
          disabled={option.disabled}
          title={option.hint}
          onClick={() => onChange(option.value)}
          className={cx(
            'rounded-lg px-3 py-1.5 text-sm font-medium transition disabled:cursor-not-allowed disabled:opacity-40',
            value === option.value
              ? 'bg-white text-zinc-900 shadow-sm dark:bg-zinc-950 dark:text-zinc-100'
              : 'text-zinc-600 hover:text-zinc-900 dark:text-zinc-400 dark:hover:text-zinc-100',
          )}
        >
          {option.label}
        </button>
      ))}
    </div>
  )
}
