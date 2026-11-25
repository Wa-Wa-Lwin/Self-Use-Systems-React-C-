# Downloads a portable FFmpeg build (ffmpeg.exe + ffprobe.exe) into ./tools/ffmpeg.
# The backend looks there automatically, so nothing has to be installed system-wide.
#
#   powershell -ExecutionPolicy Bypass -File scripts/get-ffmpeg.ps1
#
# On macOS / Linux install FFmpeg with your package manager instead (brew install ffmpeg / apt install ffmpeg).

param(
    [string]$Url = "https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip"
)

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"   # Invoke-WebRequest is very slow with the progress bar on

$root    = Split-Path -Parent $PSScriptRoot
$target  = Join-Path $root "tools/ffmpeg"
$work    = Join-Path ([System.IO.Path]::GetTempPath()) ("ffmpeg-download-" + [guid]::NewGuid().ToString("N"))
$zipPath = Join-Path $work "ffmpeg.zip"

if (Test-Path (Join-Path $target "ffmpeg.exe")) {
    Write-Host "FFmpeg already present in $target"
    & (Join-Path $target "ffmpeg.exe") -hide_banner -version | Select-Object -First 1
    exit 0
}

New-Item -ItemType Directory -Force -Path $work   | Out-Null
New-Item -ItemType Directory -Force -Path $target | Out-Null

try {
    Write-Host "Downloading $Url ..."
    Invoke-WebRequest -Uri $Url -OutFile $zipPath -UseBasicParsing

    Write-Host "Extracting ..."
    $extract = Join-Path $work "x"
    Expand-Archive -Path $zipPath -DestinationPath $extract -Force

    foreach ($exe in "ffmpeg.exe", "ffprobe.exe") {
        $found = Get-ChildItem -Path $extract -Recurse -Filter $exe | Select-Object -First 1
        if (-not $found) { throw "$exe was not found in the downloaded archive." }
        Copy-Item $found.FullName -Destination (Join-Path $target $exe) -Force
    }

    Write-Host "FFmpeg installed to $target"
    & (Join-Path $target "ffmpeg.exe") -hide_banner -version | Select-Object -First 1
}
finally {
    Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
}
