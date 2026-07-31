# AutoCut Studio — Portable Release

This repository contains the complete AutoCut Studio source, Worker, tests, packaging scripts and Windows release automation.

## Downloadable application

A release package is produced as:

```text
AutoCutStudio-win-x64.zip
├── AutoCutStudio.exe
├── AutoCutStudio.Worker.exe
├── tools/ffmpeg/
├── tools/whisper/
├── models/whisper/
├── models/opencv/
├── plugins/
├── third_party/
└── docs/
```

The ZIP is self-contained for Windows 10/11 x64 and uses an `asInvoker` application manifest. Application settings, cache, models, logs and jobs are stored outside Program Files.

## Build a validated package from GitHub

1. Open the repository **Actions** page.
2. Select **AutoCut Studio Portable Release**.
3. Choose **Run workflow**.
4. Enter a semantic version such as `0.1.0`.
5. Keep **Create or update a GitHub Release** enabled to publish downloadable assets.
6. Keep **Prerelease** enabled until the manual Windows acceptance checklist passes.

The workflow performs all of the following before publishing:

- restores and builds the .NET 8 solution;
- runs the complete automated test suite;
- downloads pinned FFmpeg, FFprobe, whisper.cpp and OpenCV models;
- verifies the SHA-256 of every pinned executable and model;
- builds the self-contained Windows App and Worker;
- verifies the `asInvoker` manifest;
- expands the final ZIP and checks every required file;
- runs `AutoCutStudio.Worker.exe --doctor` from the portable package;
- creates and probes real media from a Thai path containing spaces;
- verifies the final ZIP checksum;
- writes `AutoCutStudio-release-manifest.json`;
- uploads the ZIP, checksum and manifest as Actions artifacts;
- creates or updates a GitHub Release when publishing is enabled.

## Local Windows build

Prerequisites:

- Windows 10 or Windows 11 x64
- PowerShell 7
- .NET 8 SDK
- Chocolatey

Run:

```powershell
git clone https://github.com/thanawat150/VideoCut.git
cd VideoCut

./scripts/prepare-release-dependencies.ps1
dotnet restore AutoCutStudio.sln
dotnet build AutoCutStudio.sln -c Release
dotnet test AutoCutStudio.sln -c Release
./scripts/build-portable.ps1
```

Outputs:

```text
artifacts/AutoCutStudio-win-x64/
artifacts/AutoCutStudio-win-x64.zip
artifacts/AutoCutStudio-win-x64.zip.sha256
```

## First launch

1. Extract the inner `AutoCutStudio-win-x64.zip` into a normal writable folder.
2. Run `AutoCutStudio.exe` without Administrator privileges.
3. Create a Project in a user-writable location.
4. Import copies of test media before processing production media.
5. Review the proposed edits before approving automatic cuts.
6. Confirm the output folder and available disk space before rendering.

Source media is opened read-only and must never be selected as an output path.

## Optional Local AI providers

The main portable package bundles the local media stack used by the validated workflows:

- FFmpeg and FFprobe;
- whisper.cpp and multilingual model;
- YuNet face detection model;
- YOLOX object detection model.

ComfyUI and Piper are optional external local providers. They are not silently installed or bundled because their model selection, storage size and licenses depend on the user's setup. AutoCut Studio reports these providers as unavailable until valid loopback endpoints, executable paths and model paths are configured.

## Manual release sign-off

Automated acceptance cannot prove hardware-specific UI behavior. Before changing a release from prerelease to stable, test the extracted package on the target Windows machine:

- application starts without Administrator privileges;
- preview video and audio play correctly;
- Thai text, vowels and tone marks render correctly;
- import, seek, trim, split, delete, undo and redo work interactively;
- pause, resume, cancel and retry update from real Worker state;
- CPU export works;
- available NVENC, Quick Sync or AMF hardware encoding is detected and falls back safely;
- a Thai path and a path containing spaces work end to end;
- source file hash and modified time do not change;
- the output opens and contains the expected video and audio streams;
- installed Windows voices behave correctly;
- Windows Firewall and LAN controls are reviewed if Mobile Control is enabled;
- configured ComfyUI, Piper or cloud providers show their real availability and errors;
- crash recovery and reopening an existing Project work.

Do not label a build as stable until this manual sign-off is recorded for the intended hardware.

## Release assets

Each published version contains:

- `AutoCutStudio-win-x64.zip`
- `AutoCutStudio-win-x64.zip.sha256`
- `AutoCutStudio-release-manifest.json`

Verify a downloaded package in PowerShell:

```powershell
Get-FileHash .\AutoCutStudio-win-x64.zip -Algorithm SHA256
Get-Content .\AutoCutStudio-win-x64.zip.sha256
```

The two SHA-256 values must match before extraction.
