using System.Text.Json;
using AutoCutStudio.Core.Models;
using AutoCutStudio.Core.Services;

namespace AutoCutStudio.Infrastructure;

public sealed class PluginCatalogService
{
    public async Task<IReadOnlyList<PluginManifest>> DiscoverAsync(
        ProjectDocument project,
        CancellationToken cancellationToken = default)
    {
        var roots = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "plugins"),
            Path.Combine(project.RootPath, "plugins")
        };
        var results = new List<PluginManifest>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(root)) continue;
            foreach (var path in Directory.EnumerateFiles(root, "plugin.json", SearchOption.AllDirectories).Take(200))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var info = new FileInfo(path);
                    if (info.Length is <= 0 or > 128 * 1024)
                        throw new InvalidDataException("Plugin manifest มีขนาดไม่ถูกต้อง");
                    await using var stream = File.OpenRead(path);
                    var manifest = await JsonSerializer.DeserializeAsync<PluginManifest>(stream, JsonDefaults.Options, cancellationToken)
                                   ?? throw new InvalidDataException("Plugin manifest ว่าง");
                    var validation = Validate(manifest);
                    if (!seen.Add(manifest.Id))
                        validation = (false, "Plugin ID ซ้ำ");
                    results.Add(manifest with
                    {
                        ManifestPath = Path.GetFullPath(path),
                        IsValid = validation.Valid,
                        ValidationMessage = validation.Message
                    });
                }
                catch (Exception exception)
                {
                    results.Add(new PluginManifest
                    {
                        Id = Path.GetFileName(Path.GetDirectoryName(path)) ?? "invalid",
                        Name = "Invalid plugin",
                        ManifestPath = path,
                        IsValid = false,
                        ValidationMessage = exception.Message
                    });
                }
            }
        }
        return results.OrderByDescending(item => item.IsValid).ThenBy(item => item.Name).ToList();
    }

    private static (bool Valid, string Message) Validate(PluginManifest manifest)
    {
        if (string.IsNullOrWhiteSpace(manifest.Id) ||
            manifest.Id.Length > 80 ||
            manifest.Id.Any(character => !(char.IsLetterOrDigit(character) || character is '-' or '_' or '.')))
            return (false, "Plugin ID ไม่ถูกต้อง");
        if (string.IsNullOrWhiteSpace(manifest.Name) || manifest.Name.Length > 120)
            return (false, "Plugin Name ไม่ถูกต้อง");
        if (!string.Equals(manifest.Type, "export_preset", StringComparison.OrdinalIgnoreCase))
            return (false, "รุ่นนี้รองรับ Plugin แบบ declarative export_preset เท่านั้น");
        if (manifest.ExportPreset is null)
            return (false, "Plugin ไม่มี Export Preset");
        var preset = manifest.ExportPreset;
        if (preset.Width is < 320 or > 3840 || preset.Height is < 240 or > 3840 ||
            preset.FrameRate is < 15 or > 60 || preset.VideoBitrateKbps is < 500 or > 100000 ||
            preset.AudioBitrateKbps is < 64 or > 512)
            return (false, "ค่า Export Preset อยู่นอกขอบเขตที่ปลอดภัย");
        return (true, "พร้อมใช้งาน — declarative only, ไม่รันโค้ดภายนอก");
    }
}
