# Video Compressor

A web app that makes video files smaller. Upload a video and the app:

1. **Reads it** with `ffprobe`: codec, resolution, frame rate, bitrate, audio, HDR.
2. **Checks how compressible it is.** It test-encodes a few seconds from across the video with each option, extrapolates to the full length, and gives a verdict ("Highly compressible", "Already well compressed", …) with the reasons behind it.
3. **Lets you choose:**
   - **Presets:** Light (H.264), Balanced (H.265, ≤1080p, recommended), Strong (720p), Maximum (480p/30 fps). Each card shows the measured size and the % saved.
   - **Target file size:** type or slide to a size (e.g. 25 MB for email). It uses two-pass encoding and can pick the resolution automatically.
   - **Custom:** codec (H.264 / H.265 / AV1), quality, resolution, frame rate, audio bitrate and encoder speed, with a live size estimate.
4. **Compresses it** in a background job with live progress, then lets you preview and download the result.

| Part | Stack |
| --- | --- |
| Frontend | React 19 + TypeScript + Vite 8 + Tailwind CSS 4 (`frontend/`) |
| Backend | ASP.NET Core (.NET 10) minimal API (`backend/VideoCompression.Api/`) |
| Engine | FFmpeg / ffprobe (libx264, libx265, SVT-AV1, AAC) |
| Tests | xUnit, including an end-to-end test that runs real FFmpeg (`backend/VideoCompression.Tests/`) |

## Requirements

- .NET 10 SDK
- Node.js 20.19+ (or 22.12+)
- FFmpeg with `ffmpeg` and `ffprobe`. On Windows, run the bundled script, which puts a portable build in `tools/ffmpeg` (nothing is installed system-wide):

  ```powershell
  powershell -ExecutionPolicy Bypass -File scripts/get-ffmpeg.ps1
  ```

  Alternatively, install FFmpeg on your `PATH` (`winget install Gyan.FFmpeg`, `brew install ffmpeg`, `apt install ffmpeg`),
  or set `VideoCompression:FfmpegFolder` in `backend/VideoCompression.Api/appsettings.json`.
  The server finds FFmpeg without a restart. The UI tells you if it's missing.

## Run in development

Two terminals:

```powershell
# 1. API on http://localhost:5080
dotnet run --project backend/VideoCompression.Api

# 2. UI on http://localhost:5173 (proxies /api to the backend)
cd frontend
npm install
npm run dev
```

Open http://localhost:5173.

## Run as a single app

```powershell
cd frontend; npm run build; cd ..          # builds into backend/VideoCompression.Api/wwwroot
dotnet run --project backend/VideoCompression.Api
```

Open http://localhost:5080. ASP.NET serves both the UI and the API.

## Tests

```powershell
dotnet test                       # unit + API tests (the end-to-end test needs FFmpeg)
cd frontend; npm run typecheck
```

The end-to-end test generates a clip with FFmpeg, uploads it, waits for the analysis, requests estimates, runs a
quality job and a two-pass target-size job, then checks the downloaded MP4s against the estimates.

## How the size estimate works

Video content matters far more than any formula: a static screen recording and a handheld concert clip at the same bitrate
compress completely differently. So the server encodes real samples with the exact settings:

- Videos up to 10 s are encoded whole, which gives an exact result.
- Longer videos are sampled at 3–5 evenly spaced points, about 8 s of video in total.
- Every sample starts with a forced keyframe, which would inflate a naïve "sample size × duration" projection (badly, for
  static content). The estimator reads the packet sizes with `ffprobe`. It then models the full encode as *keyframes × average
  keyframe + other frames × average frame*, with one keyframe per GOP plus the scene cuts seen in the samples.
- Audio is added from the chosen bitrate. Small AAC/MP3 tracks are copied instead of re-encoded.
- The verdict comes from the recommended preset's measured savings. If re-encoding at the same resolution barely helps,
  it says so and points to the options that lower the resolution.

Target-size mode needs no measurement: bitrate = size ÷ duration, minus audio, with a 4% safety margin.
It warns when that bitrate is too thin for the resolution and can lower the resolution automatically.

## API

| Method | Route | Purpose |
| --- | --- | --- |
| GET | `/api/health` | FFmpeg status, available codecs, upload limit |
| POST | `/api/videos?fileName=x.mp4` | Upload (raw body, streamed to disk) → media info, insights, presets |
| GET | `/api/videos/{id}` | Analysis progress, preset estimates, verdict |
| POST | `/api/videos/{id}/estimate` | Estimate for any settings (target size / custom) |
| POST | `/api/videos/{id}/jobs` | Start compressing |
| GET | `/api/jobs/{id}` | Progress, speed, ETA, result |
| POST | `/api/jobs/{id}/cancel` | Cancel |
| GET | `/api/jobs/{id}/file[?download=true]` | Stream (range requests) or download the result |
| DELETE | `/api/videos/{id}` | Delete the upload and its outputs |

## Configuration (`appsettings.json` → `VideoCompression`)

| Key | Default | |
| --- | --- | --- |
| `FfmpegFolder` | *(auto)* | Folder with `ffmpeg` and `ffprobe` |
| `StorageFolder` | `%TEMP%/VideoCompression` | Uploads and results |
| `MaxUploadMegabytes` | `4096` | Upload limit |
| `FileLifetimeMinutes` | `120` | Idle uploads are deleted after this long |
| `MaxConcurrentJobs` | `1` | Parallel compression jobs |

State is kept in memory, so a server restart forgets uploads and deletes their files. Behind IIS, also raise
`maxAllowedContentLength` for large uploads.
# Self-Use-Systems-React-C-
