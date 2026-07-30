# Feature Matrix

✅ ใช้งานจริง · 🟡 บางส่วน · ⛔ ยังไม่รองรับ

| Feature | Status | หมายเหตุ |
|---|:---:|---|
| Create/Open Project | ✅ | JSON + folders จริง |
| Auto Save / Versions | ✅ | Atomic save + 30 versions |
| Import video | ✅ | Copy ไม่แก้ Source |
| FFprobe metadata | ✅ | Codec, size, FPS, duration, bitrate, audio |
| Preview | 🟡 | MP4/H.264 เหมาะที่สุด |
| Timeline | 🟡 | Video track เดียว |
| Split / Trim / Delete | ✅ | Non-destructive |
| Export MP4 | ✅ | H.264/AAC ผ่าน FFmpeg |
| Persistent Job Queue | ✅ | Job/progress/log files |
| Separate Worker Process | ✅ | EXE เดิมใน worker mode |
| No-admin manifest | ✅ | asInvoker |
| Unit tests | ✅ | Project/import/version |
| Smoke test | ✅ | Generated media → Worker → probe output |
| Portable EXE | 🟡 | CI สร้างได้; FFmpeg ต้องวางเอง |
| Audio waveform | ⛔ | Phase 1 ต่อเนื่อง |
| Multiple tracks | ⛔ | Planned |
| Undo/Redo | ⛔ | Planned |
| Proxy | ⛔ | Planned |
| Silence removal | ⛔ | Phase 2 |
| Speech-to-Text | ⛔ | Phase 2 + model |
| Subtitle | ⛔ | Phase 2 |
| Highlight / Shorts | ⛔ | Phase 3 |
| Hardware encoder detection | ⛔ | Phase 4 |
| AI Assistant workflow | ⛔ | Phase 5 |
