---
name: autocut-video-pipeline
description: Build and validate AutoCut Studio media workflows using FFmpeg, FFprobe, Whisper, deterministic edit plans, non-destructive outputs, worker jobs, Thai captions, social presets, and real-media tests. Use for import, probing, trim, split, silence removal, transcript editing, captions, overlays, audio processing, reframing, export, progress, and media QA.
license: MIT
compatibility: AutoCutStudio-Rebuild on Windows 10/11 x64, .NET 8, WPF, FFmpeg/FFprobe, optional local Whisper and OpenCV models.
---

# AutoCut video pipeline

## Purpose

Turn editing intent into an inspectable plan and deterministic media operations. AI may propose what to keep, remove, caption, or reframe, but FFmpeg/FFprobe and the project Worker remain the execution source of truth.

## Required workflow

1. **Inventory and probe**
   - Confirm every input exists and is readable.
   - Probe streams, duration, time base, frame rate, resolution, rotation, color metadata, codecs, sample rate, and channel layout.
   - Detect variable-frame-rate, missing-audio, unsupported-codec, and corrupt-input conditions before creating a job.

2. **Create an edit plan**
   - Represent requested operations as typed project/timeline/job data, not a free-form shell command.
   - Preserve original timecodes and source references.
   - For transcript-driven editing, map text selections to reviewed time ranges before changing the timeline.

3. **Choose a deterministic operation order**
   - Default order: source selection → trim/split → silence/filler review → speed/time transforms → crop/reframe → audio processing → captions → overlays/logo/privacy effects → encode → QA.
   - Change the order only when media semantics require it, and document the reason.

4. **Execute out of process**
   - Use the existing Worker/job queue.
   - Use `ProcessStartInfo.ArgumentList`; never concatenate user text into a shell command.
   - Capture machine-readable progress, standard error, cancellation, pause/resume state, retry data, and the exact tool version.

5. **Protect media**
   - Never overwrite source media.
   - Write to a unique `.partial` output.
   - On success, probe the partial file, verify expected streams/duration, then atomically promote it to a versioned final name.
   - On failure or cancellation, keep an inspectable error log and remove or quarantine invalid partial outputs.

6. **Validate truthfully**
   - Verify that output exists, is non-empty, opens with FFprobe, contains expected video/audio/subtitle streams, and has plausible duration.
   - Hash source files before and after operations that could risk accidental mutation.
   - Never report AI/provider success without a verified artifact.

## Feature-specific guidance

### Transcript editing

- Store transcript segments with stable IDs, start/end times, language, confidence, and source-media identity.
- Editing text must create a proposed non-destructive EDL/timeline change first.
- Show deletions, filler candidates, and silence candidates for review.
- Keep conservative defaults for Thai filler removal; do not delete low-confidence speech automatically.

### Thai captions

- Support SRT and ASS as editable intermediate formats.
- Apply Thai-aware line breaking, readable line length, safe zones, stroke/shadow, and platform presets.
- Keep caption text separate from style and timing data.
- Validate font availability and render a short caption sample before a long export.

### Silence and filler removal

- Expose threshold, minimum duration, padding, and maximum removable gap.
- Merge adjacent kept segments carefully and preserve a minimum natural pause.
- Compare expected output duration against the sum of kept ranges.

### Social export

- Provide explicit presets for TikTok/Reels/Shorts 9:16, landscape 16:9, square 1:1, and lightweight sharing output.
- Presets must define dimensions, frame rate behavior, codec, rate-control strategy, audio settings, safe zones, and fast-start behavior.
- Batch exports reuse one reviewed timeline and create separately validated outputs.

### Auto reframe and privacy tracking

- Detection/tracking produces reviewable tracks; it must not directly burn a final crop or blur without user approval.
- Store track confidence and fallback framing.
- If tracking is lost, prefer a safe center/fit fallback and make the loss visible.

### Hardware encoding

- Detect NVENC, QSV, and AMF by running a real capability check.
- Keep a tested CPU fallback.
- Do not infer encoder availability only from GPU name.

## Testing requirements

For changed media behavior, add or update tests that cover the relevant subset:

- synthetic media generation with known duration and streams
- Thai and spaced paths
- video with audio, video without audio, and audio-only inputs when applicable
- cancellation and invalid input
- output duration tolerance
- subtitle burn-in/sample frame validation
- bundled-tool execution from the expanded portable ZIP
- CPU fallback and any hardware path that CI can verify safely

## Definition of done

- The operation is represented by typed data and is inspectable before execution.
- Source files remain unchanged.
- Worker progress and failure states are real.
- Output is versioned and FFprobe-validated.
- Relevant automated and real-media tests pass.
- UI shows unavailable models/providers before the user starts a job.
- Documentation identifies limits and manual validation boundaries.

## Upstream inspiration

This project skill adapts workflow concepts from `6missedcalls/video-editing-skill` (MIT): probe and validate inputs, compose editing operations in a defined order, use FFmpeg/Whisper for deterministic execution, and report the actual output. The Bash implementation is intentionally not imported because AutoCut Studio uses a Windows .NET Worker and typed process arguments. See `.github/skills/THIRD_PARTY_NOTICES.md`.