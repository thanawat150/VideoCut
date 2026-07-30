$ErrorActionPreference = 'Stop'

$root = Resolve-Path (Join-Path $PSScriptRoot '..')
$artifacts = Join-Path $root 'artifacts/smoke'
$appOut = Join-Path $artifacts 'app'
$media = Join-Path $artifacts 'input.mp4'
$output = Join-Path $artifacts 'output.mp4'

Remove-Item $artifacts -Recurse -Force -ErrorAction SilentlyContinue
New-Item $appOut -ItemType Directory -Force | Out-Null

dotnet publish (Join-Path $root 'src/AutoCutStudio/AutoCutStudio.csproj') -c Release -r win-x64 --self-contained false -o $appOut
ffmpeg -hide_banner -loglevel error -f lavfi -i 'testsrc=size=640x360:rate=30' -f lavfi -i 'sine=frequency=1000' -t 3 -c:v libx264 -pix_fmt yuv420p -c:a aac -y $media

$jobId = [Guid]::NewGuid()
$jobRoot = Join-Path $env:LOCALAPPDATA ('AutoCutStudio/Jobs/' + $jobId.ToString('N'))
New-Item $jobRoot -ItemType Directory -Force | Out-Null
$job = @{
  jobId = $jobId
  type = 'export'
  status = 'Waiting'
  progressPercent = 0
  createdAt = [DateTimeOffset]::UtcNow
  updatedAt = [DateTimeOffset]::UtcNow
  export = @{
    jobId = $jobId
    inputPath = $media
    outputPath = $output
    start = '00:00:00'
    duration = '00:00:02'
    settings = @{ preset = 'Smoke'; codec = 'libx264'; crf = 25; audioCodec = 'aac'; audioBitrateKbps = 128 }
  }
} | ConvertTo-Json -Depth 8
$job | Set-Content (Join-Path $jobRoot 'job.json') -Encoding UTF8
'{}' | Set-Content (Join-Path $jobRoot 'progress.json') -Encoding UTF8
'' | Set-Content (Join-Path $jobRoot 'run.log') -Encoding UTF8
'' | Set-Content (Join-Path $jobRoot 'error.log') -Encoding UTF8

$process = Start-Process -FilePath (Join-Path $appOut 'AutoCutStudio.exe') -ArgumentList '--worker','--once' -PassThru -Wait
if ($process.ExitCode -ne 0) { throw "Worker exited with code $($process.ExitCode)" }
if (-not (Test-Path $output)) { throw 'Smoke test failed: output.mp4 was not created.' }

$probe = ffprobe -v error -show_entries 'format=duration:stream=codec_type' -of json $output | ConvertFrom-Json
$duration = [double]$probe.format.duration
if ($duration -lt 1.8 -or $duration -gt 2.3) { throw "Unexpected output duration: $duration" }
if (($probe.streams.codec_type -notcontains 'video') -or ($probe.streams.codec_type -notcontains 'audio')) { throw 'Output must contain video and audio.' }

Write-Host "Smoke test passed: $output ($duration seconds)"
