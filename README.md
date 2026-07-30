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

Status: `passed_with_warnings`

Windows CI compiles the application, runs unit and real-media integration tests, verifies the `asInvoker` manifest, builds the self-contained portable package, expands the ZIP, starts the bundled FFmpeg/FFprobe binaries, runs the packaged worker dependency doctor, creates test media in a Thai path containing spaces, and reads every ZIP entry to detect corruption.

A manual end-to-end UI smoke test on the user's Windows 10/11 machine is still required before calling the phase fully accepted.

See:

- [`docs/PHASE1_ARCHITECTURE.md`](docs/PHASE1_ARCHITECTURE.md)
- [`docs/IMPLEMENTATION_STATUS.md`](docs/IMPLEMENTATION_STATUS.md)

## Technology stack

- .NET 8
- WPF
- FFmpeg / FFprobe as bundled local tools in CI portable builds
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

Portable packaging requires real `ffmpeg.exe` and `ffprobe.exe` so the script cannot accidentally produce a package with a non-working Render function:

```powershell
pwsh ./scripts/build-portable.ps1 -FfmpegDirectory "C:\path\to\ffmpeg\bin"
```

Alternatively, set `AUTOCUT_BUNDLE_FFMPEG_DIR` or install FFmpeg in a location the build script can resolve. The generated ZIP contains:

```text
AutoCutStudio.exe
AutoCutStudio.Worker.exe
tools/ffmpeg/ffmpeg.exe
tools/ffmpeg/ffprobe.exe
third_party/ffmpeg/bundle-manifest.json
```

The application resolves tools in this order:

1. paths supplied through `AUTOCUT_FFMPEG_PATH` and `AUTOCUT_FFPROBE_PATH`,
2. `tools/ffmpeg/` beside the application,
3. beside the application executable,
4. a directory in `PATH`.

Dependency diagnostic command:

```powershell
./AutoCutStudio.Worker.exe --doctor
```

It returns exit code `0` only when the same `ToolLocator` used by real media jobs detects both bundled tools.

## Privacy and safety

- Source media is opened read-only and is never overwritten.
- Export names are versioned (`name.mp4`, `name_v2.mp4`, ...).
- FFmpeg and FFprobe are launched with `ProcessStartInfo.ArgumentList`; user text is never concatenated into a shell command.
- No cloud upload or API is used in Phase 1.
- Partial output is written to a temporary file and moved to the final path only after FFmpeg succeeds.
- Logs do not contain API keys because Phase 1 has no API provider.
