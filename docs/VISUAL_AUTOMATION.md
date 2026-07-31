# AutoCut Studio — Visual Video Automation

This document describes the new Make/n8n-style automation layer implemented on top of the existing local-first video engine.

## Product direction

AutoCut Studio is evolving from a menu-driven editor into a visual workflow system. A workflow is a directed acyclic graph of typed video nodes. Nodes are persisted as JSON, validated before execution, executed in dependency order, and reflected in the existing persistent Job Queue.

## Implemented workflow nodes

- Media input and folder/multi-clip input
- Merge clips with cut or crossfade and audio normalization
- Local Whisper transcription
- Isolated filler removal
- FFmpeg silence removal
- SRT and animated ASS captions
- Transcript-ranked highlights
- Local project B-roll matching
- Explicit visual approval
- Voice/noise enhancement, stabilization and color settings
- Platform style presets
- Shorts generation
- Export and notification nodes

## Platform presets

The central catalog currently includes:

- TikTok
- Instagram Reels
- Facebook Reels
- Facebook Feed 4:5
- YouTube Shorts
- YouTube 16:9
- Square Feed

Each preset owns its resolution, aspect strategy, bitrate, pacing metadata and caption safe zone. The TikTok preset reserves additional space on the right and bottom for platform UI.

## Caption safety

Thai captions are segmented using Unicode text elements so combining marks remain attached to their base character. Long transcript segments are split into multiple timed pages. Each page respects the platform's maximum line count, minimum font size, left/right margins and maximum safe-width ratio.

## Multi-clip composition

Multiple imported MP4 assets can be combined in one Worker job. The FFmpeg processor normalizes dimensions, frame rate and audio layout, generates silence for video-only inputs, and supports cut or real xfade/acrossfade transitions. Source files remain read-only.

## Automatic B-roll

The B-roll node reads the transcript, proposes visual keywords and matches them to files under `assets/broll`. Matched assets are shown at an approval node. Approved assets are copied into the job directory and composited into social outputs at transcript timestamps. External stock and AI image providers remain optional provider integrations and must report their real configuration state.

## Mobile control

The desktop can start a token-protected LAN server without administrator URL registration. A phone on the same network can:

- Upload MP4 files into the open project
- Select and run saved workflows
- View workflow runs and job progress
- Approve or reject proposed B-roll
- Pause, resume or cancel active jobs

The desktop remains the processing machine and must remain open.

## Persistence and recovery

- Workflows: `<project>/workflows/*.workflow.json`
- Workflow runs: `<project>/workflow-runs/*.run.json`
- Job state: `<project>/jobs/<job-id>/`
- Approved B-roll: staged inside the relevant job directory
- Exports: versioned and never overwrite existing outputs

## Remaining provider-gated work

Direct publishing to social platforms and cloud AI image generation require authenticated provider plugins, approved accounts, credentials and external API permission. The application must never claim these actions succeeded when a provider is not configured.
