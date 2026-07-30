# MASTER PROMPT: AutoCut Studio
# โปรแกรมตัดต่อวิดีโออัตโนมัติด้วย AI สำหรับ Windows

## 1. บทบาทของคุณ

ให้คุณทำหน้าที่เป็นทีมพัฒนาซอฟต์แวร์ระดับมืออาชีพ ประกอบด้วย:

- Software Architect
- Senior Desktop Application Developer
- Video Processing Engineer
- FFmpeg Expert
- AI/ML Engineer
- UX/UI Designer
- Audio Engineer
- Quality Assurance Engineer
- Application Security Engineer
- Product Manager

เป้าหมายคือออกแบบและสร้างโปรแกรมตัดต่อวิดีโออัตโนมัติชื่อชั่วคราวว่า:

AutoCut Studio

ห้ามสร้างเพียง Mockup, Prototype ที่กดไม่ได้, หน้าจอจำลอง หรือระบบที่อ้างว่าทำงานสำเร็จทั้งที่ยังไม่ได้ประมวลผลจริง

ทุกฟังก์ชันที่แสดงว่า “พร้อมใช้งาน” ต้องสามารถประมวลผลไฟล์จริงและสร้างผลลัพธ์จริงได้

ฟังก์ชันที่ยังไม่เสร็จต้องแสดงสถานะอย่างตรงไปตรงมาว่า:

- อยู่ระหว่างพัฒนา
- ต้องติดตั้งเครื่องมือเพิ่ม
- ต้องเชื่อม API
- ต้องใช้โมเดล AI
- ไม่รองรับในเวอร์ชันนี้

---

## 2. เป้าหมายผลิตภัณฑ์

สร้างโปรแกรมตัดต่อวิดีโออัตโนมัติที่คนไม่มีพื้นฐานตัดต่อวิดีโอก็ใช้งานได้

ผู้ใช้ควรทำงานได้ด้วย Workflow:

นำเข้าวิดีโอ
→ บอกว่าต้องการคลิปแบบใด
→ AI วิเคราะห์เนื้อหา
→ เสนอแผนการตัดต่อ
→ สร้าง Draft
→ ผู้ใช้ดู Preview
→ แก้ไขบางส่วน
→ Export วิดีโอ

โปรแกรมต้องมีทั้ง:

1. โหมดง่าย
2. โหมดมืออาชีพ
3. โหมด AI อัตโนมัติ
4. โหมดแก้ไขด้วยตนเอง
5. โหมด Batch Processing

---

## 3. กลุ่มผู้ใช้งาน

ออกแบบให้รองรับ:

- Content Creator
- YouTuber
- TikTok Creator
- Facebook Creator
- Instagram Creator
- ผู้สอนออนไลน์
- พนักงานองค์กร
- นักวิจัย
- คนทำคลิปภาคสนาม
- ผู้ทำคลิปโดรน
- ผู้ทำ Podcast
- ผู้บันทึกการประชุม
- ผู้ขายสินค้าออนไลน์
- ผู้ไม่มีประสบการณ์ตัดต่อวิดีโอ

---

## 4. แพลตฟอร์มและรูปแบบการติดตั้ง

พัฒนาเป็นโปรแกรม Desktop สำหรับ Windows 10 และ Windows 11

ข้อกำหนด:

- เปิดใช้งานด้วยไฟล์ .exe
- มีรุ่น Portable
- ไม่จำเป็นต้องใช้สิทธิ์ Administrator
- ไม่เขียนไฟล์ใน Program Files โดยไม่จำเป็น
- เก็บ Settings, Cache และ Logs ใน LocalAppData
- รองรับไฟล์ขนาดใหญ่
- หน้าจอไม่ค้างระหว่างประมวลผล
- มี Worker Process ทำงานเบื้องหลัง
- ยกเลิกงานได้
- ปิดโปรแกรมแล้วกลับมาทำต่อได้
- รองรับ Auto Save
- รองรับ Crash Recovery

ให้เลือก Technology Stack ที่เหมาะสมที่สุด โดยพิจารณา:

- .NET 8 Desktop
- WPF
- WinUI
- Avalonia
- Tauri
- React
- FFmpeg
- FFprobe
- Python Worker
- ONNX Runtime
- Whisper
- OpenCV

ก่อนเริ่มเขียนระบบ ให้สรุปเหตุผลในการเลือก Stack โดยเน้น:

- ความง่ายในการ Build
- ความเสถียร
- ประสิทธิภาพ
- การสร้าง Portable EXE
- การรองรับ Timeline
- การรองรับ Hardware Acceleration
- การขยายโมดูลในอนาคต

---

## 5. หน้าหลักของโปรแกรม

หน้าแรกต้องดูทันสมัย เข้าใจง่าย และไม่เต็มไปด้วยเมนูทางเทคนิค

เมนูหลัก:

- สร้างโปรเจกต์ใหม่
- เปิดโปรเจกต์เดิม
- ตัดต่ออัตโนมัติ
- สร้างคลิปสั้น
- ตัดคลิปพูด
- ตัด Podcast
- ตัดคลิปประชุม
- ตัดคลิปโดรน
- สร้างวิดีโอจากรูปภาพ
- สร้างวิดีโอจากข้อความ
- งานที่กำลังประมวลผล
- Template
- Brand Kit
- คลังสื่อ
- ตั้งค่า

หน้าแรกต้องมีช่อง Assistant:

“วันนี้ต้องการสร้างวิดีโอแบบไหน?”

ตัวอย่างคำสั่ง:

- ตัดคลิปนี้ให้เหลือ 1 นาที
- ทำคลิป TikTok แนวให้ความรู้
- ลบช่วงที่พูดผิดและช่วงเงียบ
- ใส่ Subtitle ภาษาไทย
- ทำคลิปสรุปการประชุม
- หาช่วงสำคัญที่สุด 5 ช่วง
- ทำวิดีโอแนวสารคดี
- ตัดคลิปโดรนให้ดู Cinematic
- ทำคลิปสินค้าแบบโฆษณา
- ทำ Shorts จากคลิปยาว
- ทำคลิปแนวตั้งและติดตามหน้าคนพูด
- ใส่เพลงและตัดตามจังหวะ
- ปิดบังใบหน้าและข้อมูลส่วนบุคคล

Assistant ต้องทำได้ 3 โหมด:

### โหมดแนะนำ

บอกผู้ใช้ว่าต้องไปเมนูใดและทำอย่างไร

### โหมดเตรียมงาน

เลือกเครื่องมือและกำหนดค่าให้ แต่ยังไม่เริ่มประมวลผล

### โหมดอัตโนมัติ

สร้าง Workflow และเริ่มทำงานหลังผู้ใช้ตรวจสอบและยืนยัน

---

## 6. Project Manager

แต่ละโปรเจกต์ต้องมีโครงสร้างที่ชัดเจน:

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
└── project.json

ภายใน project.json ให้เก็บ:

- Project ID
- ชื่อโปรเจกต์
- วันที่สร้าง
- วันที่แก้ไขล่าสุด
- Source files
- Timeline
- Tracks
- Effects
- Transcript
- Subtitle
- Export settings
- Brand Kit
- Workflow
- Processing history
- Application version

ต้องรองรับ:

- Auto Save
- Save As
- Duplicate Project
- Archive Project
- Backup Project
- Version History
- Undo
- Redo
- Restore Previous Version
- Relink Missing Media

---

## 7. การนำเข้าสื่อ

รองรับการลากไฟล์มาวาง และเลือกจากโฟลเดอร์

ไฟล์ที่ควรรองรับ:

### Video

- MP4
- MOV
- MKV
- AVI
- WebM
- MTS
- M2TS
- MXF
- MPEG
- 3GP

### Audio

- WAV
- MP3
- AAC
- M4A
- FLAC
- OGG

### Images

- JPG
- PNG
- WebP
- TIFF
- BMP
- GIF

ก่อนนำเข้าให้ใช้ FFprobe ตรวจ:

- Codec
- Resolution
- Frame rate
- Duration
- Bitrate
- Audio channels
- Sample rate
- Color space
- Rotation metadata
- Variable frame rate
- Corrupt frame
- Missing audio
- File size

รองรับการสร้าง Proxy สำหรับวิดีโอความละเอียดสูง เช่น:

- 4K
- 5.3K
- 6K
- 8K
- Drone footage
- High bitrate footage

---

## 8. Timeline Editor

สร้าง Timeline ที่ใช้งานได้จริง

รองรับ:

- Video Track
- Audio Track
- Image Track
- Subtitle Track
- Text Track
- Overlay Track
- Adjustment Track

เครื่องมือ Timeline:

- Select
- Cut
- Razor
- Trim
- Ripple Trim
- Split
- Delete
- Ripple Delete
- Move
- Slip
- Slide
- Duplicate
- Group
- Ungroup
- Lock Track
- Hide Track
- Mute Track
- Solo Track
- Snap
- Zoom Timeline

รองรับ:

- Multiple clips
- Multiple tracks
- Keyframes
- Markers
- Chapters
- In/Out points
- Nested sequence
- Compound clips
- Adjustment layer
- Speed ramp
- Freeze frame
- Reverse video
- Loop

---

## 9. ระบบวิเคราะห์วิดีโออัตโนมัติ

เมื่อผู้ใช้นำเข้าวิดีโอ ระบบต้องวิเคราะห์:

- Shot boundary
- Scene changes
- Black frames
- Frozen frames
- Blurry frames
- Camera shake
- Silence
- Loud noise
- Clipping audio
- Speaker changes
- Faces
- Main speaker
- Objects
- Text in video
- Screenshots
- Slides
- Logo
- Duplicate scenes
- Repeated speech
- Emotional moments
- Laughter
- Applause
- Important keywords
- Question and answer
- Call to action
- Product mentions
- Location changes
- Camera movement
- Music beat
- Video quality

สร้าง Analysis Report พร้อม Timeline Markers

---

## 10. ระบบตัดคลิปพูดอัตโนมัติ

ฟังก์ชัน:

- ลบช่วงเงียบ
- ลบเสียงเอ่อ อ่า อืม
- ลบคำซ้ำ
- ลบช่วงพูดผิด
- ลบประโยคที่พูดใหม่ซ้ำ
- ลดช่องว่างระหว่างประโยค
- ตรวจ Jump Cut
- ใส่ Zoom เพื่อซ่อนรอยตัด
- สลับมุมกล้องอัตโนมัติ
- เลือกมุมกล้องที่ผู้พูดกำลังพูด
- แทรก B-roll
- แทรกข้อความสำคัญ
- สร้าง Chapter
- สร้างสรุป

ทุกการลบต้องสามารถตรวจ Preview และ Undo ได้

---

## 11. Speech-to-Text และ Subtitle

ใช้ระบบถอดเสียงที่รองรับ:

- ภาษาไทย
- ภาษาอังกฤษ
- หลายภาษา
- การสลับภาษาในคลิปเดียวกัน

รองรับ:

- Timestamp ระดับคำ
- Speaker diarization
- Speaker labels
- Custom vocabulary
- ชื่อบุคคล
- คำศัพท์เฉพาะ
- คำศัพท์องค์กร
- Export SRT
- Export VTT
- Export ASS
- Burn-in Subtitle

Subtitle Editor ต้อง:

- แก้ข้อความได้
- แก้เวลาได้
- แบ่งประโยคได้
- รวมประโยคได้
- ตรวจคำยาวเกิน
- ตรวจ Subtitle ซ้อนกัน
- ตรวจอ่านไม่ทัน
- ปรับจำนวนตัวอักษรต่อบรรทัด
- Highlight คำตามเสียงพูด
- Karaoke style
- Animated captions
- Word-by-word captions

รองรับการแปล Subtitle หลายภาษา

---

## 12. Automatic Highlight Detection

สร้าง Highlight จาก:

- เนื้อหาสำคัญ
- ประโยคเปิดที่ดึงดูด
- ประโยคสรุป
- คำพูดที่มีอารมณ์
- เสียงหัวเราะ
- เสียงปรบมือ
- ช่วงที่มีการเคลื่อนไหว
- ช่วงที่ภาพคมชัด
- ช่วงที่คนมองกล้อง
- คำสำคัญ
- ช่วงถามตอบ
- ช่วงสาธิต
- ช่วงก่อนและหลัง
- ช่วงที่มีตัวเลขหรือข้อมูลสำคัญ

ผู้ใช้กำหนดได้ว่า:

- ต้องการกี่คลิป
- ความยาวต่อคลิป
- แนวนอนหรือแนวตั้ง
- โทนจริงจังหรือสนุก
- แพลตฟอร์มเป้าหมาย
- ต้องมี Hook หรือไม่
- ต้องมี CTA หรือไม่

---

## 13. สร้างคลิปแนวตั้งอัตโนมัติ

รองรับอัตราส่วน:

- 16:9
- 9:16
- 1:1
- 4:5
- 21:9
- Custom

ระบบ Auto Reframe ต้อง:

- ตรวจหน้าคน
- ตรวจผู้พูดหลัก
- ติดตามใบหน้า
- ติดตามวัตถุ
- จัดตำแหน่งตาม Rule of Thirds
- สลับตำแหน่งเมื่อตัวแบบเคลื่อน
- รองรับหลายคน
- ทำ Split Screen
- แสดงหน้าคนและ Screen Recording พร้อมกัน
- ป้องกัน Subtitle บังใบหน้า

---

## 14. Audio Processing

ระบบเสียงต้องรองรับ:

- Noise reduction
- Hum removal
- Wind noise reduction
- Echo reduction
- De-reverb
- Voice enhancement
- Equalizer
- Compressor
- Limiter
- Loudness normalization
- Auto gain
- Remove clicks
- Remove pops
- Remove background noise
- Ducking music under speech
- Fade in/out
- Crossfade
- Stereo to mono
- Channel mapping
- Sync external audio
- Auto lip sync
- Detect desynchronized audio

Preset:

- Podcast
- Interview
- Meeting
- Outdoor
- Drone
- Classroom
- Mobile phone
- Studio voice
- Social media loudness

---

## 15. Music System

รองรับ:

- นำเข้าเพลงของผู้ใช้
- เพลงที่มี License
- คลังเพลงภายใน
- Sound effects
- Jingle
- Intro
- Outro

ความสามารถ:

- ตรวจ BPM
- ตรวจ Beat
- ตัดภาพตาม Beat
- ปรับความยาวเพลง
- Loop เพลงให้เนียน
- Ducking
- Music fade
- เลือกเพลงตามอารมณ์
- ตรวจเสียงร้องในเพลง
- เตือนลิขสิทธิ์
- บันทึกที่มาและ License

ห้ามดาวน์โหลดหรือแจกเพลงลิขสิทธิ์โดยไม่ได้รับอนุญาต

---

## 16. B-roll อัตโนมัติ

ระบบควรเสนอ B-roll จาก:

- ไฟล์ของผู้ใช้
- คลังสื่อของโปรเจกต์
- ภาพนิ่ง
- วิดีโอภาคสนาม
- Stock provider ที่ผู้ใช้เชื่อมบัญชีเอง
- ภาพที่สร้างด้วย AI
- Motion graphic
- Screen recording

ใช้ Transcript และ Keyword เพื่อเลือกจังหวะแทรก

ก่อนดาวน์โหลด Stock ต้องแสดง:

- Provider
- License
- Attribution
- ค่าใช้จ่าย
- Resolution
- Watermark status

---

## 17. Color Correction และภาพ

รองรับ:

- Auto exposure
- White balance
- Contrast
- Highlights
- Shadows
- Saturation
- Vibrance
- Temperature
- Tint
- Gamma
- Curves
- LUT
- Sharpen
- Denoise
- Dehaze
- Vignette
- Film grain
- Color match
- Shot matching
- Skin tone protection
- HDR to SDR
- Log footage conversion

Preset:

- Natural
- Cinematic
- Documentary
- Corporate
- Product
- Travel
- Drone
- Forest
- Mangrove
- Night
- Warm
- Cool

---

## 18. Video Stabilization

รองรับ:

- Camera shake detection
- Stabilization strength
- Crop control
- Rolling shutter correction
- Horizon leveling
- Drone horizon correction
- Motion smoothing
- Warning when stabilization causes excessive crop

---

## 19. Drone Video Module

สร้างโมดูลสำหรับวิดีโอจากโดรนโดยเฉพาะ

ฟังก์ชัน:

- ตรวจ Metadata
- ตรวจ Resolution และ FPS
- ตรวจการสั่น
- ปรับ Horizon
- Stabilize
- Color correction
- D-Log conversion
- Speed ramp
- Select best flight segments
- Remove takeoff and landing
- Remove hovering segments
- Detect repeated routes
- Sync music to movement
- Create cinematic sequence
- Add location title
- Add map animation
- Add coordinates
- Add route overlay
- Export field report video

---

## 20. Motion Graphics และข้อความ

รองรับ:

- Titles
- Lower thirds
- Name tags
- Callouts
- Captions
- Progress bars
- Number counters
- Charts
- Logo animation
- Intro/outro
- Location labels
- Map overlays
- Arrow
- Circle highlight
- Blur box
- Pixelate
- Shape
- Gradient

ให้ผู้ใช้ปรับ:

- Font
- Font size
- Color
- Background
- Border
- Shadow
- Animation
- Position
- Duration
- Safe area

---

## 21. Privacy และ Redaction

รองรับ:

- Blur face
- Pixelate face
- Blur license plate
- Blur document
- Blur phone number
- Blur email
- Blur ID card
- Blur QR code
- Mute sensitive words
- Bleep sensitive words
- Remove personal information
- Track blur across frames

ก่อน Export ให้มี Privacy Check

---

## 22. Multi-camera Editing

รองรับ:

- Import multiple cameras
- Sync by audio waveform
- Sync by timecode
- Sync manually
- Detect active speaker
- Auto switch camera
- Create multicam sequence
- Manual camera override
- Picture-in-picture
- Split screen

---

## 23. Screen Recording และ Tutorial

รองรับ:

- Screen recording
- Webcam recording
- Microphone recording
- Cursor highlight
- Mouse click effects
- Auto zoom to clicked area
- Keyboard shortcut display
- Remove inactive screen periods
- Detect app window changes
- Create tutorial chapters
- Add step numbers
- Add voiceover

---

## 24. Podcast และการประชุม

Podcast Workflow:

- แยกผู้พูด
- ปรับเสียงแต่ละคน
- ตัดช่วงเงียบ
- ลบเสียงรบกวน
- สร้าง Chapter
- สร้าง Audiogram
- สร้าง Shorts
- สร้าง Quote Card
- Export audio-only

Meeting Workflow:

- ถอดเสียง
- แยกผู้พูด
- สรุปการประชุม
- ดึง Action Items
- สร้าง Chapter
- สร้าง Highlight
- ปิดบังข้อมูลลับ
- Export Transcript
- Export Minutes of Meeting

---

## 25. Video from Text, Images and Documents

รองรับ:

### Text to Video

ผู้ใช้ใส่ Script แล้วระบบสร้าง:

- Scene plan
- Voiceover
- Subtitle
- B-roll suggestions
- Images
- Motion graphics
- Background music
- Final timeline

### Images to Video

- Pan and zoom
- Ken Burns
- Face-aware crop
- Transition
- Duration by music
- Add caption
- Add voiceover

### Document to Video

รองรับการนำเข้า:

- TXT
- DOCX
- PDF
- PPTX

ระบบต้อง:

- สรุปเนื้อหา
- แบ่ง Scene
- สร้าง Narration
- สร้างภาพประกอบ
- สร้าง Subtitle
- สร้างวิดีโอ

ห้ามอ้างว่ารองรับไฟล์ใด หากยังไม่มี Parser จริง

---

## 26. AI Voiceover

รองรับ:

- Text-to-Speech
- ภาษาไทย
- ภาษาอังกฤษ
- หลายภาษา
- เลือกเพศและโทนเสียง
- ปรับความเร็ว
- ปรับจังหวะเว้นวรรค
- Pronunciation dictionary

การโคลนเสียงต้อง:

- ขอความยินยอม
- แสดงคำเตือน
- บันทึกหลักฐานการยืนยัน
- ไม่อนุญาตให้แอบอ้างบุคคลอื่น

---

## 27. Template System

Template ต้องบันทึกได้:

- Timeline structure
- Aspect ratio
- Fonts
- Colors
- Logo
- Intro
- Outro
- Subtitle style
- Music
- Transition
- Export preset

Template ตัวอย่าง:

- YouTube Education
- TikTok Knowledge
- Corporate Presentation
- Product Advertisement
- Documentary
- Drone Cinematic
- Podcast
- Meeting Summary
- Field Report
- Before and After
- News Summary
- Personal Vlog

---

## 28. Brand Kit

ผู้ใช้กำหนด:

- ชื่อแบรนด์
- Logo
- Primary color
- Secondary color
- Font
- Subtitle style
- Intro
- Outro
- Watermark
- CTA
- Social handles

รองรับหลาย Brand Kit

---

## 29. Export

รองรับ:

- MP4 H.264
- MP4 H.265
- WebM
- MOV
- Audio WAV
- Audio MP3
- Image sequence
- GIF

Preset:

- YouTube 1080p
- YouTube 4K
- TikTok
- Instagram Reels
- Instagram Feed
- Facebook
- LINE
- Presentation
- Archive master
- Small file size
- High quality

ผู้ใช้ปรับ:

- Resolution
- Frame rate
- Codec
- Bitrate
- Quality
- Audio bitrate
- Hardware encoding
- Subtitle burn-in
- Metadata
- File naming
- Output folder

ก่อน Export ตรวจ:

- Missing media
- Black frame
- Audio clipping
- Subtitle overflow
- Invalid frame size
- Unsupported codec
- Low disk space
- Copyright warning
- Privacy warning

---

## 30. Hardware Acceleration

ตรวจอัตโนมัติ:

- NVIDIA NVENC
- AMD AMF
- Intel Quick Sync
- CPU encoding

ระบบต้อง:

- เลือก Hardware Encoder ที่เหมาะสม
- มี Software fallback
- ทดสอบ Encoder ก่อนใช้งาน
- แจ้งเมื่อ Driver มีปัญหา
- ไม่ทำให้โปรแกรมปิดเมื่อ Hardware Encoding ล้มเหลว

---

## 31. Worker และ Job Queue

แยกการประมวลผลออกเป็น Worker

Job Status:

- Draft
- Waiting
- Preparing
- Analysing
- Processing
- Exporting
- Completed
- Completed with warnings
- Failed
- Cancelled
- Paused
- Resumable

ต้องรองรับ:

- Pause
- Resume
- Cancel
- Retry
- Duplicate Job
- Priority
- Queue
- Progress
- ETA
- Log
- Error report

ทุก Job ต้องมี:

- job.json
- progress.json
- run.log
- error.log
- output manifest
- processing report

---

## 32. Plugin และ Provider Architecture

แยกโมดูล:

- Video Decoder
- Video Encoder
- Speech-to-Text
- Subtitle
- Translation
- AI Voice
- Stock Media
- Image Generation
- Music Provider
- Cloud Storage
- Social Publishing

สร้าง Interface กลาง เพื่อเปลี่ยน Provider ได้

ตัวอย่าง:

IVideoProcessor
ISpeechRecognitionProvider
ITranslationProvider
ITextToSpeechProvider
IStockMediaProvider
IImageGenerationProvider
IExportProvider

ระบบหลักต้องเปิดได้แม้ไม่มี Provider บางตัว

---

## 33. ระบบหลายภาษา

รองรับอย่างน้อย:

- ไทย
- English

ออกแบบให้เพิ่มภาษาใหม่ผ่านไฟล์ JSON โดยไม่ต้อง Compile ใหม่

ตัวอย่าง:

languages/
├── th-TH.json
├── en-US.json
├── ja-JP.json
├── zh-CN.json
└── lo-LA.json

ห้ามฝังข้อความทั้งหมดไว้ใน Source Code

ต้องรองรับ:

- ชื่อเมนู
- คำอธิบาย
- Error
- Warning
- Tooltip
- Assistant
- Template
- Workflow

---

## 34. ชื่อทั้งหมดแก้ไขได้

สิ่งที่ผู้ใช้เห็นสามารถเปลี่ยนชื่อได้:

- ชื่อโปรแกรม
- ชื่อโมดูล
- ชื่อเมนู
- ชื่อ Workflow
- ชื่อ Template
- ชื่อสถานะ
- ชื่อ Track
- ชื่อ Preset

แต่ต้องแยก:

internal_id
display_name
aliases

ตัวอย่าง:

{
  "internal_id": "remove_silence",
  "display_name": "ลบช่วงเงียบ",
  "aliases": [
    "ตัดช่วงเงียบ",
    "ลบความเงียบ",
    "ตัดช่องว่าง"
  ]
}

ผู้ใช้เปลี่ยน display_name ได้ แต่ internal_id ห้ามเปลี่ยน

---

## 35. UI/UX

ออกแบบสไตล์:

- Modern
- Minimal
- Professional
- Dark mode
- Light mode
- Responsive
- High contrast
- รองรับหน้าจอ 1366×768 ขึ้นไป

โครงสร้างหน้าต่างหลัก:

ซ้าย:
- Media
- Templates
- Text
- Audio
- Effects
- Transitions
- AI Tools

กลาง:
- Video Preview

ล่าง:
- Timeline

ขวา:
- Properties
- Inspector
- Effects
- Assistant

ต้องมี:

- Tooltip
- Context help
- Guided mode
- Search command
- Keyboard shortcuts
- Drag and drop
- Loading skeleton
- Clear error states
- Empty states
- Progress feedback

---

## 36. โหมดสำหรับผู้เริ่มต้น

ใน Simple Mode:

- ซ่อน Codec
- ซ่อน Bitrate
- ซ่อน Log
- ซ่อนค่าทางเทคนิค
- แสดงขั้นตอนทีละข้อ
- แนะนำค่าที่เหมาะสม
- ใช้คำภาษาคน
- มี Preview ก่อนทำ
- มี Undo เสมอ

ตัวอย่าง:

แทนที่จะเขียนว่า:

“Apply loudness normalization to -14 LUFS”

ให้แสดงว่า:

“ปรับระดับเสียงให้เหมาะกับ YouTube”

---

## 37. ความปลอดภัยและความเป็นส่วนตัว

ข้อกำหนด:

- Local-first
- ไม่อัปโหลดวิดีโอโดยไม่ขออนุญาต
- แสดงข้อมูลที่จะถูกส่งออกจากเครื่อง
- เข้ารหัส API Key
- ไม่บันทึก API Key ใน Source Code
- ไม่แสดง Secret ใน Log
- ตรวจ Path Traversal
- ตรวจ Command Injection
- Escape arguments ของ FFmpeg
- จำกัดขนาด Cache
- ลบ Temporary files อย่างปลอดภัย
- รองรับ Secure Delete สำหรับไฟล์ชั่วคราวที่สำคัญ
- มี Privacy Mode
- มี Offline Mode

---

## 38. Logging และรายงานข้อผิดพลาด

Log ต้องแบ่ง:

- User-friendly message
- Technical details
- FFmpeg command
- FFmpeg output
- Stack trace
- Job ID
- Application version
- OS version
- GPU information

ผู้ใช้สามารถ:

- คัดลอก Error
- Export Diagnostic Package
- เปิด Log folder
- Retry
- Report issue

ห้ามแสดงเพียง “เกิดข้อผิดพลาด”

ต้องอธิบาย:

- เกิดอะไรขึ้น
- ขั้นตอนไหน
- สาเหตุที่เป็นไปได้
- วิธีแก้
- ไฟล์ต้นฉบับได้รับผลกระทบหรือไม่

---

## 39. การทดสอบ

สร้าง:

- Unit tests
- Integration tests
- FFmpeg command tests
- Import tests
- Export tests
- Subtitle tests
- Project recovery tests
- No-admin tests
- Large file tests
- Unicode/ภาษาไทย tests
- File path with spaces tests
- Very long path tests
- GPU fallback tests
- Crash recovery tests

สร้างชุด Test Media ขนาดเล็กที่สร้างขึ้นเองหรือไม่มีปัญหาลิขสิทธิ์

CI ต้องตรวจ:

- Compile
- Tests
- Portable build
- No-admin manifest
- Required files
- FFmpeg detection
- Basic render
- Output file readable
- Output duration correct
- Audio exists
- Video exists

---

## 40. Acceptance Criteria

โปรแกรมเวอร์ชันแรกถือว่าใช้งานจริงได้เมื่อ:

1. เปิดโปรแกรมได้โดยไม่ใช้ Admin
2. สร้างโปรเจกต์ใหม่ได้
3. นำเข้า MP4 จริงได้
4. ตรวจข้อมูลด้วย FFprobe ได้
5. แสดง Preview ได้
6. แสดง Timeline ได้
7. Split และ Trim ได้
8. ลบช่วงเงียบได้
9. ถอดเสียงได้
10. สร้าง Subtitle ได้
11. Export MP4 ได้
12. เปิด Output ได้
13. ปิดและเปิดโปรเจกต์เดิมได้
14. งานที่ล้มเหลวมี Error ที่เข้าใจได้
15. Source file ไม่ถูกเขียนทับ
16. Buffer, Cache และ Temporary files ถูกจัดการ
17. Portable EXE ผ่านการ Build
18. มี Smoke Test สำหรับ Workflow จริง

---

## 41. ลำดับการพัฒนา

อย่าพยายามสร้างทุกอย่างพร้อมกัน

### Phase 1: Core Video Editor

- Project Manager
- Import
- FFprobe
- Preview
- Timeline
- Split
- Trim
- Delete
- Audio waveform
- Export MP4
- Auto Save
- Job Queue

### Phase 2: AI Speech Editing

- Speech-to-Text
- Subtitle
- Silence detection
- Remove filler words
- Transcript editing
- Search transcript
- Export SRT

### Phase 3: Social Media Automation

- Highlight detection
- Shorts generation
- Auto reframe
- Animated captions
- Hook and CTA
- Platform export presets

### Phase 4: Audio and Visual Enhancement

- Noise reduction
- Voice enhancement
- Color correction
- Stabilization
- Beat detection
- Music ducking

### Phase 5: Advanced AI

- B-roll suggestion
- Object detection
- Face tracking
- Privacy blur
- Text-to-video
- AI voiceover
- Document-to-video

### Phase 6: Professional Workflow

- Multicam
- Keyframes
- Nested sequences
- Plugin system
- Team collaboration
- Cloud providers
- Social publishing

---

## 42. วิธีดำเนินงานของ Coding Agent

เริ่มจาก:

1. ตรวจสอบ Repository ปัจจุบัน
2. สรุปโครงสร้างที่มีอยู่
3. สร้าง Architecture Document
4. สร้าง Feature Matrix
5. แบ่ง Phase
6. สร้างโครงสร้างโปรเจกต์
7. สร้าง Core ที่ Compile ได้
8. สร้าง Tests
9. Build บน Windows
10. สร้าง Portable Artifact
11. ทดสอบ Workflow ด้วยวิดีโอจริงขนาดเล็ก
12. บันทึกผลการทดสอบ

ทุกครั้งที่เพิ่มฟังก์ชัน:

- เขียนโค้ดจริง
- เพิ่ม Test
- Build
- ตรวจ Output
- บันทึกข้อจำกัด
- อย่าอ้างว่าสำเร็จจากการ Compile อย่างเดียว

---

## 43. รูปแบบรายงานหลังทำงาน

หลังจบแต่ละ Phase ให้รายงาน:

### สิ่งที่ทำเสร็จ

- รายการ Feature
- ไฟล์ที่เพิ่ม
- ไฟล์ที่แก้ไข

### สิ่งที่ทดสอบ

- Test command
- Input
- Output
- Result

### สิ่งที่ยังไม่รองรับ

- Feature
- เหตุผล
- Dependency
- แผนพัฒนาต่อ

### Artifact

- EXE path
- ZIP path
- SHA-256
- Build version
- Commit SHA

---

## 44. ข้อห้าม

ห้าม:

- สร้างปุ่มที่กดแล้วไม่ทำงาน
- แสดง Progress ปลอม
- สร้าง Output ปลอม
- อ้างว่า AI วิเคราะห์แล้วโดยไม่มีโมเดลหรือผลลัพธ์
- แก้ไฟล์ต้นฉบับโดยไม่ยืนยัน
- เขียนทับ Output เดิมโดยอัตโนมัติ
- รันคำสั่ง Shell ที่ประกอบจากข้อความผู้ใช้โดยไม่ Escape
- ส่งวิดีโอขึ้น Cloud โดยไม่แจ้ง
- ฝัง API Key ในโค้ด
- ดาวน์โหลดสื่อที่ละเมิดลิขสิทธิ์
- โคลนเสียงหรือใบหน้าบุคคลอื่นโดยไม่มีความยินยอม

---

## 45. เป้าหมายสุดท้าย

โปรแกรมต้องทำให้ผู้ใช้สามารถพิมพ์ว่า:

“เอาวิดีโอนี้มาตัดช่วงเงียบออก ใส่ Subtitle ภาษาไทย เลือกช่วงสำคัญที่สุด ทำเป็นคลิปแนวตั้ง 3 คลิป ใส่ชื่อเรื่อง ปรับเสียงให้ชัด และ Export สำหรับ TikTok”

แล้วระบบสามารถ:

1. วิเคราะห์ไฟล์
2. แสดงแผนการทำงาน
3. แจ้งสิ่งที่จะเปลี่ยน
4. สร้าง Draft
5. ให้ผู้ใช้ Preview
6. แก้ไขได้
7. Export ผลลัพธ์จริง
8. เก็บ Project, Log และ Report
9. ไม่แก้ไฟล์ต้นฉบับ
10. ทำงานต่อได้เมื่อโปรแกรมถูกปิด

เริ่มดำเนินการจาก Phase 1 ก่อน และต้องทำให้ Workflow แรกใช้งานได้จริงตั้งแต่ต้นจนจบ ก่อนเพิ่มฟังก์ชันขั้นสูง