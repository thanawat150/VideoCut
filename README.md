# AutoCut Studio

AutoCut Studio is a Windows-first, local-first desktop video editor whose **Pixel Agents only change state when real backend jobs emit events**.

This repository currently implements the **Phase 1 core workflow**:

1. Create or open a project.
2. Import an MP4 file without modifying it.
3. Inspect the media with FFprobe.
4. Preview the source with the Windows media stack.
5. Set In/Out points, split timeline segments, delete segments, and undo/redo timeline edits.
6. Create a persistent timeline export job.
7. Run FFmpeg in a separate worker process.
8. Report real FFmpeg progress through `-progress pipe:1`.
9. Validate the output with FFprobe and SHA-256.
10. Store project, job, progress, logs, events, reports, and output manifests.
11. Open the exported file.

## Current status

Status: `in_progress`

The source code and CI workflow are present. A successful Windows CI run is required before changing the status to `passed` or `passed_with_warnings`.

See:

- [`docs/PHASE1_ARCHITECTURE.md`](docs/PHASE1_ARCHITECTURE.md)
- [`docs/IMPLEMENTATION_STATUS.md`](docs/IMPLEMENTATION_STATUS.md)

## Technology stack

- .NET 8
- WPF
- FFmpeg / FFprobe as external local tools
- Separate .NET worker process
- JSON project and job persistence
- xUnit integration tests
- GitHub Actions on `windows-latest`

## Build

```powershell
dotnet restore AutoCutStudio.sln
dotnet build AutoCutStudio.sln -c Release
dotnet test AutoCutStudio.sln -c Release
```

Portable packaging:

```powershell
pwsh ./scripts/build-portable.ps1
```

The portable package does not silently download FFmpeg. Place `ffmpeg.exe` and `ffprobe.exe` in one of these locations:

1. `tools/ffmpeg/` beside the application,
2. beside the application executable,
3. a directory in `PATH`, or
4. paths supplied through `AUTOCUT_FFMPEG_PATH` and `AUTOCUT_FFPROBE_PATH`.

The UI reports the dependency as missing until both tools are found.

## Privacy and safety

- Source media is opened read-only and is never overwritten.
- Export names are versioned (`name.mp4`, `name_v2.mp4`, ...).
- FFmpeg and FFprobe are launched with `ProcessStartInfo.ArgumentList`; user text is never concatenated into a shell command.
- No cloud upload or API is used in Phase 1.
- Partial output is written to a temporary file and moved to the final path only after FFmpeg succeeds.
- Logs do not contain API keys because Phase 1 has no API provider.
