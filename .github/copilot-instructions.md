# AutoCut Studio repository instructions

## Canonical product

Use `AutoCutStudio-Rebuild/` as the default target for all new product work. Treat the root/legacy implementation as reference or migration source unless the task explicitly asks to change it.

## Project skills

Load the matching repository skill before editing:

- `.github/skills/autocut-video-pipeline/SKILL.md` for FFmpeg, FFprobe, Whisper, transcript editing, captions, export, audio/video filters, worker jobs, and real-media validation.
- `.github/skills/autocut-wpf-mvvm/SKILL.md` for WPF UI, timeline controls, MVVM, commands, binding, async work, styling, accessibility, and UI tests.
- `.github/skills/autocut-product-roadmap/SKILL.md` for feature prioritization, release planning, acceptance criteria, and deciding what to build next.

Use more than one skill when a task crosses boundaries. For example, a transcript-driven timeline feature requires both the video-pipeline and WPF/MVVM skills.

## Non-negotiable engineering rules

1. Never overwrite source media.
2. Probe media before planning an operation and validate output with FFprobe before reporting success.
3. Write outputs to versioned paths; use `.partial` files and promote only after validation succeeds.
4. Run long media work outside the UI thread and preserve cancel, pause, resume, retry, progress, and error logs.
5. Do not concatenate user text into shell commands. Use `ProcessStartInfo.ArgumentList` or equivalent typed arguments.
6. Do not claim a provider, AI model, upload, render, or export succeeded unless a verifiable output exists.
7. Preserve Thai paths, spaces, Unicode text, and Thai subtitle readability.
8. Add or update automated tests, including real-media tests for changed FFmpeg behavior.
9. Keep ViewModels testable and business/media logic outside code-behind.
10. New features must state user value, failure behavior, acceptance criteria, and release validation.

## Delivery rule

A feature is not complete only because it compiles. Completion requires relevant unit tests, real-media or UI validation, truthful error states, documentation, and a portable-build check when packaging changes.