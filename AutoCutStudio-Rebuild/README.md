# AutoCut Studio Rebuild

เวอร์ชันนี้เขียนใหม่จากศูนย์และอยู่ในโฟลเดอร์แยก `AutoCutStudio-Rebuild/` โดยไม่แก้หรืออ้างอิง Source Code ของรุ่นเดิม

## หลักการ

- Windows 10/11 x64
- WPF + .NET 8
- Local-first
- ไม่แก้ Source Media
- FFmpeg/FFprobe Backend จริง
- UI กับ Worker ทำงานคนละ Process
- Project, Job, Progress และ Log บันทึกเป็น JSON/ไฟล์จริง
- Output ใช้ชื่อ Version และตรวจด้วย FFprobe ก่อนยืนยันผล
- รองรับ Path ภาษาไทยและ Path ที่มีช่องว่าง

## Workflow ที่เชื่อม Backend

- สร้าง/เปิด/บันทึก Project
- Import วิดีโอหลายไฟล์และ Probe Metadata
- Preview
- Timeline: Trim, Split, Delete, Move
- Export หลายคลิปเป็น MP4
- Detect Silence
- Whisper Transcription เมื่อมี local CLI/Model
- Burn Subtitle
- Shorts 9:16
- Enhance Audio
- Mix Voiceover
- Stabilize
- Logo Overlay
- Privacy Blur แบบกำหนดพื้นที่

ระบบที่ขาด Provider หรือ Model จะแจ้งสถานะจริงและไม่สร้างผลลัพธ์จำลอง

## System Doctor

กดปุ่ม **ตรวจระบบ** จาก Toolbar เพื่อเช็กความพร้อมก่อนเริ่มงาน โดยตรวจ:

- FFmpeg และ FFprobe ด้วยการเปิดโปรแกรมจริง
- Hardware encoder ที่มีใน FFmpeg build
- Whisper CLI และไฟล์โมเดล
- โมเดล OpenCV สำหรับใบหน้าและวัตถุ
- ฟอนต์ภาษาไทย
- พื้นที่ดิสก์และสิทธิ์เขียนในโฟลเดอร์ Output
- Windows Voices
- สถานะการตั้งค่า OpenAI โดยไม่อ่านหรือบันทึก API Key

ผลตรวจแบ่งเป็น `Ready`, `Warning`, `Unavailable` และ `Failed` ฟังก์ชันเสริมที่ยังไม่พร้อมจะไม่ปิดกั้นงานตัดต่อพื้นฐาน ผู้ใช้สามารถ Export รายงาน JSON สำหรับวิเคราะห์ปัญหาได้

โหมดคำสั่งสำหรับ CI/ตรวจ Portable:

```powershell
./AutoCutStudio.Rebuild.exe --doctor
```

รายงานจะถูกเขียนที่ `%TEMP%\autocut-rebuild-doctor.json` และคืน Exit Code ที่ไม่เป็นศูนย์เฉพาะเมื่อรายการที่จำเป็นล้มเหลว

## Build

```powershell
cd AutoCutStudio-Rebuild
dotnet restore AutoCutStudio.Rebuild.csproj
dotnet build AutoCutStudio.Rebuild.csproj -c Release
dotnet test tests/AutoCutStudio.Rebuild.Tests.csproj -c Release
./scripts/build-portable.ps1
```

## สถานะ

`in_progress` จนกว่า Windows CI และ Manual UI Smoke Test จะผ่าน
