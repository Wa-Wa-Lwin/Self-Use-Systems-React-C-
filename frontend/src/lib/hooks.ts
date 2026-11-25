import { useEffect, useRef, useState } from 'react'
import { api, errorMessage, isAbort } from '../api/client'
import type { CompressionSettings, EstimateResult } from '../api/types'

/**
 * Calls `tick` every `intervalMs` while `enabled`, never overlapping requests.
 * Return false from `tick` to stop polling.
 */
export function usePolling(tick: () => Promise<boolean | void>, intervalMs: number, enabled: boolean) {
  const tickRef = useRef(tick)
  tickRef.current = tick

  useEffect(() => {
    if (!enabled) return
    let stopped = false
    let timer: ReturnType<typeof setTimeout>

    const run = async () => {
      if (stopped) return
      let keepGoing: boolean | void = true
      try {
        keepGoing = await tickRef.current()
      } catch {
        // transient error: try again on the next tick
      }
      if (!stopped && keepGoing !== false) timer = setTimeout(run, intervalMs)
    }

    timer = setTimeout(run, intervalMs)
    return () => {
      stopped = true
      clearTimeout(timer)
    }
  }, [enabled, intervalMs])
}

export interface EstimateState {
  loading: boolean
  result: EstimateResult | null
  error: string | null
}

/**
 * Debounced, cancellable size estimate for a set of settings. While a new estimate is
 * loading the previous result stays visible. Pass null settings to pause.
 */
export function useEstimate(videoId: string, settings: CompressionSettings | null, delayMs = 500): EstimateState {
  const [state, setState] = useState<EstimateState>({ loading: false, result: null, error: null })
  const key = settings ? JSON.stringify(settings) : null

  useEffect(() => {
    if (!key) return
    const controller = new AbortController()
    setState((previous) => ({ ...previous, loading: true, error: null }))

    const timer = setTimeout(() => {
      api
        .estimate(videoId, JSON.parse(key) as CompressionSettings, controller.signal)
        .then((result) => setState({ loading: false, result, error: null }))
        .catch((error: unknown) => {
          if (isAbort(error) || controller.signal.aborted) return
          setState({ loading: false, result: null, error: errorMessage(error) })
        })
    }, delayMs)

    return () => {
      clearTimeout(timer)
      controller.abort()
    }
  }, [videoId, key, delayMs])

  return state
}
