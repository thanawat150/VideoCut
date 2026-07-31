# Visual Automation implementation status

Status: `in_progress_ci_validation`

The feature branch currently contains the first complete implementation pass for:

- Visual workflow models, validation, persistence and templates
- Make/n8n-style draggable desktop canvas
- Real workflow execution through local Whisper, FFmpeg and the existing Worker queue
- Platform-specific export presets
- Thai-aware two-line caption layout and TikTok safe zones
- Multi-file and folder import
- Real multi-clip cut/crossfade composition
- Project-local B-roll matching, review and rendering
- Token-protected mobile control over the local network
- Automated tests for DAG validation, captions and multi-clip filters

Windows CI is the source of truth for build, test, portable packaging and dependency validation. Any compile or integration issue found by CI must be corrected before merge.
