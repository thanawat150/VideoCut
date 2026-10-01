---
name: ep01-satun-video-edit
description: >
  ตัดต่อวิดีโอ EP.1 ชุดลงพื้นที่จังหวัดสตูลจากไฟล์ใน 05_EDIT_ASSETS
  โดยใช้ Master Edit Guide เป็นแหล่งอ้างอิงหลัก สร้าง Rough Cut แนวตั้ง 9:16
  ใช้ Voice Over เป็นแกนเรื่อง รักษา Original Audio ของงานภาคสนาม
  ใส่ Music, SFX, Text และ Subtitle ตามแผน และเปิดให้มนุษย์แก้ไขก่อน Export
---

# EP.1 SATUN VIDEO EDITING SKILL

## Goal

สร้างวิดีโอ EP.1 จาก Asset ที่เตรียมไว้ โดยยึดหลัก:

- Story ก่อน Effect
- ภาพจริงและเสียงจริงเป็นพระเอก
- Voice Over เป็นแกนการเล่าเรื่อง
- Original Audio ต้องยังมีชีวิต
- ไม่ตัดเร็วเกินไป
- มี Breathing Shot
- SFX ใช้เฉพาะจุดสำคัญ
- ทุก Automation ต้องแก้ไขด้วยมือได้
- Non-destructive editing
- ไม่ใช้ Subtitle ภาษาไทยใน EP.1
- ความยาวเป้าหมายใหม่ 120–180 วินาที โดยเป้าหมายกลางประมาณ 140 วินาที

## 1. Project Structure

ใช้โครงสร้าง:

```text
EP1/
└── 05_EDIT_ASSETS/
    ├── 01_VIDEO/
    ├── 02_VOICE_OVER/
    ├── 03_MAP/
    ├── 04_MUSIC/
    ├── 05_SFX/
    └── 06_GUIDE/
```

`05_EDIT_ASSETS` คือ Source หลักของงานตัดต่อ EP.1

ห้ามแก้ไข Source Asset โดยตรง

## 2. Video Assets

Folder:

```text
05_EDIT_ASSETS/01_VIDEO/
```

ไฟล์:

```text
01_JOURNEY_ROAD_IMG1301.mp4
02_JOURNEY_BOAT_IMG1302.mp4
03_JOURNEY_BOAT_MANGROVE_IMG1303.mp4
04_FIELD_WALK_ROOTS_IMG1309.mp4
05_FIELD_ENVIRONMENT_IMG1380.mp4
06_NEXT_MEASURE_01_IMG1331.mp4
07_NEXT_MEASURE_02_IMG1332.mp4
08_NEXT_MEASURE_03_IMG1333.mp4
09_NEXT_MEASURE_04_IMG1334.mp4
10_NEXT_MEASURE_05_IMG1335.mp4
```

Primary:

```text
01 02 03 04 05 06 08 10
```

Backup / Insert:

```text
07 09
```

## 3. Voice Over

Folder:

```text
05_EDIT_ASSETS/02_VOICE_OVER/
```

Mapping:

```text
VO1 = 01_VO_HOOK.mp3
VO2 = 02_VO_JOURNEY.mp3
VO3 = 03_VO_FIELD.mp3
VO4 = 04_VO_PURPOSE.mp3
VO5 = 05_VO_NEXT_EP.mp3
```

### VO1

```text
ปกติเวลาดูแผนที่
มันก็เหมือนเป็นแค่จุดหนึ่งจุด

แต่พอมาลงพื้นที่จริง... โห
มันไม่ได้ง่ายแบบนั้นเลย
```

### VO2

```text
อย่างจุดที่ผมไปเจอมา

ต้องนั่งเรือเข้าไปก่อน
แล้วก็เดินต่อเข้าไปอีก

กว่าจะถึงจุดที่เราจะทำงานจริง ๆ
เหนื่อยสุด ๆ
```

### VO3

```text
พอเข้ามาถึงพื้นที่
ก็จะประมาณนี้เลย

มีทั้งโคลน ทั้งรากไม้
บางช่วงก็เดินง่าย
บางช่วงยากก็ต้องค่อย ๆ ไป

ดูในแผนที่
เราไม่เห็นอะไรพวกนี้เลย
```

### VO4

```text
แล้ววันนี้ที่ผมเข้ามา
หลัก ๆ คือเรามาเก็บข้อมูลต้นไม้ในพื้นที่

พอมาถึงแล้ว
ถึงจะเริ่มทำงานกันจริง ๆ
```

### VO5

```text
แต่จริง ๆ คำว่าเก็บข้อมูลต้นไม้เนี่ย...

เขาเก็บอะไรกันบ้าง?

เดี๋ยวคลิปหน้าผมพาไปดู
```

ห้ามเปลี่ยนคำ VO เอง หากผู้ใช้ไม่ได้สั่ง

## 4. Map

Folder:

```text
05_EDIT_ASSETS/03_MAP/
```

ใช้:

```text
EP01_MAP_SCREEN_RECORD.mp4
```

Concept:

```text
จุดบนแผนที่ → Zoom → ของจริงในพื้นที่
```

Map มีหน้าที่ตั้งโจทย์ ไม่ควรอยู่นานเกินความจำเป็น

## 5. Music

Folder:

```text
05_EDIT_ASSETS/04_MUSIC/
```

ใช้:

```text
EP01_MUSIC_MAIN.mp3
```

Priority:

```text
VO / Original Audio > Music
```

## 6. SFX

Folder:

```text
05_EDIT_ASSETS/05_SFX/
```

ใช้:

```text
Interface Click.mp3
Thin Swoosh.mp3
Cinematic Low Hit.mp3
Swoosh Riser Reverb.mp3
```

SFX เป็น Accent เท่านั้น

ห้ามใช้แทน Original Audio ของเรือ น้ำ ป่า ลม กิ่งไม้ โคลน การเดิน หรือเสียงคนทำงาน

## 7. Edit Guide

Folder:

```text
05_EDIT_ASSETS/06_GUIDE/
```

ใช้:

```text
EP01 Master Edit Guide
```

อ่าน Sheet:

```text
Master Timeline
Asset Map
Track Setup
```

`Master Timeline` เป็น Source of Truth หลัก

ถ้า Skill นี้กับ Master Timeline ขัดกัน ให้ใช้ Master Timeline ล่าสุด

## 8. Project Format

Default:

```text
Aspect Ratio: 9:16
Resolution: 1080 × 1920
FPS: Source FPS
Fallback FPS: 30
Video: H.264
Container: MP4
Audio: AAC
Sample Rate: 48 kHz
```

Source ส่วนใหญ่เป็น 1920×1080 16:9

Reframe เป็น 9:16 ตามลำดับ:

1. Center Crop
2. ตรวจ Subject
3. ปรับ Position
4. Keyframe Pan ถ้าจำเป็น

ห้าม Crop คน มือ อุปกรณ์วัด ต้นไม้ รากไม้ หรือการเดินที่เป็นสาระสำคัญออกจากเฟรม

## 9. Track Structure

```text
V4  UNUSED — Subtitle Disabled
V3  Text / Graphics
V2  Map / Overlay
V1  Main Video

A4  SFX
A3  Music
A2  Voice Over
A1  Original Audio
```

## 10. Story

```text
1 จุดบนแผนที่
↓
พื้นที่จริงไม่ได้ง่าย
↓
ต้องเดินทางเข้าไป
↓
นั่งเรือ
↓
เดินเข้าป่า
↓
โคลน + รากไม้
↓
ถึงพื้นที่ทำงาน
↓
เริ่มเก็บข้อมูลต้นไม้
↓
คำถาม: “เขาเก็บอะไรกันบ้าง?”
↓
ส่งต่อ EP.2
```

ห้ามอธิบายวิธีวัดต้นไม้ละเอียดใน EP.1

## 11. Master Timeline

Timing และ Source In/Out ใน Skill นี้เป็นค่าล็อกตายตัว ห้าม Agent เปลี่ยนเอง เว้นแต่ผู้ใช้สั่งแก้โดยตรง

### 00:00–00:02 — Cold Open

Video:

```text
04_FIELD_WALK_ROOTS_IMG1309.mp4
Source: 00:00 → 00:02
```

VO: ไม่มี

Original Audio:

```text
Hero
-4 ถึง -2 dB
```

Music: ยังไม่เข้า

Text:

```text
1 POINT
```

Transition: Hard Cut

### 00:02–00:06 — Map

Video:

```text
EP01_MAP_SCREEN_RECORD.mp4
```

Source ล็อกตายตัว: 00:00.00 → 00:04.00

VO: VO1 เริ่ม

Map Audio: Mute

Music:

```text
Fade In
ประมาณ -28 dB
```

SFX:

```text
Interface Click.mp3
@ 00:02
ประมาณ -18 ถึง -14 dB
```

ใช้ครั้งเดียวตอน Point/Pin ปรากฏ

Text:

```text
1 POINT
```

### 00:06–00:10.12 — Map → Real Field

Video:

```text
03_JOURNEY_BOAT_MANGROVE_IMG1303.mp4
Source: 00:00 → 00:04.12
```

VO: VO1 ต่อ

Original Audio:

```text
ประมาณ -22 dB
ต้องยังได้ยินน้ำ/เรือเบา ๆ
```

Music:

```text
ประมาณ -28 dB
```

SFX:

```text
Thin Swoosh.mp3
@ 00:06
ประมาณ -18 ถึง -14 dB
วางคร่อมรอยตัด Map → Field
```

Text:

```text
พื้นที่จริง
```

### 00:10.12–00:14.12 — Reality

Video:

```text
04_FIELD_WALK_ROOTS_IMG1309.mp4
Source: 00:02 → 00:06
```

VO: VO1 จบ

Original Audio: ~ -22 dB

Music: ~ -28 dB

SFX:

```text
Cinematic Low Hit.mp3
@ 00:10.12
ประมาณ -22 ถึง -18 dB
ใช้เบามาก
```

Text:

```text
ของจริง
```

ห้ามทำ Low Hit ใหญ่จนเหมือน Trailer

### 00:14.12–00:18.12 — Road

Video:

```text
01_JOURNEY_ROAD_IMG1301.mp4
Source: 00:00 → 00:04
```

VO: VO2 เริ่ม

Original Audio: ~ -20 dB

Music: ~ -27 dB

### 00:18.12–00:22.12 — Boat

Video:

```text
02_JOURNEY_BOAT_IMG1302.mp4
Source: 00:00 → 00:04
```

VO: VO2 ต่อ

Text:

```text
นั่งเรือ
```

เสียงเรือ/น้ำต้องยังได้ยิน

### 00:22.12–00:26.50 — Mangrove

Video:

```text
03_JOURNEY_BOAT_MANGROVE_IMG1303.mp4
Source: 00:04.12 → 00:08.50
```

VO: VO2 จบ

Text:

```text
เข้าป่า
```

### 00:26.50–00:28.50 — Breathing Shot

Video:

```text
03_JOURNEY_BOAT_MANGROVE_IMG1303.mp4
Source: 00:08.50 → 00:10.50
```

VO: ไม่มี

Original Audio:

```text
-6 ถึง -3 dB
```

Music:

```text
~ -24 dB
```

ห้ามตัด Breathing Shot นี้ทิ้งอัตโนมัติ

### 00:28.50–00:37.50 — Field / Roots

Video:

```text
04_FIELD_WALK_ROOTS_IMG1309.mp4
Source: 00:06 → 00:15
```

VO: VO3 เริ่ม

Text:

```text
โคลน
รากไม้
```

Original Audio: ~ -20 dB

### 00:37.50–00:45.59 — Environment

Video:

```text
05_FIELD_ENVIRONMENT_IMG1380.mp4
Source: 00:00.00 → 00:08.09
```

VO: VO3 จบ

Text:

```text
ของจริง
```

ให้ภาพหายใจ ห้ามเร่ง Cut

### ~00:45.5 — Optional SFX

```text
Swoosh Riser Reverb.mp3
ประมาณ -20 ถึง -16 dB
```

ใช้เฉพาะถ้า Transition เข้า FIELD DATA ยังรู้สึกแบน

ถ้าไม่จำเป็น ไม่ใช้

### 00:45.59–00:49.09 — Start Work

Video:

```text
06_NEXT_MEASURE_01_IMG1331.mp4
Source: 00:00.00 → 00:03.50
```

VO: VO4 เริ่ม

Text:

```text
FIELD DATA
```

### 00:49.09–00:52.09 — Insert

Video:

```text
07_NEXT_MEASURE_02_IMG1332.mp4
Source: 00:00.00 → 00:03.00
```

VO: VO4 ต่อ

Text:

```text
เริ่มงาน
```

ใช้เป็น Insert ไม่ต้องอธิบายวิธีการวัด

### 00:52.09–00:56.68 — Measure

Video:

```text
08_NEXT_MEASURE_03_IMG1333.mp4
Source ประมาณ 00:00 → 00:04.59
```

VO: VO4 จบ

ให้ภาพการทำงานจริงเล่าเรื่อง

### 00:56.68–00:58 — Breathing Shot

Video:

```text
08_NEXT_MEASURE_03_IMG1333.mp4
```

VO: ไม่มี

Original Audio:

```text
~ -6 dB
```

พักก่อนคำถามท้ายคลิป

### 00:58–01:02 — Next EP Question

Video:

```text
08_NEXT_MEASURE_03_IMG1333.mp4
```

VO: VO5 เริ่ม

Text:

```text
เก็บอะไรบ้าง?
```

### 01:02–01:04 — Detail Insert

Video:

```text
09_NEXT_MEASURE_04_IMG1334.mp4
Source: 00:00.00 → 00:02.00
```

VO: VO5 ต่อ

### 01:04–01:06.16 — Tease

Video:

```text
10_NEXT_MEASURE_05_IMG1335.mp4
```

VO: VO5 จบ

Text:

```text
เก็บอะไรบ้าง?
```

Music: เริ่ม Fade Out

### 01:06.16–01:08 — End Hold

Video:

```text
10_NEXT_MEASURE_05_IMG1335.mp4
Source: 00:02.16 → 00:04.00
```

VO: ไม่มี

Original Audio:

```text
Hero
-4 ถึง -2 dB
```

Music: Fade Out

Text:

```text
คลิปหน้าผมพาไปดู
```

ปล่อย Natural Audio ประมาณ 1–2 วินาทีก่อนจบ


## 11A. Deterministic Source Lock

Source In/Out ทุกแถวของ EP.1 ต้องเป็นค่าตายตัวตามรายการต่อไปนี้ และ Agent ห้ามตีความคำว่า “เลือกช่วงที่ดีที่สุด”, “ประมาณ”, “ใช้ทั้งคลิป”, หรือปรับช่วงเอง:

```text
00:00.00–00:02.00 | 04_FIELD_WALK_ROOTS_IMG1309.mp4        | 00:00.00 → 00:02.00
00:02.00–00:06.00 | EP01_MAP_SCREEN_RECORD.mp4             | 00:00.00 → 00:04.00
00:06.00–00:10.12 | 03_JOURNEY_BOAT_MANGROVE_IMG1303.mp4  | 00:00.00 → 00:04.12
00:10.12–00:14.12 | 04_FIELD_WALK_ROOTS_IMG1309.mp4        | 00:02.00 → 00:06.00
00:14.12–00:18.12 | 01_JOURNEY_ROAD_IMG1301.mp4            | 00:00.00 → 00:04.00
00:18.12–00:22.12 | 02_JOURNEY_BOAT_IMG1302.mp4            | 00:00.00 → 00:04.00
00:22.12–00:26.50 | 03_JOURNEY_BOAT_MANGROVE_IMG1303.mp4  | 00:04.12 → 00:08.50
00:26.50–00:28.50 | 03_JOURNEY_BOAT_MANGROVE_IMG1303.mp4  | 00:08.50 → 00:10.50
00:28.50–00:37.50 | 04_FIELD_WALK_ROOTS_IMG1309.mp4        | 00:06.00 → 00:15.00
00:37.50–00:45.59 | 05_FIELD_ENVIRONMENT_IMG1380.mp4       | 00:00.00 → 00:08.09
00:45.59–00:49.09 | 06_NEXT_MEASURE_01_IMG1331.mp4         | 00:00.00 → 00:03.50
00:49.09–00:52.09 | 07_NEXT_MEASURE_02_IMG1332.mp4         | 00:00.00 → 00:03.00
00:52.09–00:56.68 | 08_NEXT_MEASURE_03_IMG1333.mp4         | 00:00.00 → 00:04.59
00:56.68–00:58.00 | 08_NEXT_MEASURE_03_IMG1333.mp4         | 00:04.59 → 00:05.91
00:58.00–01:02.00 | 08_NEXT_MEASURE_03_IMG1333.mp4         | 00:05.91 → 00:09.91
01:02.00–01:04.00 | 09_NEXT_MEASURE_04_IMG1334.mp4         | 00:00.00 → 00:02.00
01:04.00–01:06.16 | 10_NEXT_MEASURE_05_IMG1335.mp4         | 00:00.00 → 00:02.16
01:06.16–01:08.00 | 10_NEXT_MEASURE_05_IMG1335.mp4         | 00:02.16 → 00:04.00
```

ถ้า Source File สั้นกว่าช่วงที่ล็อกไว้ ให้ Preflight เป็น ERROR และห้าม Render ต่อ ห้ามเลือกช่วงใหม่แทนเอง

## 12. Audio Mixing Rules

Priority:

```text
Original Audio + VO
↓
Music
↓
SFX
```

### Original Audio

ช่วงมี VO:

```text
-22 ถึง -20 dB
```

ช่วงไม่มี VO:

```text
-6 ถึง -2 dB
```

ห้ามปิด Original Audio อัตโนมัติ

### Voice Over

Peak:

```text
ประมาณ -6 ถึง -3 dB
```

VO ต้องชัดที่สุด

### Music

ช่วงมี VO:

```text
-30 ถึง -27 dB
```

ช่วงไม่มี VO:

```text
-25 ถึง -22 dB
```

ถ้า Music รบกวน Natural Sound ให้ลด Music ก่อน

## 13. SFX Plan

```text
00:02     Interface Click
00:06     Thin Swoosh
00:10.12  Cinematic Low Hit
~00:45.5  Swoosh Riser Reverb [OPTIONAL]
```

ไม่ต้องเพิ่ม Boat / Forest / Footstep Foley ถ้าเสียงจริงดี

## 14. Text On Screen

ใช้เฉพาะ Keyword:

```text
1 POINT
พื้นที่จริง
ของจริง
นั่งเรือ
เข้าป่า
โคลน
รากไม้
FIELD DATA
เริ่มงาน
เก็บอะไรบ้าง?
คลิปหน้าผมพาไปดู
```

กฎ:

- 2–5 คำต่อครั้ง
- Text ไม่ใช่ Subtitle
- ต้องอยู่ Safe Zone ของ Short-form Video

## 15. Subtitle

EP.1 เวอร์ชันใหม่นี้ **ปิด Subtitle ทั้งหมด**

กฎ:

```text
ห้ามสร้าง SRT
ห้ามสร้าง ASS
ห้าม Burn-in Subtitle
ห้ามใช้ Whisper เพื่อสร้าง Caption สำหรับ EP.1
```

ให้ใช้เฉพาะ Text On Screen แบบ Keyword บน V3 เท่านั้น

เหตุผล: คุณภาพภาษาไทยจากระบบ Subtitle ยังไม่เป็นที่พอใจ และผู้ใช้สั่งให้เอา Subtitle ออกจาก EP.1

## 16. Transitions

Preferred:

```text
Straight Cut
Hard Cut
Cut on Action
```

หลีกเลี่ยง:

```text
Flash ทุก Cut
Spin
Glitch
Zoom ทุกช็อต
Transition ที่ดูเป็น Template มากเกินไป
```

เป้าหมาย:

```text
Natural Field Documentary
```

## 17. Automation Workflow

เมื่อ Agent ได้รับคำสั่งให้ทำ EP.1 เวอร์ชันใหม่:

1. Locate EP1
2. Locate `05_EDIT_ASSETS`
3. กลับไป Raw Footage Day 1 / Day 2 เพื่อคัดคลิปใหม่ ไม่จำกัดแค่ Select 10 คลิปเดิม
4. Read `Long Cut Plan` จาก Master Edit Guide
5. Preview Candidate Footage
6. เลือก Source In/Out ร่วมกับผู้ใช้
7. Validate Asset Names
8. Probe Duration / Resolution / FPS / Audio
9. Build Long Rough Cut เป้าหมาย 120–180 วินาที
10. Build Original Audio
11. Add VO
12. Add Music
13. Add SFX
14. Add Text On Screen
15. ห้าม Add Subtitle
16. Reframe 9:16
17. Generate Preview
18. Stop for Human Review
19. Apply Human Changes
20. Export Final

ห้าม Auto Export Final ทันทีหลัง Build Rough Cut

## 18. Human Override

Automation ทุกอย่างต้องแก้ได้

ผู้ใช้ต้องสามารถ:

```text
เปลี่ยน Clip
Move
Trim
Split
Delete
Crop
Position
Scale
Volume
Text
Music
SFX
Transition
```

Automation คือ Draft ไม่ใช่ Final Decision

## 19. Validation Before Rough Cut

ตรวจ:

```text
Video 10/10
VO 5/5
Map 1/1
Music 1/1
SFX 4/4
Guide พบ
```

ถ้าขาด ให้แจ้ง:

```text
MISSING ASSET
Filename
Expected Folder
Affected Timeline
```

ห้าม Crash

## 20. Validation Before Export

ตรวจ:

```text
No Missing Media
No accidental Black Frame
VO Sync
Music ไม่กลบ VO
Original Audio ยังได้ยิน
SFX ไม่ดังเกินไป
Text อยู่ Safe Zone
Subtitle อ่านได้
Audio ไม่ Clip
Crop ไม่ตัด Subject สำคัญ
มี Breathing Shot
Ending มี Natural Audio
```

## 21. Review

ก่อน Final Export ให้ตรวจ 3 รอบ

### Story Review

ตรวจว่าคนดูเข้าใจ:

```text
1 จุดบนแผนที่ vs ของจริงในพื้นที่
```

### Visual Review

ปิดเสียงแล้วดู:

```text
Shot Flow
Crop
Reframe
Text
Transition
```

### Audio Review

ไม่มองภาพแล้วฟัง:

```text
VO
Natural Sound
Music
SFX
Breathing
Ending
```

## 22. Success Criteria

EP.1 ผ่านเมื่อ:

- Hook เข้าใจเร็ว
- เห็นความต่างของ Map กับพื้นที่จริง
- รู้สึกถึงการเดินทางและความลำบากหน้างาน
- เห็นงานจริง
- เสียงธรรมชาติยังดี
- VO ชัด
- Music ไม่กลบ
- SFX ไม่เยอะ
- ไม่เล่า EP.2 ล่วงหน้าหมด
- จบด้วยคำถาม
- ทำให้คนอยากดูตอนต่อไป

## 23. Do Not

ห้าม:

- สร้างข้อมูลภาคสนามที่ไม่มี
- แต่ง Methodology เอง
- อธิบาย MRV เพิ่มเอง
- ใส่เสียงเรือปลอมโดยไม่จำเป็น
- ใส่ SFX ทุก Cut
- ตัด Breathing Shot ออกอัตโนมัติ
- เปลี่ยนคำ VO เอง
- บังคับคลิปสั้นกว่า 2 นาทีโดยไม่มีเหตุผล
- ทำคลิปให้ดูเหมือน Corporate Presentation
- ใช้ Effect มากกว่าภาพจริง

## 24. Decision Priority

เมื่อต้องตัดสินใจ:

```text
1. Story
2. Human Feeling
3. Original Audio
4. VO Clarity
5. Visual Continuity
6. Natural Pacing
7. Text
8. Music
9. SFX
10. Effect
```

## Scope

Skill นี้ครอบคลุมเฉพาะ:

```text
การทำ EP.1
Asset Selection
Rough Cut
Timeline
VO
Original Audio
Music
SFX
Text
Subtitle
Reframe
Review
Export
```

ไม่รวม:

```text
การสร้างโปรแกรม Desktop
การออกแบบ Architecture โปรแกรม
การ Build .exe
Prompt สร้าง Auto Video Editor
การเขียน Application Code
```


## 25. Long Cut Reset — Current Working Mode

EP.1 อยู่ในสถานะกลับไปคัด Footage ใหม่

ใช้แท็บ `Long Cut Plan` ใน `EP01 Master Edit Guide` เป็นพื้นที่ทำงานปัจจุบัน

หลัก:

```text
Target Runtime: 120–180 seconds
Preferred Runtime: ~140 seconds
Subtitle: OFF
Text On Screen: ON
Original Audio: HIGH PRIORITY
Old 1:08 timeline: reference only, not final
```

Candidate Raw Footage ที่ควร Review เพิ่ม:

```text
IMG_1382.MOV
IMG_1383.MOV
IMG_1410.MOV
IMG_1411.MOV
IMG_1412.MOV
```

ห้าม Lock Source In/Out ของ Candidate เหล่านี้จนกว่าจะ Preview และผู้ใช้เห็นชอบ
