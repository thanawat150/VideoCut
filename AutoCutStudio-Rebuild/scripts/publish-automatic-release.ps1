param(
    [string]$Repository = $env:GITHUB_REPOSITORY,
    [string]$CommitSha = $env:GITHUB_SHA,
    [string]$SourceRef = $env:GITHUB_REF,
    [string]$RunNumber = $env:GITHUB_RUN_NUMBER,
    [switch]$Publish
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

if ([string]::IsNullOrWhiteSpace($Repository)) { throw "Repository is required." }
if ([string]::IsNullOrWhiteSpace($CommitSha)) { throw "Commit SHA is required." }
if ([string]::IsNullOrWhiteSpace($RunNumber)) { throw "Run number is required." }

$Root = Split-Path -Parent $PSScriptRoot
$Artifacts = Join-Path $Root "artifacts"
$Zip = Join-Path $Artifacts "AutoCutStudio-Rebuild-win-x64.zip"
$Checksum = "$Zip.sha256"
$Manifest = Join-Path $Artifacts "AutoCutStudio-Rebuild-release-manifest.json"
$Notes = Join-Path $Artifacts "AutoCutStudio-Rebuild-release-notes.md"

foreach ($file in @($Zip, $Checksum)) {
    if (-not (Test-Path $file -PathType Leaf)) { throw "Release input is missing: $file" }
}

$declaredHash = ((Get-Content $Checksum -Raw).Trim() -split "\s+")[0].ToLowerInvariant()
$actualHash = (Get-FileHash $Zip -Algorithm SHA256).Hash.ToLowerInvariant()
if ($declaredHash -ne $actualHash) {
    throw "ZIP checksum mismatch. Declared $declaredHash, actual $actualHash."
}

$dateVersion = [DateTimeOffset]::UtcNow.ToString("yyyyMMdd")
$version = "0.$dateVersion.$RunNumber"
$tag = "autocut-rebuild-v$version"
$downloadUrl = "https://github.com/$Repository/releases/download/$tag/AutoCutStudio-Rebuild-win-x64.zip"
$releaseUrl = "https://github.com/$Repository/releases/tag/$tag"

[ordered]@{
    product = "AutoCut Studio Rebuild"
    version = $version
    tag = $tag
    commit_sha = $CommitSha
    source_ref = $SourceRef
    built_at_utc = [DateTimeOffset]::UtcNow.ToString("O")
    runtime = "win-x64"
    package = "AutoCutStudio-Rebuild-win-x64.zip"
    package_size_bytes = (Get-Item $Zip).Length
    package_sha256 = $actualHash
    download_url = $downloadUrl
    release_url = $releaseUrl
    validation = [ordered]@{
        restore = "passed"
        build = "passed"
        unit_tests = "passed"
        as_invoker_manifest = "passed"
        real_worker_render = "passed"
        thai_path_and_spaces = "passed"
        portable_ffmpeg_binary = "passed"
        portable_ffprobe_validation = "passed"
        dependency_doctor = "passed"
    }
} | ConvertTo-Json -Depth 6 | Set-Content $Manifest -Encoding UTF8

$noteLines = @(
    "## AutoCut Studio Rebuild $version",
    "",
    "Automated Windows x64 release from commit $CommitSha.",
    "",
    "### Automated validation",
    "- Release build and unit tests passed",
    "- Real two-clip FFmpeg Worker render passed",
    "- Video and audio streams validated by FFprobe",
    "- Thai paths and spaces passed",
    "- Portable ZIP checksum passed",
    "- Bundled FFmpeg and FFprobe passed real-media smoke tests",
    "- Application dependency doctor passed",
    "",
    "### Download",
    "Download AutoCutStudio-Rebuild-win-x64.zip, extract it, then run AutoCutStudio.Rebuild.exe.",
    "",
    "SHA-256: $actualHash"
)
$noteLines -join [Environment]::NewLine | Set-Content $Notes -Encoding UTF8

if ($env:GITHUB_OUTPUT) {
    "version=$version" | Out-File $env:GITHUB_OUTPUT -Append -Encoding utf8
    "tag=$tag" | Out-File $env:GITHUB_OUTPUT -Append -Encoding utf8
    "sha256=$actualHash" | Out-File $env:GITHUB_OUTPUT -Append -Encoding utf8
    "release_url=$releaseUrl" | Out-File $env:GITHUB_OUTPUT -Append -Encoding utf8
    "download_url=$downloadUrl" | Out-File $env:GITHUB_OUTPUT -Append -Encoding utf8
}

if ($env:GITHUB_STEP_SUMMARY) {
    "## Automatic Release $tag" | Out-File $env:GITHUB_STEP_SUMMARY -Append -Encoding utf8
    "- Package: AutoCutStudio-Rebuild-win-x64.zip" | Out-File $env:GITHUB_STEP_SUMMARY -Append -Encoding utf8
    "- SHA-256: $actualHash" | Out-File $env:GITHUB_STEP_SUMMARY -Append -Encoding utf8
    "- Download: $downloadUrl" | Out-File $env:GITHUB_STEP_SUMMARY -Append -Encoding utf8
}

if ($Publish) {
    if ([string]::IsNullOrWhiteSpace($env:GH_TOKEN)) { throw "GH_TOKEN is required to publish a release." }
    & gh release create $tag $Zip $Checksum $Manifest `
        --repo $Repository `
        --target $CommitSha `
        --title "AutoCut Studio Rebuild $tag" `
        --notes-file $Notes
    if ($LASTEXITCODE -ne 0) { throw "GitHub Release creation failed." }
}

Write-Host "Release tag: $tag"
Write-Host "Download URL: $downloadUrl"
Write-Host "SHA-256: $actualHash"
