# TikTok Affiliate Queue

A small GitHub Pages dashboard for Ken's TikTok affiliate posting pattern.

## What it does
- Keeps the agreed five daily slots: 09:00, 12:00, 15:00, 18:00, 21:00 Bangkok time.
- Marks existing TikTok posts as locked. The collision resolver moves only new items that share the exact date and time, preserving their time on the next unoccupied day.
- Stores queue details and metrics in this browser's localStorage; export JSON regularly for backup or use the import control on another browser.
- Keeps product pack uncertainty visible. Siriraj tinted sunscreen is prefilled; the shampoo listing needs a confirmed 1-bottle or 2-bottle link before basket attachment.
- Tracks views, basket clicks, orders and commission after publishing.
- Opens TikTok Studio for manual upload and scheduling.

## Host on GitHub Pages
After merging this change into `main`, open **Settings → Pages**, choose **Deploy from a branch**, select `main` and `/docs`, then save. The dashboard will be at:

`https://thanawat150.github.io/VideoCut/affiliate-queue/`

## Publishing boundary
This static dashboard does not sign in to TikTok, attach a product, submit a post, or read TikTok analytics. It does not store video files or account credentials. Confirm the exact product variant, caption, disclosure and time in TikTok Studio before scheduling. The dashboard's numbers are manually entered; they are not live tracking.
