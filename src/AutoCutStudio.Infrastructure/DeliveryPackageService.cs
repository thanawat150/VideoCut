using System.Security.Cryptography;
using System.Text.Json;
using AutoCutStudio.Core.Models;
using AutoCutStudio.Core.Services;

namespace AutoCutStudio.Infrastructure;

public sealed class DeliveryPackageService
{
    public IReadOnlyList<ProviderCapability> GetCapabilities() =>
    [
        new ProviderCapability
        {
            ProviderId = "local-folder",
            DisplayName = "Local / Synced Cloud Folder",
            Category = "cloud",
            IsConfigured = true,
            CanPublishDirectly = true,
            Message = "ส่งไฟล์จริงไปยังโฟลเดอร์ Local, OneDrive, Google Drive Desktop หรือ Network Drive"
        },
        new ProviderCapability
        {
            ProviderId = "youtube",
            DisplayName = "YouTube Data API",
            Category = "social",
            IsConfigured = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("AUTOCUT_YOUTUBE_CLIENT_ID")),
            CanPublishDirectly = false,
            Message = "สร้าง Publishing Package ได้ รุ่นนี้ไม่ส่งวิดีโอผ่าน API จนกว่าจะติดตั้ง Provider Plugin และยืนยันบัญชี"
        },
        new ProviderCapability
        {
            ProviderId = "tiktok",
            DisplayName = "TikTok Content Posting API",
            Category = "social",
            IsConfigured = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("AUTOCUT_TIKTOK_CLIENT_KEY")),
            CanPublishDirectly = false,
            Message = "สร้าง Publishing Package ได้ รุ่นนี้ไม่อ้างว่าส่งสำเร็จโดยไม่มี Provider Plugin และสิทธิ์ API"
        }
    ];

    public async Task<DeliveryPackageManifest> DeliverToFolderAsync(
        string sourceVideo,
        string destinationDirectory,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(sourceVideo)) throw new FileNotFoundException("ไม่พบ Output ที่ต้องการส่งมอบ", sourceVideo);
        var destination = Path.GetFullPath(destinationDirectory);
        Directory.CreateDirectory(destination);
        var fileName = Path.GetFileName(sourceVideo);
        var output = NextAvailablePath(destination, fileName);
        var temporary = output + ".partial";
        TryDelete(temporary);
        await CopyAsync(sourceVideo, temporary, cancellationToken);
        File.Move(temporary, output, false);
        var manifest = new DeliveryPackageManifest
        {
            ProviderId = "local-folder",
            SourcePath = Path.GetFullPath(sourceVideo),
            DeliveredPath = output,
            Sha256 = await Sha256Async(output, cancellationToken),
            FileSizeBytes = new FileInfo(output).Length
        };
        await File.WriteAllTextAsync(
            output + ".delivery.json",
            JsonSerializer.Serialize(manifest, JsonDefaults.Options),
            cancellationToken);
        return manifest;
    }

    public async Task<string> CreateSocialOutboxAsync(
        string sourceVideo,
        string outboxRoot,
        SocialPublishMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(sourceVideo)) throw new FileNotFoundException("ไม่พบวิดีโอสำหรับ Publishing Package", sourceVideo);
        if (string.IsNullOrWhiteSpace(metadata.Platform)) throw new InvalidDataException("ต้องระบุ Platform");
        if (string.IsNullOrWhiteSpace(metadata.Title)) throw new InvalidDataException("ต้องระบุชื่อโพสต์");
        var platform = VersionedPathService.SanitizeFileName(metadata.Platform.ToLowerInvariant());
        var packageDirectory = Path.Combine(
            Path.GetFullPath(outboxRoot), platform,
            DateTime.UtcNow.ToString("yyyyMMdd_HHmmss") + "_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(packageDirectory);
        var videoPath = Path.Combine(packageDirectory, Path.GetFileName(sourceVideo));
        await CopyAsync(sourceVideo, videoPath, cancellationToken);
        var document = new
        {
            schema = "autocut.social-package.v1",
            metadata,
            video_file = Path.GetFileName(videoPath),
            video_sha256 = await Sha256Async(videoPath, cancellationToken),
            direct_publish_attempted = false,
            status = "ready_for_review_or_provider_plugin",
            created_at = DateTimeOffset.UtcNow
        };
        await File.WriteAllTextAsync(
            Path.Combine(packageDirectory, "publish.json"),
            JsonSerializer.Serialize(document, JsonDefaults.Options),
            cancellationToken);
        return packageDirectory;
    }

    private static async Task CopyAsync(string source, string destination, CancellationToken cancellationToken)
    {
        await using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, true);
        await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1024 * 1024, true);
        await input.CopyToAsync(output, cancellationToken);
        await output.FlushAsync(cancellationToken);
    }

    private static string NextAvailablePath(string directory, string fileName)
    {
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        var candidate = Path.Combine(directory, fileName);
        for (var index = 2; File.Exists(candidate); index++)
            candidate = Path.Combine(directory, $"{stem}_v{index}{extension}");
        return candidate;
    }

    private static async Task<string> Sha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken)).ToLowerInvariant();
    }

    private static void TryDelete(string path) { try { if (File.Exists(path)) File.Delete(path); } catch { } }
}
