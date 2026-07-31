param(
    [Parameter(Mandatory = $true)]
    [string]$SourceZip,
    [Parameter(Mandatory = $true)]
    [string]$OutputDirectory,
    [int64]$PartSizeBytes = 83886080
)

$ErrorActionPreference = "Stop"
$source = (Resolve-Path $SourceZip).Path
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$outputRoot = (Resolve-Path $OutputDirectory).Path
$baseName = "AutoCutStudio-Local-AI-ComfyUI-Piper-win-x64.zip"
$buffer = New-Object byte[] (1MB)
$input = [System.IO.File]::OpenRead($source)
try {
    $partIndex = 0
    while ($input.Position -lt $input.Length) {
        $partName = "$baseName.{0:D2}.part" -f $partIndex
        $partPath = Join-Path $outputRoot $partName
        $output = [System.IO.File]::Create($partPath)
        try {
            $written = 0L
            while ($written -lt $PartSizeBytes -and $input.Position -lt $input.Length) {
                $remaining = [Math]::Min($buffer.Length, $PartSizeBytes - $written)
                $read = $input.Read($buffer, 0, [int]$remaining)
                if ($read -le 0) { break }
                $output.Write($buffer, 0, $read)
                $written += $read
            }
        }
        finally {
            $output.Dispose()
        }
        $partIndex++
    }
}
finally {
    $input.Dispose()
}

if ($partIndex -ne 5) {
    throw "Expected 5 parts but created $partIndex. Update the release workflow before publishing."
}

$packageHash = (Get-FileHash $source -Algorithm SHA256).Hash.ToLowerInvariant()
$packageHash | Set-Content (Join-Path $outputRoot "SHA256.txt") -Encoding utf8
Get-ChildItem $outputRoot -Filter "*.part" -File | Sort-Object Name | ForEach-Object {
    "{0}  {1}" -f (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $_.Name
} | Set-Content (Join-Path $outputRoot "SHA256-parts.txt") -Encoding utf8

$joinScript = @'
$ErrorActionPreference = "Stop"
$folder = Split-Path -Parent $MyInvocation.MyCommand.Path
$output = Join-Path $folder "AutoCutStudio-Local-AI-ComfyUI-Piper-win-x64.zip"
$parts = Get-ChildItem $folder -Filter "AutoCutStudio-Local-AI-ComfyUI-Piper-win-x64.zip.*.part" -File | Sort-Object Name
if ($parts.Count -ne 5) { throw "Expected 5 .part files but found $($parts.Count)." }
$stream = [System.IO.File]::Create($output)
try {
    foreach ($part in $parts) {
        Write-Host "Joining $($part.Name)"
        $input = [System.IO.File]::OpenRead($part.FullName)
        try { $input.CopyTo($stream) } finally { $input.Dispose() }
    }
}
finally { $stream.Dispose() }
$expected = (Get-Content (Join-Path $folder "SHA256.txt") | Select-Object -First 1).Trim().ToLowerInvariant()
$actual = (Get-FileHash $output -Algorithm SHA256).Hash.ToLowerInvariant()
if ($actual -ne $expected) { throw "SHA-256 mismatch. Expected $expected but got $actual" }
Write-Host "Package joined and verified: $output"
'@
$joinScript | Set-Content (Join-Path $outputRoot "Join-AutoCutStudio.ps1") -Encoding utf8

$readme = @'
AutoCut Studio Local AI - Download Instructions

1. Download all five part archives and the join-tools archive.
2. Extract all six downloaded ZIP archives into the same folder.
3. Open PowerShell in that folder and run:
   powershell -ExecutionPolicy Bypass -File .\Join-AutoCutStudio.ps1
4. The script creates AutoCutStudio-Local-AI-ComfyUI-Piper-win-x64.zip and verifies SHA-256.
5. Extract the resulting ZIP and open AutoCutStudio.exe.

All five .part files are required. Do not rename them.
ComfyUI, Piper, and their model weights are installed separately because model sizes, licenses, languages, and hardware requirements vary.
'@
$readme | Set-Content (Join-Path $outputRoot "README.txt") -Encoding utf8
Get-ChildItem $outputRoot | Format-Table Name,Length
