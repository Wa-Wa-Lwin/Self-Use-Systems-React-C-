import { hasInvalidCharacters, outputFileName } from '../lib/filename'
import { Field, cx, inputClass } from './ui'

/** Text box for naming the compressed file. ".mp4" is shown as a fixed suffix. */
export function OutputNameField({
  id,
  value,
  suggestedStem,
  onChange,
  onEnter,
}: {
  id: string
  value: string
  suggestedStem: string
  onChange: (value: string) => void
  /** Called when Enter is pressed (e.g. to start the download). */
  onEnter?: () => void
}) {
  const finalName = outputFileName(value, suggestedStem)
  const invalid = hasInvalidCharacters(value)
  const empty = value.trim() === ''

  return (
    <Field label="File name" htmlFor={id}>
      <div className="relative">
        <input
          id={id}
          type="text"
          value={value}
          placeholder={suggestedStem}
          maxLength={200}
          spellCheck={false}
          autoComplete="off"
          aria-describedby={`${id}-hint`}
          onChange={(event) => onChange(event.target.value)}
          onKeyDown={(event) => {
            if (event.key === 'Enter' && onEnter) {
              event.preventDefault()
              onEnter()
            }
          }}
          className={cx(inputClass, 'py-2.5 pr-14')}
        />
        <span className="pointer-events-none absolute inset-y-0 right-3 flex items-center text-sm text-zinc-500 dark:text-zinc-400">
          .mp4
        </span>
      </div>
      <p
        id={`${id}-hint`}
        className={cx('mt-1.5 truncate text-xs', invalid ? 'text-amber-700 dark:text-amber-400' : 'text-zinc-500 dark:text-zinc-400')}
        title={finalName}
      >
        {invalid ? 'File names can’t contain \\ / : * ? " < > |, so it will be saved as ' : empty ? 'Left empty, so it will be saved as ' : 'Saved as '}
        <span className="font-medium text-zinc-700 dark:text-zinc-200">{finalName}</span>
      </p>
    </Field>
  )
}
