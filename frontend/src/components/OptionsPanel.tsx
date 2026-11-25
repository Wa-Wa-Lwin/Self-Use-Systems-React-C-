import { useEffect, useState, type ReactNode } from 'react'
import type { Choice, CompressionSettings, Health, Preset, Video } from '../api/types'
import { clamp, formatBytes, formatFps, resolutionLabel, roundNiceMb } from '../lib/format'
import type { EstimateState } from '../lib/hooks'
import { AUDIO_CHOICES, SPEED_CHOICES, fpsChoices, qualityLabel, resolutionChoices, shortSide } from '../lib/options'
import { EstimatePanel, SavingsBadge, SizeBar, rangeText } from './Estimate'
import { SlidersIcon, TargetIcon } from './icons'
import { Badge, Chip, Field, RadioDot, Segmented, Spinner, cx, inputClass } from './ui'

const PRESET_ORDER = ['light', 'balanced', 'strong', 'maximum']

interface OptionsPanelProps {
  video: Video
  health: Health | null
  choice: Choice
  onChoose: (choice: Choice) => void
  target: CompressionSettings
  onTargetChange: (settings: CompressionSettings) => void
  targetEstimate: EstimateState
  custom: CompressionSettings
  onCustomChange: (settings: CompressionSettings) => void
  customEstimate: EstimateState
}

export function OptionsPanel(props: OptionsPanelProps) {
  const { video, choice, onChoose } = props
  const presets = [...video.presets].sort((a, b) => PRESET_ORDER.indexOf(a.id) - PRESET_ORDER.indexOf(b.id))

  return (
    <section aria-labelledby="options-heading">
      <div className="mb-3 flex items-end justify-between gap-2">
        <div>
          <h2 id="options-heading" className="text-lg font-semibold tracking-tight">
            Choose how to compress
          </h2>
          <p className="text-sm text-zinc-500 dark:text-zinc-400">Sizes are measured by test-encoding parts of your video.</p>
        </div>
      </div>

      <div role="radiogroup" aria-labelledby="options-heading" className="space-y-3">
        <div className="grid gap-3 sm:grid-cols-2">
          {presets.map((preset) => (
            <PresetCard
              key={preset.id}
              preset={preset}
              original={video.sizeBytes}
              selected={choice.kind === 'preset' && choice.id === preset.id}
              onSelect={() => onChoose({ kind: 'preset', id: preset.id })}
            />
          ))}
        </div>

        <ExpandableOption
          selected={choice.kind === 'target'}
          onSelect={() => onChoose({ kind: 'target' })}
          icon={<TargetIcon size={18} />}
          title="Target file size"
          subtitle="Hit an exact size, e.g. to fit an email or chat upload limit."
          aside={choice.kind !== 'target' && props.target.targetSizeMb ? <Chip>{props.target.targetSizeMb} MB</Chip> : null}
        >
          <TargetSizeControls {...props} />
        </ExpandableOption>

        <ExpandableOption
          selected={choice.kind === 'custom'}
          onSelect={() => onChoose({ kind: 'custom' })}
          icon={<SlidersIcon size={18} />}
          title="Custom"
          subtitle="Pick the format, quality, resolution, frame rate and audio yourself."
        >
          <CustomControls {...props} />
        </ExpandableOption>
      </div>
    </section>
  )
}

function PresetCard({ preset, original, selected, onSelect }: { preset: Preset; original: number; selected: boolean; onSelect: () => void }) {
  const unavailable = preset.status === 'unavailable'
  const plan = preset.plan

  return (
    <button
      type="button"
      role="radio"
      aria-checked={selected}
      disabled={unavailable}
      onClick={onSelect}
      title={preset.description}
      className={cx(
        'flex h-full flex-col rounded-2xl border p-4 text-left transition',
        'focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-indigo-600',
        selected
          ? 'border-indigo-500 bg-indigo-50/60 ring-1 ring-indigo-500 dark:border-indigo-400 dark:bg-indigo-500/10 dark:ring-indigo-400'
          : 'border-zinc-200 bg-white hover:border-zinc-300 hover:shadow-sm dark:border-zinc-800 dark:bg-zinc-900 dark:hover:border-zinc-700',
        unavailable && 'cursor-not-allowed opacity-50',
      )}
    >
      <div className="flex w-full items-start justify-between gap-2">
        <div className="min-w-0">
          <div className="flex flex-wrap items-center gap-2">
            <span className="font-semibold">{preset.name}</span>
            {preset.recommended && <Badge tone="indigo">Recommended</Badge>}
          </div>
          <p className="text-sm text-zinc-500 dark:text-zinc-400">{preset.tagline}</p>
        </div>
        <RadioDot checked={selected} />
      </div>

      {plan && (
        <div className="mt-3 flex flex-wrap gap-1.5">
          <Chip>{plan.codec}</Chip>
          <Chip>{resolutionLabel(plan.width, plan.height)}</Chip>
          <Chip>{formatFps(plan.fps)} fps</Chip>
          <Chip>{plan.audio}</Chip>
        </div>
      )}

      <div className="mt-auto w-full pt-4">
        <PresetEstimate preset={preset} original={original} />
      </div>
    </button>
  )
}

function PresetEstimate({ preset, original }: { preset: Preset; original: number }) {
  const { estimate, status } = preset

  if (status === 'unavailable') return <p className="text-sm text-zinc-500">{preset.error ?? 'Not available on this server.'}</p>
  if (status === 'failed') return <p className="text-sm text-amber-700 dark:text-amber-400">Couldn&apos;t estimate. You can still try it.</p>
  if (!estimate) {
    return (
      <div>
        <div className="flex items-center gap-2 text-sm text-zinc-500 dark:text-zinc-400">
          {status === 'measuring' ? <Spinner className="size-3.5" /> : <span className="size-3.5" />}
          {status === 'measuring' ? 'Measuring…' : 'Waiting to measure…'}
        </div>
        <div className="skeleton mt-2 h-1.5 rounded-full" />
      </div>
    )
  }

  return (
    <div>
      <div className="flex items-baseline justify-between gap-2">
        <span className="text-lg font-semibold tabular-nums">≈ {formatBytes(estimate.bytes)}</span>
        <SavingsBadge percent={estimate.savingsPercent} />
      </div>
      <SizeBar bytes={estimate.bytes} original={original} className="mt-2" />
      <p className="mt-1.5 text-xs text-zinc-500 dark:text-zinc-400">{rangeText(estimate)}</p>
    </div>
  )
}

function ExpandableOption({
  selected,
  onSelect,
  icon,
  title,
  subtitle,
  aside,
  children,
}: {
  selected: boolean
  onSelect: () => void
  icon: ReactNode
  title: string
  subtitle: string
  aside?: ReactNode
  children: ReactNode
}) {
  return (
    <div
      className={cx(
        'rounded-2xl border bg-white transition dark:bg-zinc-900',
        selected
          ? 'border-indigo-500 ring-1 ring-indigo-500 dark:border-indigo-400 dark:ring-indigo-400'
          : 'border-zinc-200 hover:border-zinc-300 dark:border-zinc-800 dark:hover:border-zinc-700',
      )}
    >
      <button
        type="button"
        role="radio"
        aria-checked={selected}
        aria-expanded={selected}
        onClick={onSelect}
        className="flex w-full items-center gap-3 rounded-2xl p-4 text-left focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-indigo-600"
      >
        <span
          className={cx(
            'grid size-10 shrink-0 place-items-center rounded-xl',
            selected ? 'bg-indigo-600 text-white' : 'bg-zinc-100 text-zinc-600 dark:bg-zinc-800 dark:text-zinc-300',
          )}
        >
          {icon}
        </span>
        <span className="min-w-0 flex-1">
          <span className="block font-semibold">{title}</span>
          <span className="block text-sm text-zinc-500 dark:text-zinc-400">{subtitle}</span>
        </span>
        {aside}
        <RadioDot checked={selected} />
      </button>
      {selected && <div className="border-t border-zinc-200 p-4 dark:border-zinc-800">{children}</div>}
    </div>
  )
}

function CodecPicker({ health, value, onChange }: { health: Health | null; value: CompressionSettings['codec']; onChange: (id: CompressionSettings['codec']) => void }) {
  return (
    <div className="grid gap-2 sm:grid-cols-3">
      {(health?.codecs ?? []).map((codec) => (
        <button
          key={codec.id}
          type="button"
          role="radio"
          aria-checked={value === codec.id}
          disabled={!codec.available}
          onClick={() => onChange(codec.id)}
          title={codec.description}
          className={cx(
            'rounded-xl border px-3 py-2 text-left transition disabled:cursor-not-allowed disabled:opacity-40',
            value === codec.id
              ? 'border-indigo-500 bg-indigo-50 dark:border-indigo-400 dark:bg-indigo-500/10'
              : 'border-zinc-200 hover:border-zinc-300 dark:border-zinc-700 dark:hover:border-zinc-600',
          )}
        >
          <span className="block text-sm font-medium">{codec.label}</span>
          <span className="block text-xs text-zinc-500 dark:text-zinc-400">{codec.available ? codec.compatibility : 'Not installed'}</span>
        </button>
      ))}
    </div>
  )
}

function AudioSelect({ video, value, onChange, id }: { video: Video; value: number; onChange: (kbps: number) => void; id: string }) {
  const hasAudio = video.media.audio.length > 0
  return (
    <select id={id} className={inputClass} value={hasAudio ? value : 0} disabled={!hasAudio} onChange={(e) => onChange(Number(e.target.value))}>
      {hasAudio ? (
        AUDIO_CHOICES.map((a) => (
          <option key={a.kbps} value={a.kbps}>
            {a.label}
          </option>
        ))
      ) : (
        <option value={0}>No audio track</option>
      )}
    </select>
  )
}

function TargetSizeControls({ video, health, target, onTargetChange, targetEstimate }: OptionsPanelProps) {
  const originalMb = video.sizeBytes / 1_000_000
  const minMb = Math.max(0.1, video.minTargetSizeMb)
  const maxMb = Math.max(minMb * 1.05, originalMb)
  const value = target.targetSizeMb ?? minMb
  const set = (patch: Partial<CompressionSettings>) => onTargetChange({ ...target, ...patch })

  // The slider is logarithmic so both "50 MB of a 2 GB file" and "1.5 MB" are easy to reach.
  const toPosition = (mb: number) => Math.round((1000 * Math.log(mb / minMb)) / Math.log(maxMb / minMb))
  const fromPosition = (position: number) => roundNiceMb(minMb * Math.pow(maxMb / minMb, position / 1000))

  // Keep what the user is typing (e.g. "2.") instead of reformatting it on every keystroke.
  const [text, setText] = useState(String(value))
  useEffect(() => {
    if (parseFloat(text) !== value) setText(String(value))
  }, [value]) // intentionally not depending on `text`

  const quick = [
    { label: '½ size', mb: originalMb / 2 },
    { label: '¼ size', mb: originalMb / 4 },
    { label: '10%', mb: originalMb / 10 },
    ...[10, 25, 50, 100, 500].filter((mb) => mb < originalMb * 0.9).map((mb) => ({ label: `${mb} MB`, mb })),
  ].filter((option) => option.mb >= minMb)

  const resolutionValue = target.autoResolution ? 'auto' : target.maxResolution ? String(target.maxResolution) : 'original'

  return (
    <div className="space-y-5">
      <Field label="Target size" htmlFor="target-mb">
        <div className="flex items-center gap-3">
          <div className="relative w-36 shrink-0">
            <input
              id="target-mb"
              type="number"
              inputMode="decimal"
              min={minMb}
              step="any"
              value={text}
              onChange={(e) => {
                setText(e.target.value)
                const mb = parseFloat(e.target.value)
                if (Number.isFinite(mb) && mb > 0) set({ targetSizeMb: mb })
              }}
              className={cx(inputClass, 'pr-11 tabular-nums')}
            />
            <span className="pointer-events-none absolute inset-y-0 right-3 flex items-center text-sm text-zinc-500">MB</span>
          </div>
          <input
            type="range"
            min={0}
            max={1000}
            value={clamp(toPosition(value), 0, 1000)}
            onChange={(e) => set({ targetSizeMb: fromPosition(Number(e.target.value)) })}
            aria-label="Target size"
            className="min-w-0 flex-1 accent-indigo-600"
          />
        </div>
        <div className="mt-3 flex flex-wrap gap-2">
          {quick.map((option) => (
            <button
              key={option.label}
              type="button"
              onClick={() => set({ targetSizeMb: roundNiceMb(option.mb) })}
              className="rounded-full bg-zinc-100 px-3 py-1 text-xs font-medium text-zinc-700 transition hover:bg-zinc-200 dark:bg-zinc-800 dark:text-zinc-300 dark:hover:bg-zinc-700"
            >
              {option.label}
            </button>
          ))}
        </div>
        <p className="mt-2 text-xs text-zinc-500 dark:text-zinc-400">
          Original: {formatBytes(video.sizeBytes)} · uses two-pass encoding to land on the size.
        </p>
      </Field>

      <Field label="Format">
        <CodecPicker health={health} value={target.codec} onChange={(codec) => set({ codec })} />
      </Field>

      <div className="grid gap-4 sm:grid-cols-2">
        <Field label="Resolution" htmlFor="target-resolution">
          <select
            id="target-resolution"
            className={inputClass}
            value={resolutionValue}
            onChange={(e) => {
              const v = e.target.value
              if (v === 'auto') set({ autoResolution: true, maxResolution: null })
              else if (v === 'original') set({ autoResolution: false, maxResolution: null })
              else set({ autoResolution: false, maxResolution: Number(v) })
            }}
          >
            <option value="auto">Automatic (best for the size)</option>
            <option value="original">Original ({shortSide(video.media)}p)</option>
            {resolutionChoices(video.media).map((r) => (
              <option key={r} value={r}>
                {r}p
              </option>
            ))}
          </select>
        </Field>
        <Field label="Audio" htmlFor="target-audio">
          <AudioSelect id="target-audio" video={video} value={target.audioBitrateKbps} onChange={(kbps) => set({ audioBitrateKbps: kbps })} />
        </Field>
      </div>

      <EstimatePanel state={targetEstimate} original={video.sizeBytes} waitingText="Working out the bitrate…" />
    </div>
  )
}

function CustomControls({ video, health, custom, onCustomChange, customEstimate }: OptionsPanelProps) {
  const codec = health?.codecs.find((c) => c.id === custom.codec)
  const set = (patch: Partial<CompressionSettings>) => onCustomChange({ ...custom, ...patch })
  const sourceFps = video.media.video.fps

  return (
    <div className="space-y-5">
      <Field label="Format">
        <CodecPicker
          health={health}
          value={custom.codec}
          onChange={(id) => {
            const next = health?.codecs.find((c) => c.id === id)
            set({ codec: id, crf: next?.crfDefault ?? custom.crf })
          }}
        />
      </Field>

      {codec && (
        <Field
          htmlFor="custom-quality"
          label={
            <span className="flex justify-between">
              <span>Quality</span>
              <span className="font-normal text-zinc-500 dark:text-zinc-400">
                {qualityLabel(codec, custom.crf)} · CRF {custom.crf}
              </span>
            </span>
          }
        >
          {/* Reversed so that right = better quality (a lower CRF). */}
          <input
            id="custom-quality"
            type="range"
            min={codec.crfMin}
            max={codec.crfMax}
            value={codec.crfMax + codec.crfMin - custom.crf}
            onChange={(e) => set({ crf: codec.crfMax + codec.crfMin - Number(e.target.value) })}
            className="w-full accent-indigo-600"
          />
          <div className="flex justify-between text-xs text-zinc-500 dark:text-zinc-400">
            <span>Smaller file</span>
            <span>Better quality</span>
          </div>
        </Field>
      )}

      <div className="grid gap-4 sm:grid-cols-3">
        <Field label="Resolution" htmlFor="custom-resolution">
          <select
            id="custom-resolution"
            className={inputClass}
            value={custom.maxResolution ?? ''}
            onChange={(e) => set({ maxResolution: e.target.value ? Number(e.target.value) : null })}
          >
            <option value="">Original ({shortSide(video.media)}p)</option>
            {resolutionChoices(video.media).map((r) => (
              <option key={r} value={r}>
                {r}p
              </option>
            ))}
          </select>
        </Field>
        <Field label="Frame rate" htmlFor="custom-fps">
          <select
            id="custom-fps"
            className={inputClass}
            value={custom.maxFps ?? ''}
            onChange={(e) => set({ maxFps: e.target.value ? Number(e.target.value) : null })}
          >
            <option value="">Original ({formatFps(sourceFps)} fps)</option>
            {fpsChoices(video.media).map((fps) => (
              <option key={fps} value={fps}>
                {fps} fps
              </option>
            ))}
          </select>
        </Field>
        <Field label="Audio" htmlFor="custom-audio">
          <AudioSelect id="custom-audio" video={video} value={custom.audioBitrateKbps} onChange={(kbps) => set({ audioBitrateKbps: kbps })} />
        </Field>
      </div>

      <Field label="Encoding speed">
        <Segmented label="Encoding speed" value={custom.speed} options={SPEED_CHOICES} onChange={(speed) => set({ speed })} />
      </Field>

      <EstimatePanel state={customEstimate} original={video.sizeBytes} waitingText="Test-encoding a few seconds of your video…" />
    </div>
  )
}
