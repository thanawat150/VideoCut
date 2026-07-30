# AutoCut Studio — Phase 1 architecture

## 1. Product understanding

AutoCut Studio is a Windows desktop editor for non-editors. The long-term product accepts natural-language instructions, converts them into an explicit workflow, assigns real processing jobs to Pixel Agents, produces a reviewable draft, and exports a real video.

The Pixel Office is a workflow control surface. It is not a replacement for the timeline and it does not invent progress. Every visible agent state is derived from an `AgentEvent` emitted by project, probe, worker, FFmpeg, quality-control, or render operations.

## 2. Phase 1 scope

Phase 1 contains only the first usable local workflow:

- project create/open/save and automatic persistence;
- MP4 import by reference;
- FFprobe metadata inspection;
- Windows preview;
- one-source timeline with In/Out, split, delete, undo, and redo;
- persistent job queue data;
- external worker process;
- FFmpeg timeline export;
- progress parsed from FFmpeg;
- pause/resume/cancel control for an active FFmpeg process on Windows;
- step-level recovery: an interrupted atomic export can be restarted from the beginning;
- FFprobe output validation and SHA-256;
- five event-driven agents: Producer, Media Analyst, Video Editor, Quality Control, Render;
- portable Windows publishing configuration;
- tests for event flow, safe paths, versioned output, probe, real render, Thai paths, and paths containing spaces.

## 3. Deferred capabilities

The following are deliberately marked unsupported in Phase 1:

- speech-to-text, transcript, subtitle, filler-word removal, silence removal;
- highlight selection, Shorts, auto-reframe, animated captions;
- denoise, voice enhancement, advanced color, stabilization;
- face/object tracking, redaction, B-roll, text-to-video, AI voice;
- multicam, nested sequences, keyframes, cloud providers, publishing, collaboration;
- provider plugin loading beyond the Phase 1 interfaces;
- true codec-independent frame-accurate preview on every Windows machine;
- checkpoint resume inside one FFmpeg encode. Recovery restarts the current export step.

No UI control for these deferred items is labelled ready.

## 4. Technology comparison

| Stack | Timeline and desktop controls | Preview | Pixel office | Portable / no-admin | Performance | Future cross-platform | Maintenance |
|---|---|---|---|---|---|---|---|
| .NET 8 + WPF | Strong Windows desktop controls and mature input model | Built-in `MediaElement`, dependent on Windows codecs | Canvas, shapes, sprite sheets, or SkiaSharp later | Strong self-contained publish, `asInvoker` | Native desktop | Low | Strong for Windows-first team |
| WinUI 3 | Modern Windows UI | MediaPlayerElement | Good composition APIs | Packaging and deployment are more complex | Strong | None | Higher deployment complexity |
| Avalonia | Strong cross-platform UI | Requires a separate media backend | Excellent with Skia | Strong | Strong | High | Additional preview integration risk |
| Tauri + React + WebView2 | Fast UI iteration | Browser media limitations and codec differences | Excellent Canvas/WebGL | Portable possible, WebView2 dependency | Good UI, IPC overhead | Medium | Two technology stacks |
| MonoGame | Excellent game rendering | Must build most editor widgets | Excellent | Good | Excellent rendering | High | Excessive custom UI work |
| Godot embedded | Excellent animated office | Embedding and native editor integration are complex | Excellent | Larger runtime | Strong | High | Two runtimes and difficult lifecycle |
| SkiaSharp | Excellent custom drawing | Not a complete app framework | Excellent | Library only | Strong | High | Best added after the core workflow |
| Python worker | Strong ML ecosystem | Not a desktop UI choice | Not applicable | Packaging is heavy | Good for models | High | Deferred until an ML feature exists |

## 5. Recommended Phase 1 stack

- **.NET 8 + WPF** for the Windows-first application.
- **WPF MediaElement** for the first preview implementation.
- **Canvas/Grid-based pixel office** in Phase 1; replaceable sprite assets can be added after the core workflow passes.
- **FFmpeg / FFprobe** as explicit external local dependencies.
- **Separate .NET worker executable** for media processing.
- **System.Text.Json** for transparent, inspectable project and job files.
- **xUnit** for tests.
- **GitHub Actions Windows runner** for compilation, real-media integration testing, and portable artifact creation.

This choice prioritizes a working Windows product over future cross-platform support. Core, infrastructure, and worker projects are UI-independent so a future Avalonia front end can reuse them.

## 6. Architecture

```text
AutoCutStudio.App (WPF)
  ├─ Project/Media view
  ├─ Editor view
  ├─ Pixel Office view
  ├─ Job Center view
  ├─ AgentEventBus subscriber
  └─ Worker launcher + persistent job monitor
             │
             │ job.json / control.json / progress.json / events.jsonl
             ▼
AutoCutStudio.Worker
  ├─ validates job and paths
  ├─ emits agent/job events
  ├─ invokes FFmpeg with ArgumentList
  ├─ parses -progress pipe:1
  ├─ responds to pause/resume/cancel
  ├─ invokes FFprobe QA
  └─ writes reports and manifest
             │
             ▼
FFmpeg / FFprobe
```

Project and job persistence is the cross-process contract. The UI can close without corrupting the project. The worker can continue independently. If an interrupted worker is no longer alive, the job is marked resumable and the same atomic export step can be restarted.

## 7. Pixel Office and backend connection

The WPF Pixel Office contains the five Phase 1 agents only. An agent card has a room, status, progress, message, warning, and output.

The UI never runs a random status timer. It tails each job's append-only `agent-events.jsonl` file and publishes newly read events to the in-process `AgentEventBus`. UI state is a projection of those events.

Examples:

- `ffprobe.started` → Media Analyst / `reading`
- `ffprobe.completed` → Media Analyst / `completed`
- `job.started` → Video Editor / `editing`
- `render.started` → Render / `rendering`
- `ffmpeg.progress` → Render progress
- `qa.started` → Quality Control / `analysing`
- `qa.completed` → Quality Control / `completed`
- `render.completed` → Render / `completed`
- any `*.failed` → corresponding agent / `error`

## 8. AgentEventBus

Every event contains:

- event ID and type;
- UTC timestamp;
- project ID;
- optional job ID and agent ID;
- action and status;
- optional progress;
- user-facing message;
- optional input/output paths;
- severity;
- `requires_user_action`;
- metadata dictionary.

The event log is append-only JSON Lines so it can be tailed after an application restart.

## 9. Main screens

1. **Pixel Office** — five real Phase 1 agents and workflow messages.
2. **Editor** — preview, playhead, In/Out, timeline segment list, split, delete, undo/redo, export.
3. **Job Center** — persisted jobs, actual status/progress, pause/resume/cancel/retry, paths, and log.
4. **Project / Media** — project create/open, MP4 import, FFprobe metadata, tool availability, project folder.

## 10. Runtime project structure

```text
project/
├── source/              # reserved; Phase 1 links original media instead of copying it
├── proxy/
├── audio/
├── images/
├── transcript/
├── subtitles/
├── assets/
├── music/
├── templates/
├── cache/
├── drafts/
├── exports/
├── thumbnails/
├── reports/
├── jobs/
│   └── <job-id>/
│       ├── job.json
│       ├── progress.json
│       ├── control.json
│       ├── run.log
│       ├── error.log
│       ├── agent-events.jsonl
│       ├── manifest.json
│       ├── processing_report.json
│       └── qa_report.json
├── logs/
├── backups/
└── project.json
```

## 11. Data model

- `ProjectDocument`: identity, project root, timestamps, source media, timeline, job history, output history.
- `MediaAsset`: source path, immutable probe metadata, import timestamp.
- `TimelineDocument`: active media and ordered kept segments.
- `TimelineSegment`: source start/end times.
- `JobDocument`: input, versioned output, segments, status, dependency data, expected audio, worker process ID.
- `JobProgress`: status, real progress, ETA, active agent, message, worker PID.
- `AgentEvent`: append-only event contract.
- `QaReport`: stream, duration, size, and error checks.
- `ExportManifest`: output path, SHA-256, encoder information, and creation timestamp.

## 12. Roadmap

1. **Foundation** — solution, models, repositories, safe paths, event bus.
2. **Media workflow** — project UI, import, FFprobe, preview.
3. **Timeline workflow** — range, split, delete, undo/redo.
4. **Worker workflow** — job persistence, FFmpeg export, progress, controls.
5. **QA and recovery** — FFprobe validation, hashes, event replay, orphaned-job recovery.
6. **Packaging and verification** — Windows CI, real test media, self-contained portable ZIP.

Phase 2 begins only after Phase 1 acceptance evidence is available.

## 13. Acceptance criteria

A Phase 1 build may be marked passed only when Windows CI and a local Windows smoke test prove:

- source hash and modified timestamp remain unchanged;
- output opens and FFprobe reports a video stream;
- output contains audio when the source contains audio;
- output duration matches the kept timeline duration within tolerance;
- Thai paths and paths with spaces pass;
- an existing output is not overwritten;
- events drive Pixel Agent state;
- FFmpeg progress is read from `-progress`;
- pause/resume/cancel affects the real process;
- an interrupted job is restartable;
- portable executables start without administrator rights;
- project/job/log/report files are present.

## 14. Risks

- WPF `MediaElement` depends on installed Windows media codecs. Proxy generation or LibVLC is a later mitigation.
- FFmpeg is an external dependency and licensing/distribution must be decided before bundling.
- Suspending process threads is Windows-specific and must be smoke-tested across supported Windows versions.
- A complex timeline export can become expensive because segments are decoded and re-encoded.
- A JSON file queue is intentionally simple; concurrent multi-worker scheduling is deferred.
- Linux in the authoring environment cannot validate the WPF executable. GitHub Actions and a Windows machine are required.
