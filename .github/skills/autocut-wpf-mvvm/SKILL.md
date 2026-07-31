---
name: autocut-wpf-mvvm
description: Build and modernize AutoCut Studio's WPF interface using testable MVVM boundaries, commands, binding, async worker coordination, reusable styles, timeline virtualization, accessibility, localization, and runtime validation. Use for windows, views, timeline controls, transcript editors, export screens, system doctor, job progress, and desktop UX.
license: MIT
compatibility: AutoCutStudio-Rebuild on .NET 8 WPF for Windows 10/11 x64.
---

# AutoCut WPF and MVVM

## Product rule

The interface must expose real backend state. Buttons, progress, job status, provider availability, and completion messages must reflect actual project/worker data rather than optimistic UI simulation.

## Architecture

- Keep media, project, and business rules in services/domain code.
- Keep ViewModels UI-agnostic and unit-testable.
- Use commands and binding for user actions; avoid business logic in code-behind.
- Restrict code-behind to view-only concerns such as focus, drag geometry, media-element bridging, and visual lifecycle events.
- Use dependency injection for services, ViewModels, navigation, dialogs, and job monitoring.
- Split large screens into focused ViewModels and reusable controls instead of creating a God ViewModel.

Recommended product areas:

```text
Views/
  Shell/
  Project/
  MediaLibrary/
  Timeline/
  Transcript/
  Captions/
  Export/
  SystemDoctor/
  Jobs/
ViewModels/
Services/
Controls/
Resources/
Converters/
```

## Async and threading

- Never run FFmpeg, FFprobe, Whisper, hashing, file copying, thumbnail generation, or model loading on the UI thread.
- Prefer `async` commands and cancellation tokens.
- Marshal only the minimum UI update back to the Dispatcher.
- Throttle high-frequency progress and playhead updates to avoid binding storms.
- Disable or gate commands using real `CanExecute` state while jobs are running.
- Dispose timers, subscriptions, media resources, cancellation sources, and event handlers predictably.

## Binding and state

- Use explicit binding modes and `UpdateSourceTrigger` values.
- Surface validation with `INotifyDataErrorInfo` or a project-equivalent mechanism.
- Represent loading, ready, unavailable, warning, running, paused, failed, cancelled, and completed as explicit states.
- Do not use only colors to communicate status; include text/icon/tooltips.
- Preserve state across project save/reopen where the domain requires it.

## Timeline UX

The timeline should behave as an editor rather than a static list:

- thumbnail strip and optional waveform cache
- separate video, audio, music, caption, and overlay tracks
- draggable playhead with zoom and horizontal scrolling
- trim handles, split, move, snap, ripple delete, track lock/mute/hide
- selection state and keyboard shortcuts
- virtualized rendering for long projects
- non-destructive edits represented by source ranges
- commands that produce one undoable transaction per user action

Do not bind thousands of frame thumbnails directly without virtualization or caching. Keep timeline geometry independent from rendered pixels so zoom does not alter source time.

## Transcript and caption UX

- Selecting a transcript segment seeks the preview.
- Text edits do not silently alter media; they create a reviewed timeline proposal.
- Show confidence/uncertain segments without overwhelming normal users.
- Provide Thai-aware caption preview, line breaks, safe zones, style presets, and timing adjustment.
- Use a short render sample for style verification before exporting a long video.

## System Doctor UX

Create a single readiness screen that checks:

- bundled FFmpeg/FFprobe execution
- Whisper CLI/model availability
- optional OpenCV models
- GPU encoder capability based on real command checks
- disk space and write permissions
- Thai font availability
- output directory validity
- Windows voice/provider state

Every check must display Ready, Warning, Unavailable, or Failed with an actionable explanation. Optional dependencies must not block unrelated editing features.

## Styling and localization

- Keep colors, typography, spacing, control templates, and status styles in ResourceDictionaries.
- Use reusable styles rather than page-specific hardcoded properties.
- Thai is the default language; strings must not be embedded throughout code-behind.
- Test long Thai labels, high DPI, 125–200% scaling, and Windows text rendering.
- Maintain usable keyboard focus, tab order, accessible names, and contrast.

## Performance

- Virtualize media libraries, job histories, transcripts, and timeline items.
- Cache thumbnails/waveforms by source identity and generation settings.
- Avoid synchronous file metadata access inside property getters.
- Freeze reusable WPF Freezables when safe.
- Avoid recreating full collections for small progress changes.
- Keep preview playback independent from heavy timeline re-layout.

## Testing and validation

- Unit-test commands, validation, state transitions, and ViewModel behavior.
- Test cancellation, failure, missing dependency, empty project, and invalid path states.
- Add UI automation or a focused Windows smoke test for critical workflows where practical.
- Validate XAML at runtime; a successful compile is not enough.
- Run a manual smoke test for preview playback, drag/trim behavior, DPI, GPU drivers, Windows voices, and firewall prompts before stable release.

## Definition of done

- ViewModel has no hidden business/media processing logic.
- Long work cannot freeze the UI.
- Commands expose correct enabled/disabled states.
- Errors are actionable and provider availability is truthful.
- Thai text and Unicode paths render correctly.
- Large lists/timelines remain responsive through virtualization/caching.
- Unit tests and relevant Windows UI/runtime checks pass.
- New UI states have localization and accessibility coverage.

## Upstream inspiration

This skill adapts WPF/MVVM guidance from `managedcode/dotnet-skills`, WPF skill (MIT): MVVM separation, explicit binding, async/threading discipline, reusable styles/templates, virtualization, and runtime validation. Examples and architecture are rewritten for AutoCut Studio. See `.github/skills/THIRD_PARTY_NOTICES.md`.