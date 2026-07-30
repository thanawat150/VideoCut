using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using AutoCutStudio.Core.Models;
using AutoCutStudio.Core.Services;

namespace AutoCutStudio.Infrastructure;

public sealed class CollaborationPackageService
{
    private static readonly string[] SharedDirectories =
    [
        "transcript", "subtitles", "reports", "templates", "sequences", "plugins", "thumbnails"
    ];

    public async Task<CollaborationPackageManifest> ExportAsync(
        ProjectDocument project,
        string outputZipPath,
        bool includeSourceMedia = false,
        CancellationToken cancellationToken = default)
    {
        var root = Path.GetFullPath(project.RootPath);
        var output = Path.GetFullPath(outputZipPath);
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        if (File.Exists(output)) throw new IOException("Collaboration Package มีอยู่แล้วและจะไม่ถูกเขียนทับ");
        var temporary = output + ".partial";
        TryDelete(temporary);

        var files = new List<string>();
        var projectFile = Path.Combine(root, "project.json");
        if (File.Exists(projectFile)) files.Add(projectFile);
        foreach (var directoryName in SharedDirectories)
        {
            var directory = Path.Combine(root, directoryName);
            if (!Directory.Exists(directory)) continue;
            files.AddRange(Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories));
        }
        var jobs = Path.Combine(root, "jobs");
        if (Directory.Exists(jobs))
        {
            files.AddRange(Directory.EnumerateFiles(jobs, "*", SearchOption.AllDirectories)
                .Where(path => new[] { ".json", ".jsonl", ".log", ".txt" }.Contains(Path.GetExtension(path).ToLowerInvariant())));
        }
        if (includeSourceMedia)
        {
            foreach (var media in project.SourceMedia)
            {
                if (File.Exists(media.SourcePath)) files.Add(media.SourcePath);
            }
        }

        var entries = new List<CollaborationPackageEntry>();
        using (var archive = ZipFile.Open(temporary, ZipArchiveMode.Create))
        {
            foreach (var path in files.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var relative = path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                    ? Path.GetRelativePath(root, path).Replace('\\', '/')
                    : "external-media/" + VersionedPathService.SanitizeFileName(Path.GetFileName(path));
                if (relative.Equals("collaboration-manifest.json", StringComparison.OrdinalIgnoreCase)) continue;
                var entry = archive.CreateEntry(relative, CompressionLevel.Optimal);
                await using (var input = File.OpenRead(path))
                await using (var destination = entry.Open())
                    await input.CopyToAsync(destination, cancellationToken);
                entries.Add(new CollaborationPackageEntry
                {
                    RelativePath = relative,
                    Sha256 = await Sha256Async(path, cancellationToken),
                    SizeBytes = new FileInfo(path).Length
                });
            }

            var manifest = new CollaborationPackageManifest
            {
                ProjectId = project.ProjectId,
                ProjectName = project.DisplayName,
                IncludesSourceMedia = includeSourceMedia,
                Entries = entries.OrderBy(item => item.RelativePath, StringComparer.OrdinalIgnoreCase).ToList()
            };
            var manifestEntry = archive.CreateEntry("collaboration-manifest.json", CompressionLevel.Optimal);
            await using var writer = new StreamWriter(manifestEntry.Open());
            await writer.WriteAsync(JsonSerializer.Serialize(manifest, JsonDefaults.Options));
        }
        File.Move(temporary, output, false);
        return new CollaborationPackageManifest
        {
            ProjectId = project.ProjectId,
            ProjectName = project.DisplayName,
            IncludesSourceMedia = includeSourceMedia,
            Entries = entries.OrderBy(item => item.RelativePath, StringComparer.OrdinalIgnoreCase).ToList()
        };
    }

    public async Task<CollaborationPackageManifest> ImportAsync(
        string packagePath,
        string destinationDirectory,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(packagePath)) throw new FileNotFoundException("ไม่พบ Collaboration Package", packagePath);
        var destination = Path.GetFullPath(destinationDirectory);
        if (Directory.Exists(destination) && Directory.EnumerateFileSystemEntries(destination).Any())
            throw new IOException("โฟลเดอร์ปลายทางต้องว่างเพื่อป้องกันการเขียนทับงานเดิม");
        Directory.CreateDirectory(destination);

        using var archive = ZipFile.OpenRead(packagePath);
        var manifestEntry = archive.GetEntry("collaboration-manifest.json")
                            ?? throw new InvalidDataException("Package ไม่มี collaboration-manifest.json");
        CollaborationPackageManifest manifest;
        await using (var stream = manifestEntry.Open())
        {
            manifest = await JsonSerializer.DeserializeAsync<CollaborationPackageManifest>(stream, JsonDefaults.Options, cancellationToken)
                       ?? throw new InvalidDataException("อ่าน Collaboration Manifest ไม่สำเร็จ");
        }
        var manifestMap = manifest.Entries.ToDictionary(item => item.RelativePath, StringComparer.OrdinalIgnoreCase);
        foreach (var entry in archive.Entries.Where(item => !item.FullName.Equals("collaboration-manifest.json", StringComparison.OrdinalIgnoreCase)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(entry.Name)) continue;
            var normalized = entry.FullName.Replace('/', Path.DirectorySeparatorChar);
            var output = Path.GetFullPath(Path.Combine(destination, normalized));
            if (!output.StartsWith(destination + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Package มี Path ที่ไม่ปลอดภัย");
            if (!manifestMap.TryGetValue(entry.FullName, out var expected))
                throw new InvalidDataException($"ไฟล์ไม่มีใน Manifest: {entry.FullName}");
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            await using (var source = entry.Open())
            await using (var target = new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, true))
                await source.CopyToAsync(target, cancellationToken);
            var actualHash = await Sha256Async(output, cancellationToken);
            if (!actualHash.Equals(expected.Sha256, StringComparison.OrdinalIgnoreCase) || new FileInfo(output).Length != expected.SizeBytes)
                throw new InvalidDataException($"Checksum ไม่ตรง: {entry.FullName}");
        }
        return manifest;
    }

    private static async Task<string> Sha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken)).ToLowerInvariant();
    }

    private static void TryDelete(string path) { try { if (File.Exists(path)) File.Delete(path); } catch { } }
}
