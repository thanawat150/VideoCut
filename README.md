# AutoCut Studio — Visual Video Automation

AutoCut Studio is a Windows-first, local-first visual video automation system. It combines a Make/n8n-style workflow canvas with real local Whisper, FFmpeg, OpenCV, persistent workers, QA and non-destructive media handling. Node and Agent states change only when real backend work occurs.

## Visual Automation Studio

The primary experience is a draggable node canvas for building reusable video production flows:

```text
Media / Folder Input
→ Transcribe
→ Remove Fillers / Remove Silence
→ Captions
→ Highlights
→ Auto B-roll + Approval
→ Platform Style
→ Shorts / Multi-clip Export
```

Implemented automation capabilities:

- Node library, draggable canvas, connections, settings, validation and execution states
- Persistent workflow templates and resumable run documents
- Local Whisper transcription, transcript editing and SRT generation
- Real FFmpeg silence removal, multi-clip cut/crossfade and versioned exports
- TikTok, Instagram Reels, Facebook Reels/Feed, YouTube Shorts/16:9 and square presets
- Thai-aware caption pagination with platform-specific safe zones
- Automatic matching of transcript topics to local project B-roll, followed by explicit approval
- B-roll overlay, animated captions, voice enhancement, color correction and stabilization in social renders
- Multi-file and whole-folder import
- Token-protected mobile LAN dashboard for MP4 upload, workflow execution, approval and job control

Default templates include talking-head TikTok, multi-platform Shorts, event recap and transcript/SRT workflows.

## Script to Video

The Script-to-Video workspace can create a complete narrated video from a script:

```text
Script
→ Scene planning
→ Local assets or AI-generated image/video
→ Multilingual voice
→ Captions
→ Platform render
```

It supports:

- Deterministic scene splitting and editable prompts per scene
- Project-local image and B-roll matching before cloud generation
- OpenAI image generation with `gpt-image-1`
- OpenAI text-to-video generation with `sora-2`
- Video-to-image fallback when cloud video generation fails
- Windows local voices selected by language or installed voice name
- OpenAI multilingual speech using `gpt-4o-mini-tts`
- Auto detection for Thai, English, Japanese, Chinese, Korean, Arabic and Russian scripts
- Manual language selection for Spanish, French, German, Portuguese, Italian and other supported voices
- In-memory API key entry or the `OPENAI_API_KEY` environment variable
- Versioned generated assets under the current Project

The API key entered in the Script-to-Video window is used only for the current process and is not written to the Project file. The application also supports `OPENAI_BASE_URL` for compatible gateways.

## Implemented product scope

### Phase 1 — Core Working Product

- Project create/open and auto-save
- Read-only MP4 import and FFprobe metadata
- Preview player, In/Out, split, trim, delete and undo/redo
- Persistent Job Queue and separate Worker process
- Real FFmpeg progress, pause, resume and cancel
- FFprobe QA, SHA-256 manifest and versioned outputs
- Pixel Office states connected to the real Agent Event Bus
- Self-contained Windows portable build with `asInvoker` manifest

### Phase 2 — Speech Editing

- Bundled local `whisper.cpp` and multilingual model
- Thai/English/auto transcription
- Editable transcript, search, playback by segment and SRT export
- Silence detection/removal and conservative filler-only segment review
- Text-based editing converted into a non-destructive timeline

### Phase 3 — Social Media Automation

- Transcript-ranked highlight candidates with visible scores and reasons
- Reviewed batch generation of short clips
- TikTok/Reels/Shorts, landscape and square presets
- Center crop or fit/pad reframing
- Animated ASS captions, Hook and CTA overlays

### Phase 4 — Audio and Visual Enhancement

- Noise reduction and voice enhancement presets
- Color correction presets
- Basic FFmpeg stabilization
- PCM-based beat analysis
- Local music mixing with sidechain ducking

### Phase 5 — Advanced Local AI

- Bundled OpenCV Zoo YuNet face detection
- Temporal face tracks and review-gated Privacy Blur
- Bundled OpenCV Zoo YOLOX object detection
- B-roll suggestions from transcript, detected objects and local assets
- TXT, Markdown, DOCX and PDF ingestion
- Editable template-based text/document-to-video
- Windows local voiceover without voice cloning

### Phase 6 — Professional Workflow

- Multicam switch-plan rendering
- Linear zoom/focus/audio keyframes
- Reusable nested sequences
- Safe declarative export-preset plugins; external DLL code is not loaded
- Real delivery to local, network or desktop-synced cloud folders
- Reviewable social publishing outbox packages
- Collaboration ZIP export/import with SHA-256 and zip-slip protection

### Phase 7 — AI Media Generation and Multilingual Voice

- Provider contracts for image, video and speech generation
- OpenAI image, video and speech implementations over authenticated HTTPS
- Windows local speech provider with culture-based voice selection
- Script-to-Video AI visual modes: local only, fill missing images, regenerate all images, generate all videos, or video with image fallback
- Per-scene prompt editing and generation status
- Platform-aware image and video dimensions
- Long OpenAI narration split into chunks and rejoined through bundled FFmpeg
- API failures remain visible and never report success unless output files are created

## Important provider limitation

Cloud and social provider interfaces report their actual configuration state. The built-in product performs real file delivery to local or synced folders, uses project-local B-roll and creates inspectable social outbox packages. Cloud image, video and speech generation require a valid API key, account access, available quota and network connectivity. It does **not** claim a YouTube, TikTok, Instagram or Facebook upload, stock download or cloud generation succeeded without an authenticated provider and a verified output file.

## Technology stack

- .NET 8 and WPF
- Separate .NET Worker process
- FFmpeg and FFprobe
- `whisper.cpp`
- OpenCvSharp with OpenCV Zoo YuNet and YOLOX ONNX models
- OpenAI Images, Videos and Audio REST APIs when configured
- PdfPig and direct DOCX XML parsing
- JSON project/job/workflow persistence
- xUnit real-media integration tests
- GitHub Actions on Windows

## Build and test

```powershell
dotnet restore AutoCutStudio.sln
dotnet build AutoCutStudio.sln -c Release
dotnet test AutoCutStudio.sln -c Release
```

Optional OpenAI provider configuration:

```powershell
setx OPENAI_API_KEY "your_project_api_key"
```

Restart AutoCut Studio after changing a persistent environment variable. A key may instead be pasted into the Script-to-Video window for the current session only.

## Portable package

The packaging script refuses to silently create a full package when FFmpeg, Whisper or required computer-vision models are missing:

```powershell
pwsh ./scripts/build-portable.ps1 `
  -FfmpegDirectory "C:\path\to\ffmpeg\bin" `
  -WhisperDirectory "C:\path\to\whisper" `
  -WhisperModelPath "C:\path\to\ggml-base-q5_1.bin" `
  -FaceModelPath "C:\path\to\face_detection_yunet_2023mar.onnx" `
  -ObjectModelPath "C:\path\to\object_detection_yolox_2022nov.onnx"
```

CI downloads pinned dependencies from their official upstream repositories, creates the package and tests the tools and models from inside the expanded ZIP.

The package contains at least:

```text
AutoCutStudio.exe
AutoCutStudio.Worker.exe
tools/ffmpeg/ffmpeg.exe
tools/ffmpeg/ffprobe.exe
tools/whisper/whisper-cli.exe
models/whisper/ggml-base-q5_1.bin
models/opencv/face_detection_yunet_2023mar.onnx
models/opencv/object_detection_yolox_2022nov.onnx
plugins/official-youtube-1080p/plugin.json
third_party/*/bundle-manifest.json
```

Dependency diagnostic command:

```powershell
./AutoCutStudio.Worker.exe --doctor
```

## Privacy and safety

- Source media is never overwritten.
- Output paths are versioned and partial files are promoted only after processing succeeds.
- Tool arguments use `ProcessStartInfo.ArgumentList`; user text is not concatenated into a shell command.
- Speech, face and object analysis run locally unless a cloud provider is explicitly selected.
- Cloud prompts, scripts and reference images are sent only after the user selects a cloud mode and confirms the request.
- API keys are not persisted in project files or application logs.
- Privacy Blur is generated only from tracks selected by the user.
- B-roll is copied into the relevant job directory only after approval.
- Mobile Control uses a per-session token and exposes no arbitrary filesystem browser.
- Plugins are declarative JSON presets and cannot execute arbitrary code.
- Collaboration import validates paths, file sizes and SHA-256 values.

## Validation boundary

Automated CI validates compile, tests, real media, real Whisper, model loading, rendering, QA, portable dependencies, Thai paths, spaces and ZIP integrity. Cloud provider integration tests do not spend API credits in CI; authenticated image/video/speech calls require a final manual smoke test with the user's account. A final manual UI smoke test on the target Windows 10/11 hardware remains the release sign-off step for hardware-specific playback, GPU drivers, Windows Firewall prompts, LAN routing, installed Windows voices and cloud account access.
