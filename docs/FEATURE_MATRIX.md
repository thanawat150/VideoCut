# AutoCut Studio feature matrix

Status values:

- `validated`: automated Windows CI processed real generated media or verified the real persistence/delivery path.
- `ready`: implemented and connected to a real local workflow; manual hardware/UI sign-off may remain.
- `dependency_gated`: implemented but requires a user asset, installed Windows voice or authenticated external provider.
- `review_gated`: the system proposes or prepares work but requires explicit user review before execution.

## Phase 1 — Core Video Editor

| Feature | Status | Notes |
|---|---|---|
| Project manager / reopen / autosave | validated | Atomic JSON, backups, recent project and recovery state. |
| MP4 import and FFprobe | validated | Source is referenced read-only. |
| Preview | ready | WPF `MediaElement`; hardware and Windows codec dependent. |
| Timeline / split / trim / delete | validated | Undo and redo included. |
| Export MP4 | validated | Separate Worker, real progress, versioned output and QA. |
| Job queue / pause / resume / cancel | validated | Atomic encode steps can be resumed by restarting safely. |
| Pixel Office / Agent Event Bus | validated | Agent state changes come from real events. |

## Phase 2 — Speech Editing

| Feature | Status | Notes |
|---|---|---|
| Silence detection and removal | validated | FFmpeg `silencedetect`, reviewable plan and real export. |
| Speech-to-text | validated | Bundled local `whisper.cpp` multilingual model. |
| Transcript editing and search | validated | Editable segment text, search and persistence. |
| Subtitle / SRT export | validated | Generated from the edited transcript. |
| Filler review | review_gated | Conservatively marks filler-only segments; does not pretend to remove words without timestamps. |
| Text-based timeline editing | validated | Transcript ranges become non-destructive timeline segments. |

## Phase 3 — Social Media Automation

| Feature | Status | Notes |
|---|---|---|
| Highlight detection | validated | Transcript-ranked candidates with visible scores and reasons. |
| Shorts generation | validated | Up to three reviewed clips per batch. |
| Auto reframe | validated | Center crop or fit/pad; face-aware tracking is used only in Phase 5 privacy analysis. |
| Animated captions | validated | Burned-in ASS rendering from transcript timestamps. |
| Hook and CTA | validated | Editable and previewed before job creation. |
| Platform presets | validated | Vertical, landscape and square presets. |

## Phase 4 — Audio and Visual Enhancement

| Feature | Status | Notes |
|---|---|---|
| Noise reduction | validated | FFmpeg `afftdn` preset. |
| Voice enhancement | validated | High/low pass and dynamic normalization. |
| Color correction | validated | Safe preset values, not raw user filter strings. |
| Stabilization | validated | Basic FFmpeg stabilization; not presented as model-based stabilization. |
| Beat analysis | validated | PCM RMS/onset analysis and BPM estimate. |
| Music ducking | validated | Local user-supplied music with sidechain compression and mix. |

## Phase 5 — Advanced Local AI

| Feature | Status | Notes |
|---|---|---|
| B-roll suggestions | review_gated | Transcript/object evidence plus local project assets. |
| Object detection | validated | Bundled OpenCV Zoo YOLOX ONNX on CPU. |
| Face tracking | validated | Bundled YuNet detections linked into temporal tracks. |
| Privacy blur | review_gated | Only selected face tracks are blurred; real pixel-change test included. |
| Text/document-to-video | validated | TXT, Markdown, DOCX and PDF to editable ASS template video. |
| Local voiceover | dependency_gated | Uses installed Windows SAPI voices; no voice cloning. |

## Phase 6 — Professional Workflow

| Feature | Status | Notes |
|---|---|---|
| Multicam | validated | Reviewed switch plan and real multi-source FFmpeg render. |
| Keyframes | validated | Linear zoom, focus and audio-gain interpolation. |
| Nested sequences | validated | Persisted reusable sequence documents and Worker render. |
| Plugin system | validated | Declarative JSON export presets only; external code is not executed. |
| Cloud providers | ready | Real local/network/synced-folder delivery with SHA-256; external APIs need provider plugins. |
| Social publishing | review_gated | Creates platform outbox packages; does not claim an upload occurred. |
| Team collaboration | validated | ZIP export/import, SHA-256 verification and zip-slip protection. |

## Release rule

A feature is marked `validated` only when its UI or service reaches a real implementation and automated Windows validation checks the relevant output or persistence path. Compilation alone is not sufficient. Direct external platform publishing can only move beyond `dependency_gated` after authenticated provider tests using approved accounts and permissions.
