using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using AutoCutStudio.Core.Models;
using AutoCutStudio.Core.Services;
using AutoCutStudio.Infrastructure;

namespace AutoCutStudio.Tests;

public sealed class ProfessionalToolsTests
{
    [Fact]
    public async Task DeclarativePluginCatalogAcceptsSafePresetAndRejectsCodePlugin()
    {
        var root = TempRoot("Plugins");
        try
        {
            var project = new ProjectDocument { ProjectId = Guid.NewGuid(), DisplayName = "Plugin Test", RootPath = root };
            var validDirectory = Path.Combine(root, "plugins", "official.vertical");
            var invalidDirectory = Path.Combine(root, "plugins", "unsafe.dll");
            Directory.CreateDirectory(validDirectory);
            Directory.CreateDirectory(invalidDirectory);
            await File.WriteAllTextAsync(Path.Combine(validDirectory, "plugin.json"), """
            {
              "id": "official.vertical",
              "name": "Official Vertical",
              "version": "1.0.0",
              "type": "export_preset",
              "description": "Safe declarative preset",
              "exportPreset": {
                "width": 1080,
                "height": 1920,
                "frameRate": 30,
                "videoBitrateKbps": 8000,
                "audioBitrateKbps": 192
              }
            }
            """);
            await File.WriteAllTextAsync(Path.Combine(invalidDirectory, "plugin.json"), """
            {
              "id": "unsafe.dll",
              "name": "Unsafe Code Plugin",
              "version": "1.0.0",
              "type": "dotnet_assembly"
            }
            """);

            var plugins = await new PluginCatalogService().DiscoverAsync(project);

            Assert.Contains(plugins, item => item.Id == "official.vertical" && item.IsValid);
            Assert.Contains(plugins, item => item.Id == "unsafe.dll" && !item.IsValid && item.ValidationMessage.Contains("declarative"));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task NestedSequenceRepositoryRoundTripsThaiPaths()
    {
        var root = TempRoot("Nested ภาษาไทย");
        try
        {
            var project = new ProjectDocument { ProjectId = Guid.NewGuid(), DisplayName = "Nested", RootPath = root };
            var source = Path.Combine(root, "วิดีโอ มีช่องว่าง.mp4");
            await File.WriteAllBytesAsync(source, [0, 1, 2]);
            var document = new NestedSequenceDocument
            {
                ProjectId = project.ProjectId,
                Name = "ลำดับซ้อนทดสอบ",
                Clips =
                [
                    new NestedSequenceClip
                    {
                        Sequence = 1,
                        SourcePath = source,
                        SourceStartSeconds = 1,
                        SourceEndSeconds = 3,
                        HasAudio = true
                    }
                ]
            };
            var repository = new NestedSequenceRepository();

            await repository.SaveAsync(project, document);
            var loaded = await repository.ListAsync(project);

            var item = Assert.Single(loaded);
            Assert.Equal(document.SequenceId, item.SequenceId);
            Assert.Equal("ลำดับซ้อนทดสอบ", item.Name);
            Assert.Equal(source, item.Clips[0].SourcePath);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task DeliveryCopiesRealFileAndCreatesReviewGatedSocialPackage()
    {
        var root = TempRoot("Delivery ภาษาไทย");
        try
        {
            var source = Path.Combine(root, "output video.mp4");
            await File.WriteAllBytesAsync(source, Enumerable.Range(0, 4096).Select(index => (byte)(index % 251)).ToArray());
            var destination = Path.Combine(root, "Cloud Sync");
            var service = new DeliveryPackageService();

            var manifest = await service.DeliverToFolderAsync(source, destination);
            var outbox = await service.CreateSocialOutboxAsync(source, destination, new SocialPublishMetadata
            {
                Platform = "youtube",
                Title = "ทดสอบส่งออก",
                Privacy = "private"
            });

            Assert.True(File.Exists(manifest.DeliveredPath));
            Assert.True(File.Exists(manifest.DeliveredPath + ".delivery.json"));
            Assert.Equal(await HashAsync(source), manifest.Sha256);
            var publishPath = Path.Combine(outbox, "publish.json");
            Assert.True(File.Exists(publishPath));
            using var publish = JsonDocument.Parse(await File.ReadAllTextAsync(publishPath));
            Assert.False(publish.RootElement.GetProperty("direct_publish_attempted").GetBoolean());
            Assert.Equal("ready_for_review_or_provider_plugin", publish.RootElement.GetProperty("status").GetString());
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task CollaborationPackageVerifiesChecksumsAndExcludesSourceByDefault()
    {
        var root = TempRoot("Collaboration");
        var importParent = TempRoot("Imported");
        try
        {
            foreach (var directory in new[] { "transcript", "reports", "jobs" })
                Directory.CreateDirectory(Path.Combine(root, directory));
            await File.WriteAllTextAsync(Path.Combine(root, "project.json"), "{\"projectId\":\"00000000-0000-0000-0000-000000000001\"}");
            await File.WriteAllTextAsync(Path.Combine(root, "transcript", "editable.json"), "{\"text\":\"ภาษาไทย\"}");
            await File.WriteAllTextAsync(Path.Combine(root, "reports", "qa.json"), "{\"passed\":true}");
            var source = Path.Combine(root, "private-source.mp4");
            await File.WriteAllBytesAsync(source, new byte[1024]);
            var project = new ProjectDocument
            {
                ProjectId = Guid.NewGuid(),
                DisplayName = "Team Project",
                RootPath = root,
                SourceMedia = [new MediaAsset { SourcePath = source }]
            };
            var package = Path.Combine(importParent, "team package.zip");
            var service = new CollaborationPackageService();

            var exported = await service.ExportAsync(project, package, includeSourceMedia: false);
            var destination = Path.Combine(importParent, "verified project");
            var imported = await service.ImportAsync(package, destination);

            Assert.False(exported.IncludesSourceMedia);
            Assert.DoesNotContain(exported.Entries, item => item.RelativePath.Contains("private-source", StringComparison.OrdinalIgnoreCase));
            Assert.Equal(exported.Entries.Count, imported.Entries.Count);
            Assert.True(File.Exists(Path.Combine(destination, "transcript", "editable.json")));
            using var archive = ZipFile.OpenRead(package);
            Assert.Null(archive.GetEntry("external-media/private-source.mp4"));
        }
        finally
        {
            Directory.Delete(root, true);
            Directory.Delete(importParent, true);
        }
    }

    [Fact]
    public async Task RealMulticamKeyframeAndNestedRenderingPreserveAllSources()
    {
        var tools = new ToolLocator();
        var availability = tools.Locate();
        Assert.True(availability.IsReady, availability.Message);
        var root = TempRoot("Professional Render ภาษาไทย");
        try
        {
            var first = Path.Combine(root, "กล้อง 1.mp4");
            var second = Path.Combine(root, "กล้อง 2.mp4");
            await GenerateVideoAsync(availability.FfmpegPath!, first, "red", 440);
            await GenerateVideoAsync(availability.FfmpegPath!, second, "blue", 660);
            var firstHash = await HashAsync(first);
            var secondHash = await HashAsync(second);
            var probe = new FfprobeMediaProbe(tools);
            var firstMeta = await probe.ProbeAsync(first);
            var project = new ProjectDocument { ProjectId = Guid.NewGuid(), DisplayName = "Professional", RootPath = root };
            var repository = new JobRepository();
            var factory = new ProfessionalJobFactory(repository);

            var angle1 = new CameraAngle { DisplayName = "Camera 1", SourcePath = first, HasAudio = true };
            var angle2 = new CameraAngle { DisplayName = "Camera 2", SourcePath = second, HasAudio = true };
            var multicamJob = await factory.CreateMulticamAsync(project, new MulticamRenderRecipe
            {
                Angles = [angle1, angle2],
                Switches =
                [
                    new MulticamSwitch { Sequence = 1, AngleId = angle1.AngleId, SourceStartSeconds = 0, SourceEndSeconds = 1.5 },
                    new MulticamSwitch { Sequence = 2, AngleId = angle2.AngleId, SourceStartSeconds = 1.5, SourceEndSeconds = 3 }
                ],
                OutputWidth = 320,
                OutputHeight = 180,
                FrameRate = 25
            });
            await RenderAndVerifyAsync(tools, multicamJob, 320, 180, 2.7, 3.3);

            var media = new MediaAsset { SourcePath = first, Metadata = firstMeta };
            var keyframeJob = await factory.CreateKeyframeAsync(project, media, new KeyframeRenderRecipe
            {
                OutputWidth = 320,
                OutputHeight = 180,
                FrameRate = 25,
                Keyframes =
                [
                    new MotionKeyframe { Sequence = 1, TimeSeconds = 0, Zoom = 1, FocusX = 0.5, FocusY = 0.5, AudioGain = 1 },
                    new MotionKeyframe { Sequence = 2, TimeSeconds = 1.5, Zoom = 1.4, FocusX = 0.35, FocusY = 0.5, AudioGain = 0.8 },
                    new MotionKeyframe { Sequence = 3, TimeSeconds = 3, Zoom = 1, FocusX = 0.5, FocusY = 0.5, AudioGain = 1 }
                ]
            });
            await RenderAndVerifyAsync(tools, keyframeJob, 320, 180, 2.7, 3.3);

            var nestedJob = await factory.CreateNestedSequenceAsync(project, new NestedSequenceRenderRecipe
            {
                SequenceId = Guid.NewGuid(),
                Name = "Nested ภาษาไทย",
                OutputWidth = 320,
                OutputHeight = 180,
                FrameRate = 25,
                Clips =
                [
                    new NestedSequenceClip { Sequence = 1, SourcePath = second, SourceStartSeconds = 0.5, SourceEndSeconds = 1.5, HasAudio = true },
                    new NestedSequenceClip { Sequence = 2, SourcePath = first, SourceStartSeconds = 1.5, SourceEndSeconds = 3, HasAudio = true }
                ]
            });
            await RenderAndVerifyAsync(tools, nestedJob, 320, 180, 2.2, 2.8);

            Assert.Equal(firstHash, await HashAsync(first));
            Assert.Equal(secondHash, await HashAsync(second));
        }
        finally { Directory.Delete(root, true); }
    }

    private static async Task RenderAndVerifyAsync(
        ToolLocator tools,
        JobDocument job,
        int width,
        int height,
        double minimumDuration,
        double maximumDuration)
    {
        JobProgress? progress = null;
        var report = await new FfmpegProfessionalProcessor(tools).ProcessAsync(
            job,
            _ => Task.CompletedTask,
            item => { progress = item; return Task.CompletedTask; });
        var metadata = await new FfprobeMediaProbe(tools).ProbeAsync(job.OutputPath);
        Assert.False(report.SourceWasModified);
        Assert.Equal(width, metadata.Width);
        Assert.Equal(height, metadata.Height);
        Assert.True(metadata.HasVideo);
        Assert.True(metadata.HasAudio);
        Assert.InRange(metadata.DurationSeconds, minimumDuration, maximumDuration);
        Assert.Equal(100, progress?.Progress);
        Assert.DoesNotContain(job.InputPath, report.SafeCommandDisplay, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task GenerateVideoAsync(string ffmpeg, string output, string color, int frequency)
    {
        await RunFfmpegAsync(ffmpeg,
            "-hide_banner", "-y",
            "-f", "lavfi", "-i", $"color=c={color}:size=320x180:rate=25:duration=4",
            "-f", "lavfi", "-i", $"sine=frequency={frequency}:sample_rate=48000:duration=4",
            "-c:v", "libx264", "-pix_fmt", "yuv420p", "-c:a", "aac", "-shortest", output);
    }

    private static async Task RunFfmpegAsync(string ffmpeg, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = ffmpeg,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Unable to start FFmpeg.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        _ = await stdout;
        Assert.True(process.ExitCode == 0, await stderr);
    }

    private static string TempRoot(string name)
    {
        var path = Path.Combine(Path.GetTempPath(), "AutoCut " + name + " " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static async Task<string> HashAsync(string path)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream)).ToLowerInvariant();
    }
}
