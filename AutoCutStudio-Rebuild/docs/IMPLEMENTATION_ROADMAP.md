# AutoCut Studio Rebuild — Implementation Roadmap

This roadmap combines the current product gaps with curated WPF/MVVM and FFmpeg/Whisper skill guidance. It is intentionally ordered to finish the core editor before expanding cloud or generative-AI features.

## Release R1 — One product and truthful readiness

### 1. Canonical product and release cleanup

- Mark `AutoCutStudio-Rebuild/` as the primary product for new development.
- Label legacy and experimental implementations clearly.
- Separate Stable and Preview releases.
- Provide one permanent latest-download path and one known-issues page.

### 2. System Doctor

Checks:

- FFmpeg and FFprobe execute from the portable package.
- Whisper CLI and model availability.
- Optional OpenCV model availability.
- NVENC, QSV, and AMF through real encoder checks.
- Free disk space and output write permission.
- Thai caption fonts.
- Windows voices and configured cloud/local providers.

Definition of done:

- Every result is Ready, Warning, Unavailable, or Failed.
- Optional failures do not block normal cutting/export.
- Diagnostic export excludes API keys and private paths where possible.
- Unit tests cover state mapping and failure messages.

## Release R2 — Timeline users can edit with

### 1. Timeline foundation

- time-based geometry independent from screen pixels
- playhead, zoom, horizontal scroll, snap, selection
- thumbnail cache and waveform cache
- video, audio, music, caption, and overlay tracks
- trim handles, split, move, ripple delete, track lock/mute/hide
- keyboard shortcuts and one undo transaction per action

Definition of done:

- A user can import, arrange, trim, split, preview, undo, and export without editing numeric fields.
- A 60-minute project remains responsive using virtualization/caching.
- Source-media ranges remain non-destructive.
- Timeline and preview seek stay synchronized.

### 2. Export Center foundation

- Draft, Standard, and High quality profiles
- CPU plus verified GPU options
- TikTok/Reels/Shorts 9:16, landscape 16:9, square 1:1, and lightweight sharing presets
- versioned batch outputs, manifest, SHA-256, and FFprobe validation

## Release R3 — Edit speech by editing text

### 1. Transcript workspace

- local Whisper transcription with stable segment IDs and timecodes
- click segment to seek preview
- search and correction without losing original timing
- visible language and low-confidence states

### 2. Review-gated text editing

- deleted text creates a proposed EDL/timeline change
- silence/filler candidates include reasons and time ranges
- conservative Thai filler defaults
- accept/reject individually or in reviewed batches

Definition of done:

- No transcript edit mutates source media.
- Accepted proposals create undoable timeline edits.
- Rejected proposals leave the timeline unchanged.
- Real-media tests compare expected and rendered durations.

## Release R4 — Thai caption production

- Thai-aware line breaking and readable line-length guidance
- editable timing and text
- SRT and ASS support
- platform safe zones
- standard, minimal, and emphasized presets
- font readiness check and short sample render
- export subtitle-only or burn into video

Definition of done:

- Thai captions display without missing glyphs.
- Style is separate from text/timing.
- A short sample can be approved before the full render.
- Caption exports pass real-media and Thai-path tests.

## Release R5 — Face tracking, reframe, and privacy

- local face detection and temporal tracking
- reviewable track list with confidence
- select primary subject for 9:16 reframe
- select privacy targets for blur
- visible tracking-loss fallback
- preview before final render

Definition of done:

- Detection never silently becomes an approved crop/blur.
- Lost tracking uses a safe fallback and marks the affected range.
- Output is FFprobe-validated and the applied track IDs are recorded in the manifest.

## Later releases

After R1–R5 pass manual Windows smoke testing:

- highlight scoring with visible reasons
- project-local B-roll matching and approval
- hook/CTA assistance
- beat-aware music editing
- multicam and nested sequences
- script-to-video and cloud generation
- mobile control and authenticated social publishing
- declarative plugin marketplace

## Required implementation discipline

Every feature PR must include:

- user problem and non-goals
- typed data/model changes
- failure/cancel/unavailable states
- source-media protection
- unit and real-media tests
- Thai path/text coverage where relevant
- manual validation steps
- documentation and release notes

Repository agents should load the matching skills under `.github/skills/` before making changes.