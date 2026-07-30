# AutoCut Studio feature matrix

Status values:

- `ready`: implemented and backed by a real local workflow.
- `validated`: automated Windows CI has processed real generated media.
- `in_progress`: code exists but the current commit has not passed the full portable smoke test yet.
- `planned`: not exposed as ready in the application.
- `dependency_gated`: implementation requires an explicit local model, provider, credential, or user asset.

## Phase 1 — Core Video Editor

| Feature | Status | Notes |
|---|---|---|
| Project manager / reopen / autosave | validated | Atomic JSON, backups, recent project and recovery state. |
| MP4 import and FFprobe | validated | Source is referenced read-only. |
| Preview | ready | WPF `MediaElement`; depends on Windows codecs. |
| Timeline / split / trim / delete | validated | Undo and redo included. |
| Audio waveform | planned | Not displayed yet; must not be represented as complete. |
| Export MP4 | validated | Separate worker, real progress, versioned output and QA. |
| Job queue / pause / resume / cancel | validated | Resume restarts the active atomic encode step. |

## Phase 2 — Speech Editing

| Feature | Status | Notes |
|---|---|---|
| Silence detection and removal | validated | FFmpeg `silencedetect`, three presets, reviewable plan and real export. |
| Speech-to-text | in_progress | Local whisper.cpp multilingual model; awaiting full portable smoke-test result for this commit. |
| Transcript editing and search | in_progress | Editable segment text, search and persistence. |
| Subtitle / SRT export | in_progress | SRT is generated from the edited transcript. |
| Remove filler words | in_progress | Conservatively marks filler-only transcript segments; does not cut words inside a normal sentence. |

## Phase 3 — Social Media Automation

| Feature | Status | Notes |
|---|---|---|
| Highlight detection | planned | Will use transcript and media evidence and require review. |
| Shorts generation | planned | No UI button is exposed as ready. |
| Auto reframe | planned | Face-aware tracking is deferred to Phase 5; Phase 3 will start with an inspectable reframe strategy. |
| Animated captions | planned | SRT exists first; burned-in animated ASS rendering is next. |
| Hook and CTA | planned | Must be previewed before rendering. |
| Platform export presets | planned | TikTok, Shorts, Reels and YouTube profiles. |

## Phase 4 — Audio and Visual Enhancement

All features are `planned`: noise reduction, voice enhancement, color correction, stabilization, beat detection and music ducking.

## Phase 5 — Advanced AI

All features are `planned` or will be `dependency_gated`: B-roll suggestion, object detection, face tracking, privacy blur, template/local text-to-video, consent-based local voiceover and document-to-video.

## Phase 6 — Professional Workflow

All features are `planned` or will be `dependency_gated`: multicam, keyframes, nested sequences, plugin system, collaboration, cloud providers and social publishing.

## Release rule

A feature moves to `ready` only when its UI calls a real implementation. It moves to `validated` only after Windows CI or a documented human smoke test verifies a real input and readable output. Compilation alone is not sufficient.
