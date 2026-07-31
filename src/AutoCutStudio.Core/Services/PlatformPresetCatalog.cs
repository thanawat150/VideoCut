using AutoCutStudio.Core.Models;

namespace AutoCutStudio.Core.Services;

public sealed class PlatformPresetCatalog
{
    private static readonly IReadOnlyList<SocialExportPreset> Presets =
    [
        new()
        {
            Id = "tiktok",
            PlatformId = "tiktok",
            DisplayName = "TikTok",
            Width = 1080,
            Height = 1920,
            FrameRate = 30,
            AspectStrategy = "center_crop",
            VideoBitrateKbps = 8000,
            AudioBitrateKbps = 192,
            SuggestedMaximumDurationSeconds = 60,
            Pacing = "fast",
            EnableHook = true,
            EnableCta = true,
            CaptionSafeZone = new CaptionSafeZone
            {
                MarginLeft = 112,
                MarginRight = 176,
                MarginBottom = 310,
                MarginTop = 150,
                MaximumLines = 2,
                MaximumWidthRatio = 0.76,
                PreferredFontSize = 60,
                MinimumFontSize = 44
            }
        },
        new()
        {
            Id = "instagram_reels",
            PlatformId = "instagram",
            DisplayName = "Instagram Reels",
            Width = 1080,
            Height = 1920,
            FrameRate = 30,
            AspectStrategy = "center_crop",
            VideoBitrateKbps = 8000,
            AudioBitrateKbps = 192,
            SuggestedMaximumDurationSeconds = 90,
            Pacing = "clean",
            EnableHook = true,
            EnableCta = true,
            CaptionSafeZone = new CaptionSafeZone
            {
                MarginLeft = 104,
                MarginRight = 150,
                MarginBottom = 290,
                MarginTop = 160,
                MaximumLines = 2,
                MaximumWidthRatio = 0.78,
                PreferredFontSize = 58,
                MinimumFontSize = 44
            }
        },
        new()
        {
            Id = "facebook_reels",
            PlatformId = "facebook",
            DisplayName = "Facebook Reels",
            Width = 1080,
            Height = 1920,
            FrameRate = 30,
            AspectStrategy = "center_crop",
            VideoBitrateKbps = 8000,
            AudioBitrateKbps = 192,
            SuggestedMaximumDurationSeconds = 90,
            Pacing = "balanced",
            EnableHook = true,
            EnableCta = true,
            CaptionSafeZone = new CaptionSafeZone
            {
                MarginLeft = 100,
                MarginRight = 150,
                MarginBottom = 285,
                MarginTop = 150,
                MaximumLines = 2,
                MaximumWidthRatio = 0.79,
                PreferredFontSize = 58,
                MinimumFontSize = 44
            }
        },
        new()
        {
            Id = "facebook_feed",
            PlatformId = "facebook",
            DisplayName = "Facebook Feed 4:5",
            Width = 1080,
            Height = 1350,
            FrameRate = 30,
            AspectStrategy = "center_crop",
            VideoBitrateKbps = 7500,
            AudioBitrateKbps = 192,
            SuggestedMaximumDurationSeconds = 180,
            Pacing = "balanced",
            EnableHook = true,
            EnableCta = true,
            CaptionSafeZone = new CaptionSafeZone
            {
                MarginLeft = 90,
                MarginRight = 90,
                MarginBottom = 140,
                MarginTop = 100,
                MaximumLines = 2,
                MaximumWidthRatio = 0.83,
                PreferredFontSize = 54,
                MinimumFontSize = 40
            }
        },
        new()
        {
            Id = "youtube_shorts",
            PlatformId = "youtube",
            DisplayName = "YouTube Shorts",
            Width = 1080,
            Height = 1920,
            FrameRate = 30,
            AspectStrategy = "center_crop",
            VideoBitrateKbps = 9000,
            AudioBitrateKbps = 192,
            SuggestedMaximumDurationSeconds = 180,
            Pacing = "fast",
            EnableHook = true,
            EnableCta = true,
            CaptionSafeZone = new CaptionSafeZone
            {
                MarginLeft = 105,
                MarginRight = 160,
                MarginBottom = 300,
                MarginTop = 150,
                MaximumLines = 2,
                MaximumWidthRatio = 0.78,
                PreferredFontSize = 60,
                MinimumFontSize = 44
            }
        },
        new()
        {
            Id = "youtube_landscape",
            PlatformId = "youtube",
            DisplayName = "YouTube 16:9",
            Width = 1920,
            Height = 1080,
            FrameRate = 30,
            AspectStrategy = "fit_pad",
            VideoBitrateKbps = 10000,
            AudioBitrateKbps = 192,
            SuggestedMaximumDurationSeconds = 3600,
            Pacing = "story",
            EnableHook = false,
            EnableCta = true,
            CaptionSafeZone = new CaptionSafeZone
            {
                MarginLeft = 150,
                MarginRight = 150,
                MarginBottom = 105,
                MarginTop = 80,
                MaximumLines = 2,
                MaximumWidthRatio = 0.82,
                PreferredFontSize = 52,
                MinimumFontSize = 38
            }
        },
        new()
        {
            Id = "square_feed",
            PlatformId = "generic",
            DisplayName = "Square Feed 1:1",
            Width = 1080,
            Height = 1080,
            FrameRate = 30,
            AspectStrategy = "center_crop",
            VideoBitrateKbps = 7000,
            AudioBitrateKbps = 192,
            SuggestedMaximumDurationSeconds = 180,
            Pacing = "balanced",
            EnableHook = true,
            EnableCta = true,
            CaptionSafeZone = new CaptionSafeZone
            {
                MarginLeft = 86,
                MarginRight = 86,
                MarginBottom = 100,
                MarginTop = 86,
                MaximumLines = 2,
                MaximumWidthRatio = 0.84,
                PreferredFontSize = 50,
                MinimumFontSize = 38
            }
        }
    ];

    public IReadOnlyList<SocialExportPreset> GetAll() => Presets;

    public SocialExportPreset Get(string? id)
    {
        var value = string.IsNullOrWhiteSpace(id) ? "tiktok" : id.Trim();
        return Presets.FirstOrDefault(item => string.Equals(item.Id, value, StringComparison.OrdinalIgnoreCase))
               ?? throw new KeyNotFoundException($"ไม่พบ Platform Preset: {value}");
    }

    public bool TryGet(string? id, out SocialExportPreset preset)
    {
        preset = Presets.FirstOrDefault(item =>
                     string.Equals(item.Id, id?.Trim(), StringComparison.OrdinalIgnoreCase))!;
        return preset is not null;
    }
}
