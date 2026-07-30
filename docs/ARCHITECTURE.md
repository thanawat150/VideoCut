# Architecture — Phase 1

## Stack Decision

เลือก **.NET 8 + WPF + FFmpeg/FFprobe** สำหรับ Windows 10/11

- WPF มี Desktop tooling ที่เสถียรและตรงกับ Windows-only product
- `dotnet publish` สร้าง self-contained Portable EXE ได้
- งานหนักแยกเป็น Worker Process จึงไม่บล็อก UI
- FFmpeg รองรับ codec และ hardware encoder ในอนาคตโดยไม่ผูกกับ UI
- Model/Service ยังแยกเป็นคลาส ทำให้แตกเป็นหลาย project ได้เมื่อระบบโต
- WinUI มี deployment/tooling ซับซ้อนกว่า, Avalonia เพิ่ม media dependency, Tauri เพิ่ม WebView/IPC โดยไม่จำเป็นใน Phase 1

## Runtime Modes

`AutoCutStudio.exe` มีสองโหมด:

1. ปกติ: เปิด WPF Editor
2. `--worker --once`: อ่าน Job Queue และประมวลผลหนึ่งงานใน Process แยก

## Project Layout

```text
project/
├── source/
├── audio/
├── images/
├── proxy/
├── transcript/
├── subtitles/
├── assets/
├── cache/
├── drafts/
├── exports/
├── thumbnails/
├── reports/
├── versions/
└── project.json
```

Save ใช้ `.tmp` แล้วสลับไฟล์ พร้อมสำรอง `project.json` เดิมลง `versions`

## Media Pipeline

Import → Copy แบบชื่อไม่ชน → FFprobe → MediaItem → TimelineClip → Atomic Save

Export → สร้าง Job folder → เปิด Worker Process → FFmpeg เขียน `.partial` → ตรวจ Exit Code → ย้ายเป็น Output จริง

## Security

- Local-only ไม่มี Upload
- ใช้ `ProcessStartInfo.ArgumentList` ไม่ประกอบ Shell command
- Input และ Output ห้ามเป็นไฟล์เดียวกัน
- ไม่ฝัง API Key
- Manifest เป็น `asInvoker`
- ไฟล์ต้นฉบับไม่ถูกเขียนทับ

## Known Gaps

- Preview ใช้ Media Foundation จึงรองรับ codec น้อยกว่า FFmpeg
- Job claiming รองรับ Worker ตัวเดียวเป็นหลัก
- Pause/Resume/Cancel ยังไม่มี IPC
- Project schema migration และ Undo/Redo ยังไม่พร้อม
