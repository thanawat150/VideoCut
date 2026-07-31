---
name: autocut-product-roadmap
description: Prioritize and plan AutoCut Studio product work using user value, product coherence, implementation risk, validation cost, and release readiness. Use when deciding what to build next, creating issues, defining milestones, comparing features, or translating ideas into acceptance criteria.
compatibility: AutoCutStudio-Rebuild repository planning and implementation work.
---

# AutoCut product roadmap

## Goal

Build one dependable Windows video editor rather than accumulating disconnected features. Prefer improvements that reduce user confusion, shorten the path from import to export, and increase trust in local automated editing.

## Canonical direction

- `AutoCutStudio-Rebuild/` is the target for new product work.
- Legacy code may be mined for proven capabilities, but migration must preserve Rebuild architecture, tests, and truthful states.
- Do not maintain two competing product surfaces for the same feature.
- A feature with a menu/button but no validated backend is not implemented.

## Prioritization method

Score each proposal from 1–5:

- **User impact:** How strongly does it improve the main edit workflow?
- **Frequency:** How often will typical users need it?
- **Trust/safety:** Does it prevent lost work, false success, or damaging output?
- **Strategic fit:** Does it strengthen the local-first Windows editor?
- **Implementation confidence:** Can it be built and tested with the current architecture?
- **Ongoing cost:** Reverse score; high maintenance/provider cost lowers priority.

Prefer the smallest coherent release that produces a complete user outcome. Avoid adding advanced AI before foundational editing and review flows are usable.

## Ordered roadmap

### Priority 0 — Product consolidation

Outcome: users know exactly which version to download and developers know where to make changes.

Acceptance criteria:

- Rebuild is identified as the canonical product in repository and release documentation.
- Legacy/experimental folders are clearly labelled.
- Stable and preview releases are distinct.
- One version scheme, one download path, one known-issues page, and one migration policy exist.

### Priority 1 — System Doctor

Outcome: users know what works before starting a long job.

Acceptance criteria:

- One screen checks FFmpeg, FFprobe, Whisper/model, optional OpenCV models, GPU encoders, fonts, disk space, write permission, output path, and voice/provider availability.
- Each dependency reports Ready, Warning, Unavailable, or Failed with an actionable message.
- Optional failures do not block unrelated editing.
- Results can be exported as a diagnostic report without secrets.

### Priority 2 — Real editing timeline

Outcome: users can perform normal editing visually without thinking in FFmpeg commands.

Acceptance criteria:

- Thumbnail strip, waveform, multiple typed tracks, playhead, zoom, scrolling, trim handles, split, move, snap, ripple delete, lock/mute/hide, and keyboard shortcuts.
- Every edit is non-destructive and undoable.
- Long timelines remain responsive through virtualization and caching.
- Preview seeking remains synchronized with timeline time.

### Priority 3 — Transcript-driven editing

Outcome: users can edit spoken video by editing text.

Acceptance criteria:

- Local Whisper creates timestamped segments.
- Clicking text seeks preview.
- Deleting/selecting text creates a reviewable EDL/timeline proposal.
- Silence and filler candidates are conservative and review-gated.
- Thai language and low-confidence segments are handled visibly.

### Priority 4 — Thai caption editor

Outcome: users can produce readable platform-ready Thai captions quickly.

Acceptance criteria:

- Editable text, timing, line breaks, style, position, safe zones, and platform presets.
- Thai font readiness and short sample render validation.
- Export SRT, ASS, and burned video.
- No caption style is applied irreversibly to source/timeline data.

### Priority 5 — Face tracking, auto reframe, and privacy

Outcome: 16:9 footage becomes useful 9:16 content while protecting identities.

Acceptance criteria:

- Local detection/tracking produces reviewable tracks with confidence.
- User selects the primary subject and privacy targets.
- Tracking-loss fallback is visible and safe.
- Crop/blur is previewed before export.
- No automated face blur or crop is reported successful without a validated output.

### Priority 6 — Export Center

Outcome: one reviewed edit can produce all required delivery formats.

Acceptance criteria:

- TikTok/Reels/Shorts, 16:9, square, lightweight sharing, audio-only, and subtitle-only presets.
- Batch export with CPU and verified GPU options.
- Predicted size/quality guidance, real progress, versioned output, FFprobe QA, and an inspectable manifest.

### Later priorities

Only after priorities 0–6 are stable:

- auto highlight scoring with visible reasons
- hook/CTA generation
- project-local B-roll matching
- beat-aware music editing
- multicam and nested sequences
- script-to-video and cloud generation
- mobile control and authenticated social delivery
- plugin marketplace

## Feature design template

Every issue or PR for a product feature should state:

1. User problem and target workflow.
2. Current behavior and evidence.
3. Proposed user-visible behavior.
4. Architecture and data model changes.
5. Failure, cancellation, and unavailable-provider behavior.
6. Privacy and source-media protections.
7. Automated tests and manual validation.
8. Definition of done and explicit non-goals.

## Release gates

A release candidate must pass:

- compile and unit tests
- relevant real-media tests
- Thai/spaced-path tests
- source immutability and output QA
- portable package verification
- dependency/provider truthfulness checks
- manual Windows UI smoke test for changed product areas
- known limitations and upgrade notes

## Decision rule

When two features compete, build the one that completes a frequent user workflow with fewer hidden dependencies and stronger validation. A smaller feature that users can trust is preferred over a broad AI feature that cannot be verified.