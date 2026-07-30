param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot
$Artifacts = Join-Path $Root "artifacts"
$Package = Join-Path $Artifacts "AutoCutStudio-$Runtime"
$AppOut = Join-Path $Artifacts "_app"
$WorkerOut = Join-Path $Artifacts "_worker"

Remove-Item $Artifacts -Recurse -Force -ErrorAction SilentlyContinue
New-Item $Package -ItemType Directory -Force | Out-Null
New-Item (Join-Path $Package "tools\ffmpeg") -ItemType Directory -Force | Out-Null
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

@"
AutoCut Studio does not silently download FFmpeg.

Place ffmpeg.exe and ffprobe.exe in this folder, or add them to PATH.
The application reports the provider as "missing" until both tools are available.
"@ | Set-Content (Join-Path $Package "tools\ffmpeg\README.txt") -Encoding UTF8

$Zip = "$Package.zip"
Compress-Archive -Path "$Package\*" -DestinationPath $Zip -Force
$Hash = (Get-FileHash $Zip -Algorithm SHA256).Hash.ToLowerInvariant()
"$Hash  $(Split-Path $Zip -Leaf)" | Set-Content "$Zip.sha256" -Encoding ASCII

Write-Host "Portable folder: $Package"
Write-Host "Portable ZIP: $Zip"
Write-Host "SHA-256: $Hash"
