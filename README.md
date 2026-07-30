# AutoCut Studio

โปรแกรมตัดต่อวิดีโอแบบ Local-first สำหรับ Windows 10/11 สร้างจากข้อกำหนดใน [`MASTER_PROMPT.md`](MASTER_PROMPT.md)

> สถานะ: **Phase 1 — Early Alpha**  
> ฟังก์ชันที่ยังไม่เสร็จจะไม่ถูกแสดงว่าใช้งานได้

## ทำงานได้แล้ว

- สร้างและเปิดโปรเจกต์ `project.json`
- สร้างโครงสร้างโฟลเดอร์โปรเจกต์อัตโนมัติ
- Auto Save แบบ atomic พร้อมเก็บ Version History 30 รุ่น
- นำเข้าวิดีโอโดยคัดลอกไฟล์ ไม่แก้ไขต้นฉบับ
- ตรวจ Codec, Resolution, FPS, Duration, Bitrate และ Audio ด้วย FFprobe จริง
- Preview MP4 ผ่าน WPF MediaElement
- Timeline วิดีโอ 1 Track
- Split, Trim In/Out และ Delete แบบ non-destructive
- Export MP4 H.264/AAC ด้วย FFmpeg จริง
- Job Queue ที่ `%LocalAppData%\AutoCutStudio\Jobs`
- Worker แยกโดยเปิด EXE เดิมด้วย `--worker --once`
- `job.json`, `progress.json`, `run.log`, `error.log`
- Unit tests และ end-to-end smoke test
- Portable single-file EXE ผ่าน Windows CI

## ยังไม่รองรับ

Audio waveform, multiple tracks, Undo/Redo, Proxy, Silence removal, Speech-to-Text, Subtitle, Hardware encoder detection และ AI Assistant ยังอยู่ใน Phase ต่อไป

ดูรายละเอียดที่ [`docs/FEATURE_MATRIX.md`](docs/FEATURE_MATRIX.md)

## Stack

- .NET 8 + WPF
- FFmpeg / FFprobe
- JSON Project + LocalAppData Job Queue
- xUnit + GitHub Actions `windows-latest`

เหตุผลการเลือกอยู่ที่ [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md)

## FFmpeg

วาง `ffmpeg.exe` และ `ffprobe.exe` ไว้ในโฟลเดอร์ `tools` ข้างโปรแกรม หรือเพิ่ม FFmpeg ลงใน PATH โปรแกรมจะไม่ดาวน์โหลดเครื่องมือเอง

## Build

```powershell
dotnet restore AutoCutStudio.sln
dotnet build AutoCutStudio.sln -c Release
dotnet test AutoCutStudio.sln -c Release --no-build
```

## Smoke Test

```powershell
./scripts/smoke-test.ps1
```

สคริปต์สร้าง Test Media เอง ตัดด้วย Worker จริง และตรวจว่า Output มี Video, Audio และความยาวประมาณ 2 วินาที

## Portable EXE

```powershell
dotnet publish src/AutoCutStudio/AutoCutStudio.csproj `
  -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
  -o artifacts/portable
```
