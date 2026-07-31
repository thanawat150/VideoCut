param([string]$Configuration = "Release", [string]$Runtime = "win-x64")
$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot
$Artifacts = Join-Path $Root "artifacts"
$Package = Join-Path $Artifacts "AutoCutStudio-Rebuild-$Runtime"
$Publish = Join-Path $Artifacts "publish"
Remove-Item $Artifacts -Recurse -Force -ErrorAction SilentlyContinue
New-Item $Package -ItemType Directory -Force | Out-Null

dotnet publish (Join-Path $Root "AutoCutStudio.Rebuild.csproj") -c $Configuration -r $Runtime --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishTrimmed=false -p:DebugType=None -o $Publish
if ($LASTEXITCODE -ne 0) { throw "Publish failed." }
Copy-Item (Join-Path $Publish "AutoCutStudio.Rebuild.exe") $Package
Copy-Item (Join-Path $Root "README.md") $Package

$ffmpeg = Get-Command ffmpeg.exe -ErrorAction SilentlyContinue
$ffprobe = Get-Command ffprobe.exe -ErrorAction SilentlyContinue
if ($ffmpeg -and $ffprobe) {
  $tools = Join-Path $Package "tools\ffmpeg"
  New-Item $tools -ItemType Directory -Force | Out-Null
  Copy-Item $ffmpeg.Source $tools
  Copy-Item $ffprobe.Source $tools
}

$zip = "$Package.zip"
Compress-Archive -Path "$Package\*" -DestinationPath $zip -Force
$hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  $(Split-Path $zip -Leaf)" | Set-Content "$zip.sha256" -Encoding ASCII
Write-Host "Portable ZIP: $zip"
Write-Host "SHA-256: $hash"
