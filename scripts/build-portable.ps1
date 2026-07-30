param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$FfmpegDirectory = "",
    [string]$WhisperDirectory = "",
    [string]$WhisperModelPath = "",
    [string]$FaceModelPath = "",
    [string]$ObjectModelPath = "",
    [switch]$AllowMissingFfmpeg,
    [switch]$AllowMissingWhisper,
    [switch]$AllowMissingVision
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
$BundledVisionModels = Join-Path $Package "models\opencv"
$BundledPlugins = Join-Path $Package "plugins"

function Resolve-ExistingFile {
    param([string[]]$Candidates, [long]$MinimumSize = 1)
    foreach ($candidate in $Candidates | Select-Object -Unique) {
        if ([string]::IsNullOrWhiteSpace($candidate)) { continue }
        try { $fullPath = [System.IO.Path]::GetFullPath($candidate) } catch { continue }
        if ((Test-Path $fullPath -PathType Leaf) -and (Get-Item $fullPath).Length -ge $MinimumSize) {
            return $fullPath
        }
    }
    return $null
}

function Get-ValidFfmpegDirectory {
    param([string]$Candidate)
    if ([string]::IsNullOrWhiteSpace($Candidate)) { return $null }
    try { $fullPath = [System.IO.Path]::GetFullPath($Candidate) } catch { return $null }
    $ffmpeg = Join-Path $fullPath "ffmpeg.exe"
    $ffprobe = Join-Path $fullPath "ffprobe.exe"
    if (-not (Test-Path $ffmpeg -PathType Leaf) -or -not (Test-Path $ffprobe -PathType Leaf)) { return $null }
    if ((Get-Item $ffmpeg).Length -lt 1MB -or (Get-Item $ffprobe).Length -lt 1MB) { return $null }
    return $fullPath
}

function Resolve-FfmpegDirectory {
    $candidates = [System.Collections.Generic.List[string]]::new()
    if ($FfmpegDirectory) { $candidates.Add($FfmpegDirectory) }
    if ($env:AUTOCUT_BUNDLE_FFMPEG_DIR) { $candidates.Add($env:AUTOCUT_BUNDLE_FFMPEG_DIR) }
    foreach ($root in @($env:ChocolateyInstall, "C:\ProgramData\chocolatey") | Where-Object { $_ }) {
        $lib = Join-Path $root "lib"
        if (Test-Path $lib -PathType Container) {
            Get-ChildItem $lib -Filter ffmpeg.exe -File -Recurse -ErrorAction SilentlyContinue |
                Sort-Object Length -Descending |
                ForEach-Object { $candidates.Add($_.DirectoryName) }
        }
    }
    $command = Get-Command ffmpeg.exe -CommandType Application -ErrorAction SilentlyContinue
    if ($null -ne $command -and $command.Source) { $candidates.Add((Split-Path $command.Source -Parent)) }
    foreach ($candidate in $candidates | Select-Object -Unique) {
        $resolved = Get-ValidFfmpegDirectory $candidate
        if ($null -ne $resolved) { return $resolved }
    }
    return $null
}

function Resolve-WhisperDirectory {
    $candidates = @(
        $WhisperDirectory,
        $env:AUTOCUT_BUNDLE_WHISPER_DIR,
        $(if ($env:AUTOCUT_WHISPER_CLI_PATH) { Split-Path $env:AUTOCUT_WHISPER_CLI_PATH -Parent }),
        (Join-Path $Root ".tools\whisper")
    )
    foreach ($candidate in $candidates | Where-Object { $_ } | Select-Object -Unique) {
        try { $full = [System.IO.Path]::GetFullPath($candidate) } catch { continue }
        if (Test-Path (Join-Path $full "whisper-cli.exe") -PathType Leaf) { return $full }
    }
    return $null
}

function Copy-LicenseFiles {
    param([string]$SourceDirectory, [string]$DestinationDirectory, [int]$MaximumLevels = 5)
    New-Item $DestinationDirectory -ItemType Directory -Force | Out-Null
    $copied = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    $current = $SourceDirectory
    for ($level = 0; $level -lt $MaximumLevels -and $current; $level++) {
        foreach ($pattern in @("LICENSE*", "COPYING*", "NOTICE*", "README*")) {
            Get-ChildItem $current -Filter $pattern -File -ErrorAction SilentlyContinue | ForEach-Object {
                if ($copied.Add($_.Name)) { Copy-Item $_.FullName (Join-Path $DestinationDirectory $_.Name) }
            }
        }
        $parent = Split-Path $current -Parent
        if (-not $parent -or $parent -eq $current) { break }
        $current = $parent
    }
}

function Write-FfmpegNotice {
    param([string]$SourceDirectory)
    $notice = Join-Path $Package "third_party\ffmpeg"
    Copy-LicenseFiles $SourceDirectory $notice
    $ffmpeg = Join-Path $BundledFfmpegTools "ffmpeg.exe"
    $ffprobe = Join-Path $BundledFfmpegTools "ffprobe.exe"
    $version = ((& $ffmpeg -version 2>&1 | Select-Object -First 1) -join "").Trim()
    if ($LASTEXITCODE -ne 0) { throw "Bundled ffmpeg.exe failed to start." }
    [ordered]@{
        product = "FFmpeg"; bundled_at_utc = [DateTimeOffset]::UtcNow.ToString("O")
        version = $version
        ffmpeg_sha256 = (Get-FileHash $ffmpeg -Algorithm SHA256).Hash.ToLowerInvariant()
        ffprobe_sha256 = (Get-FileHash $ffprobe -Algorithm SHA256).Hash.ToLowerInvariant()
        project_website = "https://ffmpeg.org/"
    } | ConvertTo-Json | Set-Content (Join-Path $notice "bundle-manifest.json") -Encoding UTF8
}

function Write-WhisperNotice {
    param([string]$SourceDirectory, [string]$SourceModelPath)
    $notice = Join-Path $Package "third_party\whisper.cpp"
    Copy-LicenseFiles $SourceDirectory $notice
    $cli = Join-Path $BundledWhisperTools "whisper-cli.exe"
    $model = Join-Path $BundledWhisperModels "ggml-base-q5_1.bin"
    & $cli -h *> $null
    if ($LASTEXITCODE -ne 0) { throw "Bundled whisper-cli.exe failed to start." }
    [ordered]@{
        product = "whisper.cpp"; release = "v1.8.6"
        bundled_at_utc = [DateTimeOffset]::UtcNow.ToString("O")
        cli_sha256 = (Get-FileHash $cli -Algorithm SHA256).Hash.ToLowerInvariant()
        model = "ggml-base-q5_1.bin"
        model_size_bytes = (Get-Item $model).Length
        model_sha256 = (Get-FileHash $model -Algorithm SHA256).Hash.ToLowerInvariant()
        project_website = "https://github.com/ggml-org/whisper.cpp"
    } | ConvertTo-Json | Set-Content (Join-Path $notice "bundle-manifest.json") -Encoding UTF8
}

function Write-VisionNotice {
    param([string]$FaceSource, [string]$ObjectSource)
    $notice = Join-Path $Package "third_party\opencv-zoo"
    New-Item $notice -ItemType Directory -Force | Out-Null
    $face = Join-Path $BundledVisionModels "face_detection_yunet_2023mar.onnx"
    $object = Join-Path $BundledVisionModels "object_detection_yolox_2022nov.onnx"
    foreach ($candidate in @((Split-Path $FaceSource -Parent), (Split-Path $ObjectSource -Parent))) {
        Copy-LicenseFiles $candidate $notice 4
    }
    [ordered]@{
        product = "OpenCV Zoo models"
        bundled_at_utc = [DateTimeOffset]::UtcNow.ToString("O")
        face_model = (Split-Path $face -Leaf)
        face_size_bytes = (Get-Item $face).Length
        face_sha256 = (Get-FileHash $face -Algorithm SHA256).Hash.ToLowerInvariant()
        object_model = (Split-Path $object -Leaf)
        object_size_bytes = (Get-Item $object).Length
        object_sha256 = (Get-FileHash $object -Algorithm SHA256).Hash.ToLowerInvariant()
        model_repository = "https://github.com/opencv/opencv_zoo"
        runtime = "OpenCvSharp / OpenCV CPU"
        licensing_note = "Third-party model files are bundled with upstream notices when available."
    } | ConvertTo-Json | Set-Content (Join-Path $notice "bundle-manifest.json") -Encoding UTF8
}

Remove-Item $Artifacts -Recurse -Force -ErrorAction SilentlyContinue
foreach ($directory in @(
    $Package, $BundledFfmpegTools, $BundledWhisperTools,
    $BundledWhisperModels, $BundledVisionModels, $BundledPlugins, (Join-Path $Package "docs")
)) { New-Item $directory -ItemType Directory -Force | Out-Null }

dotnet publish (Join-Path $Root "src\AutoCutStudio.App\AutoCutStudio.App.csproj") `
    -c $Configuration -r $Runtime --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:PublishTrimmed=false -p:DebugType=None -o $AppOut
if ($LASTEXITCODE -ne 0) { throw "Application publish failed." }

dotnet publish (Join-Path $Root "src\AutoCutStudio.Worker\AutoCutStudio.Worker.csproj") `
    -c $Configuration -r $Runtime --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:PublishTrimmed=false -p:DebugType=None -o $WorkerOut
if ($LASTEXITCODE -ne 0) { throw "Worker publish failed." }

Copy-Item (Join-Path $AppOut "AutoCutStudio.exe") $Package
Copy-Item (Join-Path $WorkerOut "AutoCutStudio.Worker.exe") $Package
Copy-Item (Join-Path $Root "README.md") $Package
foreach ($doc in @("PHASE1_ARCHITECTURE.md", "IMPLEMENTATION_STATUS.md", "FEATURE_MATRIX.md")) {
    $source = Join-Path $Root "docs\$doc"
    if (Test-Path $source -PathType Leaf) { Copy-Item $source (Join-Path $Package "docs") }
}
$pluginRoot = Join-Path $Root "plugins"
if (Test-Path $pluginRoot -PathType Container) {
    Copy-Item (Join-Path $pluginRoot "*") $BundledPlugins -Recurse -Force
}

$resolvedFfmpeg = Resolve-FfmpegDirectory
if ($null -eq $resolvedFfmpeg) {
    if (-not $AllowMissingFfmpeg) { throw "Unable to locate real ffmpeg.exe and ffprobe.exe binaries." }
    "FFmpeg missing." | Set-Content (Join-Path $BundledFfmpegTools "MISSING_DEPENDENCY.txt")
} else {
    Copy-Item (Join-Path $resolvedFfmpeg "ffmpeg.exe") $BundledFfmpegTools
    Copy-Item (Join-Path $resolvedFfmpeg "ffprobe.exe") $BundledFfmpegTools
    Write-FfmpegNotice $resolvedFfmpeg
}

$resolvedWhisperDirectory = Resolve-WhisperDirectory
$resolvedWhisperModel = Resolve-ExistingFile @(
    $WhisperModelPath, $env:AUTOCUT_BUNDLE_WHISPER_MODEL,
    $env:AUTOCUT_WHISPER_MODEL_PATH,
    (Join-Path $Root ".tools\whisper-model\ggml-base-q5_1.bin")
) 10MB
if ($null -eq $resolvedWhisperDirectory -or $null -eq $resolvedWhisperModel) {
    if (-not $AllowMissingWhisper) { throw "Unable to locate whisper-cli.exe and multilingual model." }
    "Whisper missing." | Set-Content (Join-Path $BundledWhisperTools "MISSING_DEPENDENCY.txt")
} else {
    Copy-Item (Join-Path $resolvedWhisperDirectory "*") $BundledWhisperTools -Recurse -Force
    Copy-Item $resolvedWhisperModel (Join-Path $BundledWhisperModels "ggml-base-q5_1.bin") -Force
    Write-WhisperNotice $resolvedWhisperDirectory $resolvedWhisperModel
}

$resolvedFace = Resolve-ExistingFile @(
    $FaceModelPath, $env:AUTOCUT_BUNDLE_FACE_MODEL, $env:AUTOCUT_FACE_MODEL_PATH,
    (Join-Path $Root ".tools\vision-models\face_detection_yunet_2023mar.onnx")
) 200KB
$resolvedObject = Resolve-ExistingFile @(
    $ObjectModelPath, $env:AUTOCUT_BUNDLE_OBJECT_MODEL, $env:AUTOCUT_OBJECT_MODEL_PATH,
    (Join-Path $Root ".tools\vision-models\object_detection_yolox_2022nov.onnx")
) 20MB
if ($null -eq $resolvedFace -or $null -eq $resolvedObject) {
    if (-not $AllowMissingVision) { throw "Unable to locate YuNet and YOLOX ONNX models." }
    "Computer vision models missing." | Set-Content (Join-Path $BundledVisionModels "MISSING_DEPENDENCY.txt")
} else {
    Copy-Item $resolvedFace (Join-Path $BundledVisionModels "face_detection_yunet_2023mar.onnx") -Force
    Copy-Item $resolvedObject (Join-Path $BundledVisionModels "object_detection_yolox_2022nov.onnx") -Force
    Write-VisionNotice $resolvedFace $resolvedObject
}

$Zip = "$Package.zip"
Compress-Archive -Path "$Package\*" -DestinationPath $Zip -Force
$Hash = (Get-FileHash $Zip -Algorithm SHA256).Hash.ToLowerInvariant()
"$Hash  $(Split-Path $Zip -Leaf)" | Set-Content "$Zip.sha256" -Encoding ASCII
Write-Host "Portable folder: $Package"
Write-Host "Portable ZIP: $Zip"
Write-Host "SHA-256: $Hash"
if ($resolvedFfmpeg) { Write-Host "Bundled FFmpeg from: $resolvedFfmpeg" }
if ($resolvedWhisperDirectory) { Write-Host "Bundled Whisper from: $resolvedWhisperDirectory" }
if ($resolvedWhisperModel) { Write-Host "Bundled Whisper model from: $resolvedWhisperModel" }
if ($resolvedFace) { Write-Host "Bundled YuNet model from: $resolvedFace" }
if ($resolvedObject) { Write-Host "Bundled YOLOX model from: $resolvedObject" }
Write-Host "Bundled declarative plugins: $BundledPlugins"
