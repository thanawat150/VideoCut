param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$FfmpegDirectory = "",
    [string]$WhisperDirectory = "",
    [string]$WhisperModelPath = "",
    [switch]$AllowMissingFfmpeg,
    [switch]$AllowMissingWhisper
)

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot
$Artifacts = Join-Path $Root "artifacts"
$Package = Join-Path $Artifacts "AutoCutStudio-$Runtime"
$AppOut = Join-Path $Artifacts "_app"
$WorkerOut = Join-Path $Artifacts "_worker"
$BundledFfmpegTools = Join-Path $Package "tools\ffmpeg"
$BundledWhisperTools = Join-Path $Package "tools\whisper"
$BundledWhisperModels = Join-Path $Package "models\whisper"

function Get-ValidFfmpegDirectory {
    param([string]$Candidate)

    if ([string]::IsNullOrWhiteSpace($Candidate)) { return $null }
    try { $fullPath = [System.IO.Path]::GetFullPath($Candidate) }
    catch { return $null }

    if ((Test-Path (Join-Path $fullPath "ffmpeg.exe") -PathType Leaf) -and
        (Test-Path (Join-Path $fullPath "ffprobe.exe") -PathType Leaf)) {
        return $fullPath
    }

    return $null
}

function Resolve-FfmpegDirectory {
    $candidates = [System.Collections.Generic.List[string]]::new()
    if (-not [string]::IsNullOrWhiteSpace($FfmpegDirectory)) { $candidates.Add($FfmpegDirectory) }
    if (-not [string]::IsNullOrWhiteSpace($env:AUTOCUT_BUNDLE_FFMPEG_DIR)) { $candidates.Add($env:AUTOCUT_BUNDLE_FFMPEG_DIR) }

    $ffmpegCommand = Get-Command ffmpeg.exe -CommandType Application -ErrorAction SilentlyContinue
    if ($null -ne $ffmpegCommand -and -not [string]::IsNullOrWhiteSpace($ffmpegCommand.Source)) {
        $commandDirectory = Split-Path $ffmpegCommand.Source -Parent
        $chocolateyBin = if ([string]::IsNullOrWhiteSpace($env:ChocolateyInstall)) { "" } else { Join-Path $env:ChocolateyInstall "bin" }
        if ([string]::IsNullOrWhiteSpace($chocolateyBin) -or
            -not $commandDirectory.Equals($chocolateyBin, [System.StringComparison]::OrdinalIgnoreCase)) {
            $candidates.Add($commandDirectory)
        }
    }

    $chocolateyRoots = [System.Collections.Generic.List[string]]::new()
    if (-not [string]::IsNullOrWhiteSpace($env:ChocolateyInstall)) { $chocolateyRoots.Add($env:ChocolateyInstall) }
    $chocolateyRoots.Add("C:\ProgramData\chocolatey")
    foreach ($chocolateyRoot in $chocolateyRoots | Select-Object -Unique) {
        $libraryRoot = Join-Path $chocolateyRoot "lib"
        if (-not (Test-Path $libraryRoot -PathType Container)) { continue }
        Get-ChildItem $libraryRoot -Filter "ffmpeg.exe" -File -Recurse -ErrorAction SilentlyContinue |
            ForEach-Object { $candidates.Add($_.DirectoryName) }
    }

    foreach ($candidate in $candidates | Select-Object -Unique) {
        $resolved = Get-ValidFfmpegDirectory $candidate
        if ($null -ne $resolved) { return $resolved }
    }

    return $null
}

function Get-ValidWhisperDirectory {
    param([string]$Candidate)

    if ([string]::IsNullOrWhiteSpace($Candidate)) { return $null }
    try { $fullPath = [System.IO.Path]::GetFullPath($Candidate) }
    catch { return $null }

    if (Test-Path (Join-Path $fullPath "whisper-cli.exe") -PathType Leaf) {
        return $fullPath
    }

    return $null
}

function Resolve-WhisperDirectory {
    $candidates = [System.Collections.Generic.List[string]]::new()
    if (-not [string]::IsNullOrWhiteSpace($WhisperDirectory)) { $candidates.Add($WhisperDirectory) }
    if (-not [string]::IsNullOrWhiteSpace($env:AUTOCUT_BUNDLE_WHISPER_DIR)) { $candidates.Add($env:AUTOCUT_BUNDLE_WHISPER_DIR) }
    if (-not [string]::IsNullOrWhiteSpace($env:AUTOCUT_WHISPER_CLI_PATH)) {
        $candidates.Add((Split-Path $env:AUTOCUT_WHISPER_CLI_PATH -Parent))
    }
    $candidates.Add((Join-Path $Root ".tools\whisper"))

    foreach ($candidate in $candidates | Select-Object -Unique) {
        $resolved = Get-ValidWhisperDirectory $candidate
        if ($null -ne $resolved) { return $resolved }
    }

    return $null
}

function Resolve-WhisperModelPath {
    $candidates = [System.Collections.Generic.List[string]]::new()
    if (-not [string]::IsNullOrWhiteSpace($WhisperModelPath)) { $candidates.Add($WhisperModelPath) }
    if (-not [string]::IsNullOrWhiteSpace($env:AUTOCUT_BUNDLE_WHISPER_MODEL)) { $candidates.Add($env:AUTOCUT_BUNDLE_WHISPER_MODEL) }
    if (-not [string]::IsNullOrWhiteSpace($env:AUTOCUT_WHISPER_MODEL_PATH)) { $candidates.Add($env:AUTOCUT_WHISPER_MODEL_PATH) }
    $candidates.Add((Join-Path $Root ".tools\whisper-model\ggml-base-q5_1.bin"))

    foreach ($candidate in $candidates | Select-Object -Unique) {
        if ([string]::IsNullOrWhiteSpace($candidate)) { continue }
        try { $fullPath = [System.IO.Path]::GetFullPath($candidate) }
        catch { continue }
        if ((Test-Path $fullPath -PathType Leaf) -and (Get-Item $fullPath).Length -gt 10MB) {
            return $fullPath
        }
    }

    return $null
}

function Copy-LicenseFiles {
    param(
        [string]$SourceDirectory,
        [string]$DestinationDirectory,
        [int]$MaximumLevels = 5
    )

    New-Item $DestinationDirectory -ItemType Directory -Force | Out-Null
    $copied = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    $current = $SourceDirectory
    for ($level = 0; $level -lt $MaximumLevels -and -not [string]::IsNullOrWhiteSpace($current); $level++) {
        foreach ($pattern in @("LICENSE*", "COPYING*", "NOTICE*", "README*")) {
            Get-ChildItem $current -Filter $pattern -File -ErrorAction SilentlyContinue | ForEach-Object {
                if ($copied.Add($_.Name)) {
                    Copy-Item $_.FullName (Join-Path $DestinationDirectory $_.Name)
                }
            }
        }

        $parent = Split-Path $current -Parent
        if ([string]::IsNullOrWhiteSpace($parent) -or $parent -eq $current) { break }
        $current = $parent
    }
}

function Copy-FfmpegNotices {
    param([string]$SourceDirectory)

    $noticeDirectory = Join-Path $Package "third_party\ffmpeg"
    Copy-LicenseFiles -SourceDirectory $SourceDirectory -DestinationDirectory $noticeDirectory
    $ffmpegPath = Join-Path $BundledFfmpegTools "ffmpeg.exe"
    $ffprobePath = Join-Path $BundledFfmpegTools "ffprobe.exe"
    $ffmpegVersion = ((& $ffmpegPath -version 2>&1 | Select-Object -First 1) -join "").Trim()
    if ($LASTEXITCODE -ne 0) { throw "Bundled ffmpeg.exe did not start successfully." }
    $ffprobeVersion = ((& $ffprobePath -version 2>&1 | Select-Object -First 1) -join "").Trim()
    if ($LASTEXITCODE -ne 0) { throw "Bundled ffprobe.exe did not start successfully." }

    [ordered]@{
        product = "FFmpeg"
        bundled_at_utc = [DateTimeOffset]::UtcNow.ToString("O")
        ffmpeg_version = $ffmpegVersion
        ffprobe_version = $ffprobeVersion
        ffmpeg_sha256 = (Get-FileHash $ffmpegPath -Algorithm SHA256).Hash.ToLowerInvariant()
        ffprobe_sha256 = (Get-FileHash $ffprobePath -Algorithm SHA256).Hash.ToLowerInvariant()
        source_directory = "Build environment FFmpeg distribution"
        project_website = "https://ffmpeg.org/"
        licensing_note = "FFmpeg is third-party software. Distribution license files are stored beside this manifest when available."
    } | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $noticeDirectory "bundle-manifest.json") -Encoding UTF8
}

function Copy-WhisperNotices {
    param(
        [string]$SourceDirectory,
        [string]$SourceModelPath
    )

    $noticeDirectory = Join-Path $Package "third_party\whisper.cpp"
    Copy-LicenseFiles -SourceDirectory $SourceDirectory -DestinationDirectory $noticeDirectory
    $cliPath = Join-Path $BundledWhisperTools "whisper-cli.exe"
    $modelPath = Join-Path $BundledWhisperModels "ggml-base-q5_1.bin"
    $helpOutput = & $cliPath -h 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "Bundled whisper-cli.exe did not start successfully: $($helpOutput | Select-Object -First 8)"
    }

    [ordered]@{
        product = "whisper.cpp"
        bundled_at_utc = [DateTimeOffset]::UtcNow.ToString("O")
        release = "v1.8.6"
        cli_sha256 = (Get-FileHash $cliPath -Algorithm SHA256).Hash.ToLowerInvariant()
        model = "ggml-base-q5_1.bin"
        model_size_bytes = (Get-Item $modelPath).Length
        model_sha256 = (Get-FileHash $modelPath -Algorithm SHA256).Hash.ToLowerInvariant()
        source_directory = "Official whisper.cpp Windows x64 release"
        project_website = "https://github.com/ggml-org/whisper.cpp"
        model_repository = "https://huggingface.co/ggerganov/whisper.cpp"
        licensing_note = "whisper.cpp is MIT licensed. Model use remains subject to the upstream model repository terms."
    } | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $noticeDirectory "bundle-manifest.json") -Encoding UTF8
}

Remove-Item $Artifacts -Recurse -Force -ErrorAction SilentlyContinue
New-Item $Package -ItemType Directory -Force | Out-Null
New-Item $BundledFfmpegTools -ItemType Directory -Force | Out-Null
New-Item $BundledWhisperTools -ItemType Directory -Force | Out-Null
New-Item $BundledWhisperModels -ItemType Directory -Force | Out-Null
New-Item (Join-Path $Package "docs") -ItemType Directory -Force | Out-Null

dotnet publish (Join-Path $Root "src\AutoCutStudio.App\AutoCutStudio.App.csproj") `
    -c $Configuration -r $Runtime --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:PublishTrimmed=false -p:DebugType=None -o $AppOut

dotnet publish (Join-Path $Root "src\AutoCutStudio.Worker\AutoCutStudio.Worker.csproj") `
    -c $Configuration -r $Runtime --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:PublishTrimmed=false -p:DebugType=None -o $WorkerOut

Copy-Item (Join-Path $AppOut "AutoCutStudio.exe") $Package
Copy-Item (Join-Path $WorkerOut "AutoCutStudio.Worker.exe") $Package
Copy-Item (Join-Path $Root "README.md") $Package
Copy-Item (Join-Path $Root "docs\PHASE1_ARCHITECTURE.md") (Join-Path $Package "docs")
Copy-Item (Join-Path $Root "docs\IMPLEMENTATION_STATUS.md") (Join-Path $Package "docs")

$resolvedFfmpegDirectory = Resolve-FfmpegDirectory
if ($null -eq $resolvedFfmpegDirectory) {
    if (-not $AllowMissingFfmpeg) {
        throw "Unable to locate real ffmpeg.exe and ffprobe.exe for the portable package."
    }
    "FFmpeg and FFprobe were not bundled in this developer build." |
        Set-Content (Join-Path $BundledFfmpegTools "MISSING_DEPENDENCY.txt") -Encoding UTF8
}
else {
    Copy-Item (Join-Path $resolvedFfmpegDirectory "ffmpeg.exe") $BundledFfmpegTools
    Copy-Item (Join-Path $resolvedFfmpegDirectory "ffprobe.exe") $BundledFfmpegTools
    Copy-FfmpegNotices -SourceDirectory $resolvedFfmpegDirectory
}

$resolvedWhisperDirectory = Resolve-WhisperDirectory
$resolvedWhisperModel = Resolve-WhisperModelPath
if ($null -eq $resolvedWhisperDirectory -or $null -eq $resolvedWhisperModel) {
    if (-not $AllowMissingWhisper) {
        throw "Unable to locate whisper-cli.exe and ggml-base-q5_1.bin for the portable package."
    }
    "Whisper Local Speech-to-Text was not bundled in this developer build." |
        Set-Content (Join-Path $BundledWhisperTools "MISSING_DEPENDENCY.txt") -Encoding UTF8
}
else {
    Copy-Item (Join-Path $resolvedWhisperDirectory "*") $BundledWhisperTools -Recurse -Force
    Copy-Item $resolvedWhisperModel (Join-Path $BundledWhisperModels "ggml-base-q5_1.bin") -Force
    Copy-WhisperNotices -SourceDirectory $resolvedWhisperDirectory -SourceModelPath $resolvedWhisperModel
}

$Zip = "$Package.zip"
Compress-Archive -Path "$Package\*" -DestinationPath $Zip -Force
$Hash = (Get-FileHash $Zip -Algorithm SHA256).Hash.ToLowerInvariant()
"$Hash  $(Split-Path $Zip -Leaf)" | Set-Content "$Zip.sha256" -Encoding ASCII

Write-Host "Portable folder: $Package"
Write-Host "Portable ZIP: $Zip"
Write-Host "SHA-256: $Hash"
if ($null -ne $resolvedFfmpegDirectory) { Write-Host "Bundled FFmpeg from: $resolvedFfmpegDirectory" }
if ($null -ne $resolvedWhisperDirectory) { Write-Host "Bundled Whisper from: $resolvedWhisperDirectory" }
if ($null -ne $resolvedWhisperModel) { Write-Host "Bundled Whisper model from: $resolvedWhisperModel" }
