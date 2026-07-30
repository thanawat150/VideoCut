param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$FfmpegDirectory = "",
    [switch]$AllowMissingFfmpeg
)

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot
$Artifacts = Join-Path $Root "artifacts"
$Package = Join-Path $Artifacts "AutoCutStudio-$Runtime"
$AppOut = Join-Path $Artifacts "_app"
$WorkerOut = Join-Path $Artifacts "_worker"
$BundledTools = Join-Path $Package "tools\ffmpeg"

function Get-ValidFfmpegDirectory {
    param([string]$Candidate)

    if ([string]::IsNullOrWhiteSpace($Candidate)) {
        return $null
    }

    try {
        $fullPath = [System.IO.Path]::GetFullPath($Candidate)
    }
    catch {
        return $null
    }

    $ffmpeg = Join-Path $fullPath "ffmpeg.exe"
    $ffprobe = Join-Path $fullPath "ffprobe.exe"
    if ((Test-Path $ffmpeg -PathType Leaf) -and (Test-Path $ffprobe -PathType Leaf)) {
        return $fullPath
    }

    return $null
}

function Resolve-FfmpegDirectory {
    $candidates = [System.Collections.Generic.List[string]]::new()

    if (-not [string]::IsNullOrWhiteSpace($FfmpegDirectory)) {
        $candidates.Add($FfmpegDirectory)
    }

    if (-not [string]::IsNullOrWhiteSpace($env:AUTOCUT_BUNDLE_FFMPEG_DIR)) {
        $candidates.Add($env:AUTOCUT_BUNDLE_FFMPEG_DIR)
    }

    $ffmpegCommand = Get-Command ffmpeg.exe -CommandType Application -ErrorAction SilentlyContinue
    if ($null -ne $ffmpegCommand -and -not [string]::IsNullOrWhiteSpace($ffmpegCommand.Source)) {
        $commandDirectory = Split-Path $ffmpegCommand.Source -Parent
        $chocolateyBin = if ([string]::IsNullOrWhiteSpace($env:ChocolateyInstall)) {
            ""
        }
        else {
            Join-Path $env:ChocolateyInstall "bin"
        }

        if ([string]::IsNullOrWhiteSpace($chocolateyBin) -or
            -not $commandDirectory.Equals($chocolateyBin, [System.StringComparison]::OrdinalIgnoreCase)) {
            $candidates.Add($commandDirectory)
        }
    }

    $chocolateyRoots = [System.Collections.Generic.List[string]]::new()
    if (-not [string]::IsNullOrWhiteSpace($env:ChocolateyInstall)) {
        $chocolateyRoots.Add($env:ChocolateyInstall)
    }
    $chocolateyRoots.Add("C:\ProgramData\chocolatey")

    foreach ($chocolateyRoot in $chocolateyRoots | Select-Object -Unique) {
        $libraryRoot = Join-Path $chocolateyRoot "lib"
        if (-not (Test-Path $libraryRoot -PathType Container)) {
            continue
        }

        Get-ChildItem $libraryRoot -Filter "ffmpeg.exe" -File -Recurse -ErrorAction SilentlyContinue |
            ForEach-Object { $candidates.Add($_.DirectoryName) }
    }

    foreach ($candidate in $candidates | Select-Object -Unique) {
        $resolved = Get-ValidFfmpegDirectory $candidate
        if ($null -ne $resolved) {
            return $resolved
        }
    }

    return $null
}

function Copy-ThirdPartyNotices {
    param(
        [string]$SourceDirectory,
        [string]$DestinationDirectory
    )

    $noticeDirectory = Join-Path $Package "third_party\ffmpeg"
    New-Item $noticeDirectory -ItemType Directory -Force | Out-Null

    $copied = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    $current = $SourceDirectory
    for ($level = 0; $level -lt 5 -and -not [string]::IsNullOrWhiteSpace($current); $level++) {
        foreach ($pattern in @("LICENSE*", "COPYING*", "README*")) {
            Get-ChildItem $current -Filter $pattern -File -ErrorAction SilentlyContinue | ForEach-Object {
                if ($copied.Add($_.Name)) {
                    Copy-Item $_.FullName (Join-Path $noticeDirectory $_.Name)
                }
            }
        }

        $parent = Split-Path $current -Parent
        if ([string]::IsNullOrWhiteSpace($parent) -or $parent -eq $current) {
            break
        }
        $current = $parent
    }

    $ffmpegPath = Join-Path $DestinationDirectory "ffmpeg.exe"
    $ffprobePath = Join-Path $DestinationDirectory "ffprobe.exe"
    $ffmpegVersion = ((& $ffmpegPath -version 2>&1 | Select-Object -First 1) -join "").Trim()
    if ($LASTEXITCODE -ne 0) {
        throw "Bundled ffmpeg.exe did not start successfully."
    }
    $ffprobeVersion = ((& $ffprobePath -version 2>&1 | Select-Object -First 1) -join "").Trim()
    if ($LASTEXITCODE -ne 0) {
        throw "Bundled ffprobe.exe did not start successfully."
    }

    $manifest = [ordered]@{
        product = "FFmpeg"
        bundled_at_utc = [DateTimeOffset]::UtcNow.ToString("O")
        ffmpeg_version = $ffmpegVersion
        ffprobe_version = $ffprobeVersion
        ffmpeg_sha256 = (Get-FileHash $ffmpegPath -Algorithm SHA256).Hash.ToLowerInvariant()
        ffprobe_sha256 = (Get-FileHash $ffprobePath -Algorithm SHA256).Hash.ToLowerInvariant()
        source_directory = "Build environment FFmpeg distribution"
        project_website = "https://ffmpeg.org/"
        licensing_note = "FFmpeg is third-party software. License files copied from the distribution are stored in this directory when available."
    }

    $manifest | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $noticeDirectory "bundle-manifest.json") -Encoding UTF8
}

Remove-Item $Artifacts -Recurse -Force -ErrorAction SilentlyContinue
New-Item $Package -ItemType Directory -Force | Out-Null
New-Item $BundledTools -ItemType Directory -Force | Out-Null
New-Item (Join-Path $Package "docs") -ItemType Directory -Force | Out-Null

dotnet publish (Join-Path $Root "src\AutoCutStudio.App\AutoCutStudio.App.csproj") `
    -c $Configuration `
    -r $Runtime `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:PublishTrimmed=false `
    -p:DebugType=None `
    -o $AppOut

dotnet publish (Join-Path $Root "src\AutoCutStudio.Worker\AutoCutStudio.Worker.csproj") `
    -c $Configuration `
    -r $Runtime `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:PublishTrimmed=false `
    -p:DebugType=None `
    -o $WorkerOut

Copy-Item (Join-Path $AppOut "AutoCutStudio.exe") $Package
Copy-Item (Join-Path $WorkerOut "AutoCutStudio.Worker.exe") $Package
Copy-Item (Join-Path $Root "README.md") $Package
Copy-Item (Join-Path $Root "docs\PHASE1_ARCHITECTURE.md") (Join-Path $Package "docs")
Copy-Item (Join-Path $Root "docs\IMPLEMENTATION_STATUS.md") (Join-Path $Package "docs")

$resolvedFfmpegDirectory = Resolve-FfmpegDirectory
if ($null -eq $resolvedFfmpegDirectory) {
    if (-not $AllowMissingFfmpeg) {
        throw "Unable to locate real ffmpeg.exe and ffprobe.exe for the portable package. Provide -FfmpegDirectory or AUTOCUT_BUNDLE_FFMPEG_DIR."
    }

    @"
FFmpeg and FFprobe were not bundled in this developer build.
Place ffmpeg.exe and ffprobe.exe in this folder before running media jobs.
"@ | Set-Content (Join-Path $BundledTools "MISSING_DEPENDENCY.txt") -Encoding UTF8
}
else {
    Copy-Item (Join-Path $resolvedFfmpegDirectory "ffmpeg.exe") $BundledTools
    Copy-Item (Join-Path $resolvedFfmpegDirectory "ffprobe.exe") $BundledTools
    Copy-ThirdPartyNotices -SourceDirectory $resolvedFfmpegDirectory -DestinationDirectory $BundledTools
}

$Zip = "$Package.zip"
Compress-Archive -Path "$Package\*" -DestinationPath $Zip -Force
$Hash = (Get-FileHash $Zip -Algorithm SHA256).Hash.ToLowerInvariant()
"$Hash  $(Split-Path $Zip -Leaf)" | Set-Content "$Zip.sha256" -Encoding ASCII

Write-Host "Portable folder: $Package"
Write-Host "Portable ZIP: $Zip"
Write-Host "SHA-256: $Hash"
if ($null -ne $resolvedFfmpegDirectory) {
    Write-Host "Bundled FFmpeg from: $resolvedFfmpegDirectory"
}
