# Phase 1 implementation status

## สิ่งที่ทำเสร็จ

- Solution structure for App, Core, Infrastructure, Worker, and Tests.
- Project create/open/save with atomic JSON writes and backups.
- MP4 import by reference; source files are not edited.
- FFprobe metadata parsing.
- WPF preview using `MediaElement`.
- Basic timeline with In/Out, split, delete, undo, and redo.
- Versioned output naming.
- Persistent jobs and job folders.
- Worker process using safe argument lists.
- Real FFmpeg progress parsing.
- Windows pause/resume/cancel control.
- Event-driven Phase 1 Pixel Agents.
- Output QA with FFprobe and SHA-256.
- Windows CI and portable packaging scripts.
- Portable CI package bundles real `ffmpeg.exe` and `ffprobe.exe` under `tools/ffmpeg/`.
- Worker `--doctor` command validates the same dependency lookup used by real jobs.
- Third-party bundle manifest records versions and SHA-256 values for both tools.

## สิ่งที่ทดสอบ

Windows GitHub Actions verifies:

- Restore and Release build.
- Event bus publication.
- Versioned output.
- Path traversal protection.
- Project save/load.
- Real FFmpeg trim and FFprobe validation.
- Source hash and modified time remain unchanged.
- Thai path and spaces in path.
- `asInvoker` application manifest.
- Self-contained portable application and worker.
- Portable ZIP extraction and full-entry read to detect archive corruption.
- Bundled FFmpeg starts successfully from inside the extracted portable package.
- Bundled FFprobe starts and reads real media.
- Packaged worker `--doctor` reports `ready` using app-relative `tools/ffmpeg/`.
- Bundled FFmpeg creates H.264/AAC test media in a Thai directory containing spaces.

A manual UI smoke test on a Windows 10/11 desktop is still required for preview playback, buttons, pause/resume interaction, and opening the generated output through Explorer.

## สิ่งที่ยังไม่รองรับ

- Speech, subtitles, silence/filler editing.
- Social short-form automation.
- AI analysis.
- Advanced audio, color, stabilization.
- Privacy tracking and redaction.
- Cloud providers.
- True mid-encode checkpoint resume.

Each of these is deferred by the Phase plan, not presented as ready in the UI.

## Build Artifact

- EXE Path: `artifacts/AutoCutStudio-win-x64/AutoCutStudio.exe`
- Worker Path: `artifacts/AutoCutStudio-win-x64/AutoCutStudio.Worker.exe`
- Bundled Tools: `artifacts/AutoCutStudio-win-x64/tools/ffmpeg/`
- ZIP Path: `artifacts/AutoCutStudio-win-x64.zip`
- Build Version: `0.1.0-phase1`
- Commit SHA: populated by GitHub Actions.
- SHA-256: generated beside the ZIP by `scripts/build-portable.ps1`.

## สถานะ

`passed_with_warnings`
