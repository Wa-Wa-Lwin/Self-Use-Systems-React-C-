// Mirrors FileNames.cs on the server, so the preview under the input matches what gets downloaded.

const MAX_STEM_LENGTH = 120
// Forbidden on Windows (the strictest common OS), plus control characters.
const INVALID_CHARS = '[\\\\/:*?"<>|\\u0000-\\u001f\\u007f]'
const INVALID_ALL = new RegExp(INVALID_CHARS, 'g')
const INVALID_ANY = new RegExp(INVALID_CHARS)
const VIDEO_EXTENSION = /\.(mp4|m4v|mov|mkv|webm|avi|wmv|asf|flv|f4v|3gp|3g2|mpg|mpeg|m2v|ts|mts|m2ts|vob|ogv|mxf|dv|divx)$/i

/** "My trip?.mov" → "My trip_" (the output is always MP4, so a typed video extension is dropped). */
export function cleanFileStem(input: string): string {
  let stem = input.trim().replace(VIDEO_EXTENSION, '').replace(INVALID_ALL, '_')
  stem = stem.trim().replace(/^[. ]+|[. ]+$/g, '')
  if (stem.length > MAX_STEM_LENGTH) stem = Array.from(stem).slice(0, MAX_STEM_LENGTH).join('').replace(/[. ]+$/, '')
  return stem
}

export function hasInvalidCharacters(input: string): boolean {
  return INVALID_ANY.test(input)
}

/** The name the file will be saved as; falls back to the suggested name when the input is empty. */
export function outputFileName(input: string, suggestedStem: string): string {
  return `${cleanFileStem(input) || cleanFileStem(suggestedStem) || 'video-compressed'}.mp4`
}

/** "holiday-compressed.mp4" → "holiday-compressed" */
export function stemOf(fileName: string): string {
  return fileName.replace(/\.mp4$/i, '')
}
