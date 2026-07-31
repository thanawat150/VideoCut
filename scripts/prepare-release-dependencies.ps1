param(
    [string]$WhisperVersion = "v1.8.6",
    [string]$FfmpegChocolateyVersion = "8.1.2"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$Root = Split-Path -Parent $PSScriptRoot
$ToolsRoot = Join-Path $Root ".tools"
$WhisperDownload = Join-Path $ToolsRoot "whisper-download"
$WhisperDirectory = Join-Path $ToolsRoot "whisper"
$WhisperModelDirectory = Join-Path $ToolsRoot "whisper-model"
$VisionModelDirectory = Join-Path $ToolsRoot "vision-models"
$DependencyManifestPath = Join-Path $ToolsRoot "release-dependencies.json"

$Expected = [ordered]@{
    ffmpeg = "1326dde4c84ff1f96fe6b8916c5bed29e163e9b5dccf995f6f3db069d143ec5e"
    ffprobe = "b49ccc7c6547b141ad5a2f6ec69cc04323d7133d7704d70b331b904c63eecb07"
    whisper_cli = "111bd344b7bf0356818f2795a525cb5240ed0a99028fa9f3f1c68b4ff5b17b91"
    whisper_model = "422f1ae452ade6f30a004d7e5c6a43195e4433bc370bf23fac9cc591f01a8898"
    face_model = "8f2383e4dd3cfbb4553ea8718107fc0423210dc964f9f4280604804ed2552fa4"
    object_model = "c5c2d13e59ae883e6af3b45daea64af4833a4951c92d116ec270d9ddbe998063"
}

function Get-Sha256 {
    param([Parameter(Mandatory)][string]$Path)
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Assert-FileHash {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$ExpectedHash,
        [Parameter(Mandatory)][string]$DisplayName
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "$DisplayName is missing: $Path"
    }

    $actual = Get-Sha256 $Path
    if ($actual -ne $ExpectedHash.ToLowerInvariant()) {
        throw "$DisplayName checksum mismatch. Expected $ExpectedHash but received $actual."
    }
}

function Download-File {
    param(
        [Parameter(Mandatory)][string]$Uri,
        [Parameter(Mandatory)][string]$Destination
    )

    $directory = Split-Path $Destination -Parent
    New-Item $directory -ItemType Directory -Force | Out-Null
    curl.exe -L --fail --retry 4 --retry-delay 3 $Uri -o $Destination
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $Destination -PathType Leaf)) {
        throw "Download failed: $Uri"
    }
}

function Resolve-BundledFfmpegDirectory {
    $roots = @($env:ChocolateyInstall, "C:\ProgramData\chocolatey") |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
        Select-Object -Unique

    foreach ($root in $roots) {
        $lib = Join-Path $root "lib"
        if (-not (Test-Path -LiteralPath $lib -PathType Container)) { continue }

        $candidates = Get-ChildItem -LiteralPath $lib -Filter "ffmpeg.exe" -File -Recurse -ErrorAction SilentlyContinue |
            Where-Object { $_.Length -gt 1MB } |
            Sort-Object Length -Descending

        foreach ($ffmpeg in $candidates) {
            $ffprobe = Join-Path $ffmpeg.DirectoryName "ffprobe.exe"
            if ((Test-Path -LiteralPath $ffprobe -PathType Leaf) -and (Get-Item -LiteralPath $ffprobe).Length -gt 1MB) {
                return $ffmpeg.DirectoryName
            }
        }
    }

    throw "Unable to locate full ffmpeg.exe and ffprobe.exe binaries from Chocolatey."
}

Write-Host "Preparing pinned AutoCut Studio release dependencies..."
New-Item $ToolsRoot -ItemType Directory -Force | Out-Null
Remove-Item $WhisperDownload,$WhisperDirectory,$WhisperModelDirectory,$VisionModelDirectory -Recurse -Force -ErrorAction SilentlyContinue
New-Item $WhisperDownload,$WhisperDirectory,$WhisperModelDirectory,$VisionModelDirectory -ItemType Directory -Force | Out-Null

choco install ffmpeg --version=$FfmpegChocolateyVersion -y --no-progress --allow-downgrade
if ($LASTEXITCODE -ne 0) { throw "Chocolatey FFmpeg installation failed." }

$FfmpegDirectory = Resolve-BundledFfmpegDirectory
$FfmpegPath = Join-Path $FfmpegDirectory "ffmpeg.exe"
$FfprobePath = Join-Path $FfmpegDirectory "ffprobe.exe"
Assert-FileHash $FfmpegPath $Expected.ffmpeg "FFmpeg"
Assert-FileHash $FfprobePath $Expected.ffprobe "FFprobe"

$WhisperArchive = Join-Path $env:RUNNER_TEMP "whisper-bin-x64-$WhisperVersion.zip"
Download-File "https://github.com/ggml-org/whisper.cpp/releases/download/$WhisperVersion/whisper-bin-x64.zip" $WhisperArchive
Expand-Archive -LiteralPath $WhisperArchive -DestinationPath $WhisperDownload -Force
$WhisperCliSource = Get-ChildItem -LiteralPath $WhisperDownload -Filter "whisper-cli.exe" -File -Recurse |
    Select-Object -First 1
if ($null -eq $WhisperCliSource) { throw "The official whisper.cpp archive does not contain whisper-cli.exe." }
Copy-Item -Path (Join-Path $WhisperCliSource.DirectoryName "*") -Destination $WhisperDirectory -Recurse -Force
$WhisperCliPath = Join-Path $WhisperDirectory "whisper-cli.exe"
Assert-FileHash $WhisperCliPath $Expected.whisper_cli "whisper.cpp CLI"

$WhisperModelPath = Join-Path $WhisperModelDirectory "ggml-base-q5_1.bin"
Download-File "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-base-q5_1.bin?download=true" $WhisperModelPath
Assert-FileHash $WhisperModelPath $Expected.whisper_model "Whisper multilingual model"
Download-File "https://raw.githubusercontent.com/ggml-org/whisper.cpp/$WhisperVersion/LICENSE" (Join-Path $WhisperDirectory "LICENSE-whisper.cpp.txt")

$FaceModelPath = Join-Path $VisionModelDirectory "face_detection_yunet_2023mar.onnx"
$ObjectModelPath = Join-Path $VisionModelDirectory "object_detection_yolox_2022nov.onnx"
Download-File "https://media.githubusercontent.com/media/opencv/opencv_zoo/refs/heads/main/models/face_detection_yunet/face_detection_yunet_2023mar.onnx" $FaceModelPath
Download-File "https://media.githubusercontent.com/media/opencv/opencv_zoo/refs/heads/main/models/object_detection_yolox/object_detection_yolox_2022nov.onnx" $ObjectModelPath
Download-File "https://raw.githubusercontent.com/opencv/opencv_zoo/main/LICENSE" (Join-Path $VisionModelDirectory "LICENSE-opencv-zoo.txt")
Assert-FileHash $FaceModelPath $Expected.face_model "YuNet face model"
Assert-FileHash $ObjectModelPath $Expected.object_model "YOLOX object model"

& $FfmpegPath -version | Select-Object -First 1
if ($LASTEXITCODE -ne 0) { throw "Pinned FFmpeg failed to start." }
& $FfprobePath -version | Select-Object -First 1
if ($LASTEXITCODE -ne 0) { throw "Pinned FFprobe failed to start." }
& $WhisperCliPath -h *> $null
if ($LASTEXITCODE -ne 0) { throw "Pinned whisper.cpp CLI failed to start." }

$EnvironmentValues = [ordered]@{
    AUTOCUT_BUNDLE_FFMPEG_DIR = $FfmpegDirectory
    AUTOCUT_WHISPER_CLI_PATH = $WhisperCliPath
    AUTOCUT_WHISPER_MODEL_PATH = $WhisperModelPath
    AUTOCUT_BUNDLE_WHISPER_DIR = $WhisperDirectory
    AUTOCUT_BUNDLE_WHISPER_MODEL = $WhisperModelPath
    AUTOCUT_FACE_MODEL_PATH = $FaceModelPath
    AUTOCUT_OBJECT_MODEL_PATH = $ObjectModelPath
    AUTOCUT_BUNDLE_FACE_MODEL = $FaceModelPath
    AUTOCUT_BUNDLE_OBJECT_MODEL = $ObjectModelPath
}

if (-not [string]::IsNullOrWhiteSpace($env:GITHUB_ENV)) {
    foreach ($entry in $EnvironmentValues.GetEnumerator()) {
        "$($entry.Key)=$($entry.Value)" | Out-File -LiteralPath $env:GITHUB_ENV -Append -Encoding utf8
    }
}

[ordered]@{
    prepared_at_utc = [DateTimeOffset]::UtcNow.ToString("O")
    ffmpeg_chocolatey_version = $FfmpegChocolateyVersion
    whisper_cpp_version = $WhisperVersion
    files = [ordered]@{
        ffmpeg = [ordered]@{ path = $FfmpegPath; sha256 = Get-Sha256 $FfmpegPath }
        ffprobe = [ordered]@{ path = $FfprobePath; sha256 = Get-Sha256 $FfprobePath }
        whisper_cli = [ordered]@{ path = $WhisperCliPath; sha256 = Get-Sha256 $WhisperCliPath }
        whisper_model = [ordered]@{ path = $WhisperModelPath; sha256 = Get-Sha256 $WhisperModelPath }
        face_model = [ordered]@{ path = $FaceModelPath; sha256 = Get-Sha256 $FaceModelPath }
        object_model = [ordered]@{ path = $ObjectModelPath; sha256 = Get-Sha256 $ObjectModelPath }
    }
} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $DependencyManifestPath -Encoding utf8

Write-Host "Release dependencies are ready."
Write-Host "Dependency manifest: $DependencyManifestPath"
