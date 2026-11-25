import { useState } from 'react'
import type { AudioStream, Video } from '../api/types'
import { formatBitrate, formatBytes, formatDuration, formatFps, resolutionLabel } from '../lib/format'
import { FilmIcon } from './icons'
import { Card } from './ui'

function describeAudio(audio: AudioStream[]): string {
  if (audio.length === 0) return 'None'
  const first = audio[0]
  const channels = first.channels === 1 ? 'mono' : first.channels === 2 ? 'stereo' : `${first.channels} ch`
  const bitrate = first.bitrateBps ? ` · ${formatBitrate(first.bitrateBps)}` : ''
  const more = audio.length > 1 ? ` (+${audio.length - 1} more)` : ''
  return `${first.codecLabel} ${channels}${bitrate}${more}`
}

/** Local preview of the original (straight from the browser's copy of the file) plus its key facts. */
export function VideoOverview({ video, previewUrl }: { video: Video; previewUrl: string | null }) {
  const [playable, setPlayable] = useState(true)
  const { media } = video
  const v = media.video

  const facts: Array<[string, string]> = [
    ['File size', formatBytes(media.sizeBytes)],
    ['Duration', formatDuration(media.durationSeconds)],
    ['Resolution', `${v.width}×${v.height} · ${resolutionLabel(v.width, v.height)}`],
    ['Frame rate', `${formatFps(v.fps)} fps`],
    ['Video codec', `${v.codecLabel}${v.bitDepth > 8 ? ` · ${v.bitDepth}-bit` : ''}${v.isHdr ? ' · HDR' : ''}`],
    ['Video bitrate', `${v.bitrateEstimated ? '≈ ' : ''}${formatBitrate(v.bitrateBps)}`],
    ['Audio', describeAudio(media.audio)],
    ['Container', media.container],
  ]

  return (
    <Card className="overflow-hidden">
      <div className="aspect-video bg-zinc-950">
        {previewUrl && playable ? (
          <video
            src={previewUrl}
            controls
            muted
            playsInline
            preload="metadata"
            className="size-full object-contain"
            onError={() => setPlayable(false)}
          />
        ) : (
          <div className="grid size-full place-items-center text-zinc-500">
            <div className="text-center">
              <FilmIcon size={32} className="mx-auto" />
              <p className="mt-2 text-sm">Your browser can&apos;t preview this format</p>
            </div>
          </div>
        )}
      </div>
      <div className="p-5">
        <p className="truncate font-medium" title={video.fileName}>
          {video.fileName}
        </p>
        <dl className="mt-4 grid grid-cols-2 gap-x-4 gap-y-3 text-sm">
          {facts.map(([label, value]) => (
            <div key={label} className="min-w-0">
              <dt className="text-zinc-500 dark:text-zinc-400">{label}</dt>
              <dd className="truncate font-medium" title={value}>
                {value}
              </dd>
            </div>
          ))}
        </dl>
      </div>
    </Card>
  )
}
