param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$FfmpegDirectory = ""
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$Root = Split-Path -Parent $PSScriptRoot
$Artifacts = Join-Path $Root "artifacts"
$Package = Join-Path $Artifacts "AutoCutStudio-Rebuild-$Runtime"
$Publish = Join-Path $Artifacts "publish"
$Tools = Join-Path $Package "tools\ffmpeg"
$Notice = Join-Path $Package "third_party\ffmpeg"

function Get-ValidFfmpegDirectory {
    param([string]$Candidate)
    if ([string]::IsNullOrWhiteSpace($Candidate)) { return $null }
    try { $full = [System.IO.Path]::GetFullPath($Candidate) } catch { return $null }
    $ffmpeg = Join-Path $full "ffmpeg.exe"
    $ffprobe = Join-Path $full "ffprobe.exe"
    if (-not (Test-Path $ffmpeg -PathType Leaf) -or -not (Test-Path $ffprobe -PathType Leaf)) { return $null }
    if ((Get-Item $ffmpeg).Length -lt 1MB -or (Get-Item $ffprobe).Length -lt 1MB) { return $null }
    return $full
}

function Resolve-FfmpegDirectory {
    $candidates = [System.Collections.Generic.List[string]]::new()
    if ($FfmpegDirectory) { $candidates.Add($FfmpegDirectory) }
    if ($env:AUTOCUT_REBUILD_FFMPEG_DIR) { $candidates.Add($env:AUTOCUT_REBUILD_FFMPEG_DIR) }

    foreach ($root in @($env:ChocolateyInstall, "C:\ProgramData\chocolatey") | Where-Object { $_ }) {
        $lib = Join-Path $root "lib"
        if (-not (Test-Path $lib -PathType Container)) { continue }
        Get-ChildItem $lib -Filter ffmpeg.exe -File -Recurse -ErrorAction SilentlyContinue |
            Sort-Object Length -Descending |
            ForEach-Object { $candidates.Add($_.DirectoryName) }
    }

    $command = Get-Command ffmpeg.exe -CommandType Application -ErrorAction SilentlyContinue
    if ($null -ne $command -and $command.Source) { $candidates.Add((Split-Path $command.Source -Parent)) }

    foreach ($candidate in $candidates | Select-Object -Unique) {
        $resolved = Get-ValidFfmpegDirectory $candidate
        if ($null -ne $resolved) { return $resolved }
    }
    return $null
}

function Copy-NoticeFiles {
    param([string]$SourceDirectory)
    New-Item $Notice -ItemType Directory -Force | Out-Null
    $seen = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    $current = $SourceDirectory
    for ($level = 0; $level -lt 6 -and $current; $level++) {
        foreach ($pattern in @("LICENSE*", "COPYING*", "NOTICE*", "README*")) {
            Get-ChildItem $current -Filter $pattern -File -ErrorAction SilentlyContinue | ForEach-Object {
                if ($seen.Add($_.Name)) { Copy-Item $_.FullName (Join-Path $Notice $_.Name) }
            }
        }
        $parent = Split-Path $current -Parent
        if (-not $parent -or $parent -eq $current) { break }
        $current = $parent
    }
}

Remove-Item $Artifacts -Recurse -Force -ErrorAction SilentlyContinue
New-Item $Package,$Tools,$Notice -ItemType Directory -Force | Out-Null

dotnet publish (Join-Path $Root "AutoCutStudio.Rebuild.csproj") `
    -c $Configuration -r $Runtime --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:PublishTrimmed=false -p:DebugType=None -o $Publish
if ($LASTEXITCODE -ne 0) { throw "Publish failed." }

Copy-Item (Join-Path $Publish "AutoCutStudio.Rebuild.exe") $Package
Copy-Item (Join-Path $Root "README.md") $Package

$resolvedFfmpeg = Resolve-FfmpegDirectory
if ($null -eq $resolvedFfmpeg) {
    throw "Unable to locate real ffmpeg.exe and ffprobe.exe binaries. Chocolatey shims are not accepted."
}
Copy-Item (Join-Path $resolvedFfmpeg "ffmpeg.exe") $Tools
Copy-Item (Join-Path $resolvedFfmpeg "ffprobe.exe") $Tools
Copy-NoticeFiles $resolvedFfmpeg

$bundledFfmpeg = Join-Path $Tools "ffmpeg.exe"
$bundledFfprobe = Join-Path $Tools "ffprobe.exe"
$version = ((& $bundledFfmpeg -version 2>&1 | Select-Object -First 1) -join "").Trim()
if ($LASTEXITCODE -ne 0) { throw "Bundled ffmpeg.exe failed to start." }
& $bundledFfprobe -version *> $null
if ($LASTEXITCODE -ne 0) { throw "Bundled ffprobe.exe failed to start." }

[ordered]@{
    product = "FFmpeg"
    bundled_at_utc = [DateTimeOffset]::UtcNow.ToString("O")
    version = $version
    source_directory = $resolvedFfmpeg
    ffmpeg_size_bytes = (Get-Item $bundledFfmpeg).Length
    ffprobe_size_bytes = (Get-Item $bundledFfprobe).Length
    ffmpeg_sha256 = (Get-FileHash $bundledFfmpeg -Algorithm SHA256).Hash.ToLowerInvariant()
    ffprobe_sha256 = (Get-FileHash $bundledFfprobe -Algorithm SHA256).Hash.ToLowerInvariant()
    project_website = "https://ffmpeg.org/"
} | ConvertTo-Json | Set-Content (Join-Path $Notice "bundle-manifest.json") -Encoding UTF8

$zip = "$Package.zip"
Compress-Archive -Path "$Package\*" -DestinationPath $zip -Force
$hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  $(Split-Path $zip -Leaf)" | Set-Content "$zip.sha256" -Encoding ASCII
Write-Host "Portable ZIP: $zip"
Write-Host "SHA-256: $hash"
Write-Host "Bundled FFmpeg directory: $resolvedFfmpeg"