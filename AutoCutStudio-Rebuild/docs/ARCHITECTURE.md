# AutoCut Studio Rebuild Architecture

```text
AutoCutStudio.Rebuild.exe
├─ UI mode
│  ├─ Project/Timeline
│  ├─ FFprobe import
│  └─ creates durable Job JSON
└─ Worker mode (--worker job.json)
   ├─ FFmpeg/Whisper execution
   ├─ progress.json and error.log
   ├─ .partial output
   └─ FFprobe validation before promotion
```

## Security

- `ProcessStartInfo.ArgumentList` ป้องกันการประกอบ Shell Command จากข้อความผู้ใช้
- Output ต้องอยู่ใต้ Project Root
- Source Media ใช้เป็น Input และไม่ถูกเขียนทับ
- Output เดิมได้รับ `_v2`, `_v3` ตามลำดับ
- Missing Model/Provider ทำให้ Job Failed ตามจริง
