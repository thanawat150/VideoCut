# AutoCut Studio implementation status

## Phase coverage

| Phase | Scope | Implementation |
|---|---|---|
| 1 | Core editor, project, timeline, worker, QA, portable, Pixel Office | Implemented |
| 2 | Local speech-to-text, transcript, subtitle, silence/filler and text editing | Implemented |
| 3 | Highlights, shorts, reframing, animated captions, Hook/CTA and presets | Implemented |
| 4 | Noise, voice, color, stabilization, beat analysis and ducking | Implemented |
| 5 | B-roll, face tracking, object detection, privacy blur, document video and local voiceover | Implemented |
| 6 | Multicam, keyframes, nested sequences, plugins, providers, publishing and collaboration | Implemented |

## Real processing paths

- FFmpeg and FFprobe are used for media processing and QA.
- Whisper runs locally through the bundled `whisper.cpp` executable and multilingual model.
- YuNet and YOLOX run locally through OpenCvSharp/OpenCV CPU inference.
- Windows voiceover uses installed SAPI voices; it does not clone a voice.
- Multicam, keyframe and nested-sequence exports are persistent Worker jobs.
- Local/cloud-sync delivery copies and hashes a real output file.
- Social publishing creates a complete outbox package unless an authenticated provider plugin is installed.
- Collaboration export/import verifies every included file with SHA-256.

## Automated validation

Windows CI verifies:

- Restore and Release build.
- Unit and real-media integration tests.
- Project persistence, event publication and versioned output.
- Source media hashes and modified times remain unchanged.
- Real FFmpeg rendering and FFprobe output inspection.
- Real Whisper transcription.
- OpenCV model load and inference.
- Privacy Blur changes output pixels.
- Template/document video and Windows voiceover.
- Audio/visual enhancement and beat analysis.
- Multicam, keyframe and nested-sequence rendering.
- Plugin validation, delivery, social outbox and collaboration checksums.
- Thai paths and spaces in paths.
- `asInvoker` manifest.
- Self-contained portable App and Worker.
- Bundled FFmpeg, FFprobe, Whisper, YuNet and YOLOX from inside the expanded ZIP.
- Portable Worker dependency doctor.
- Required declarative plugins and third-party manifests.
- Full ZIP entry reads to detect archive corruption.

## Provider boundary

The provider framework is complete and honest about external dependencies:

- `local-folder` performs real delivery and supports desktop-synced cloud folders.
- YouTube/TikTok/Instagram/Facebook outbox packages contain video, metadata and SHA-256.
- Direct external API publishing remains disabled unless a separately authenticated provider plugin and required platform permission are present.
- The product never reports an external upload as successful when it has not occurred.

## Artifact paths

- App: `artifacts/AutoCutStudio-win-x64/AutoCutStudio.exe`
- Worker: `artifacts/AutoCutStudio-win-x64/AutoCutStudio.Worker.exe`
- FFmpeg: `artifacts/AutoCutStudio-win-x64/tools/ffmpeg/`
- Whisper: `artifacts/AutoCutStudio-win-x64/tools/whisper/`
- Models: `artifacts/AutoCutStudio-win-x64/models/`
- Plugins: `artifacts/AutoCutStudio-win-x64/plugins/`
- ZIP: `artifacts/AutoCutStudio-win-x64.zip`
- ZIP checksum: `artifacts/AutoCutStudio-win-x64.zip.sha256`

## Remaining release sign-off

The code and automated acceptance workflow can be marked complete only after the final Phase 6 CI run passes. Hardware-specific UI playback, installed voices, GPU/driver behavior and interactive usability still require a manual smoke test on the target Windows 10/11 machine before a production release is signed.

## Current status

`in_progress_until_final_phase6_ci_passes`
