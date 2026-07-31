using System.Text.Json;
using AutoCutStudio.Core.Models;
using AutoCutStudio.Core.Services;
using AutoCutStudio.Infrastructure;

namespace AutoCutStudio.Tests;

public sealed class ScriptVideoAutomationTests
{
    [Fact]
    public void Planner_splits_script_and_matches_project_assets()
    {
        var root = Path.Combine(Path.GetTempPath(), "autocut-script-plan-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllBytes(Path.Combine(root, "ป่าชายเลน.jpg"), [1, 2, 3]);
            File.WriteAllBytes(Path.Combine(root, "carbon.png"), [4, 5, 6]);
            var script = "ป่าชายเลนเป็นระบบนิเวศที่สำคัญ ช่วยป้องกันชายฝั่งและเป็นแหล่งอนุบาลสัตว์น้ำ\n\n" +
                         "Mangrove forests can store carbon and support climate action.";

            var plan = new ScriptVideoPlanner().Plan(
                script,
                root,
                new ScriptVideoPlanningOptions
                {
                    MaximumSceneCharacters = 120,
                    UseSequentialAssetFallback = true
                },
                "Mangrove Story");

            Assert.Equal("Mangrove Story", plan.Title);
            Assert.True(plan.Scenes.Count >= 2);
            Assert.All(plan.Scenes, scene => Assert.True(scene.DurationSeconds >= 2));
            Assert.Contains(plan.Scenes, scene => scene.VisualAssetPath is not null);
            Assert.Equal(plan.Scenes.Count, plan.ToDocumentSections().Count);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Pronunciation_dictionary_replaces_terms_without_changing_other_text()
    {
        var service = new ScriptVideoPlanner();
        var output = service.ApplyPronunciationDictionary(
            "T-VER ใช้ GIS สำหรับ MRV",
            "T-VER=ที เวอร์\nGIS=จี ไอ เอส\nMRV=เอ็ม อาร์ วี");

        Assert.Equal("ที เวอร์ ใช้ จี ไอ เอส สำหรับ เอ็ม อาร์ วี", output);
    }

    [Fact]
    public async Task Template_processor_builds_overlay_graph_for_staged_scene_visuals()
    {
        var root = Path.Combine(Path.GetTempPath(), "autocut-script-render-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var scriptPath = Path.Combine(root, "document-script.json");
            var assPath = Path.Combine(root, "slides.ass");
            var scenePath = Path.Combine(root, "scene-000.png");
            await File.WriteAllBytesAsync(scenePath, [1, 2, 3]);
            await File.WriteAllTextAsync(assPath, "[Script Info]");
            var recipe = new TemplateVideoRecipe
            {
                Title = "Test",
                Width = 1080,
                Height = 1920,
                FrameRate = 30,
                ThemeId = "cinematic_visual",
                Sections =
                [
                    new DocumentSection
                    {
                        Index = 0,
                        Heading = "Scene",
                        Body = "Narration",
                        SuggestedDurationSeconds = 5
                    }
                ]
            };
            await File.WriteAllTextAsync(scriptPath, JsonSerializer.Serialize(recipe, JsonDefaults.Options));
            var job = new JobDocument
            {
                ProjectRoot = root,
                ExpectedDurationSeconds = 5,
                TemplateVideoRecipe = new TemplateVideoJobRecipe
                {
                    Recipe = recipe,
                    ScriptPath = scriptPath,
                    AssPath = assPath
                }
            };

            var arguments = FfmpegTemplateVideoProcessor.BuildArguments(job, Path.Combine(root, "output.mp4"));
            var display = string.Join(" ", arguments);

            Assert.Contains("-filter_complex", arguments);
            Assert.Contains("overlay=0:0", display, StringComparison.Ordinal);
            Assert.Contains("ass=filename='slides.ass'", display, StringComparison.Ordinal);
            Assert.Contains("1080:1920", display, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task Real_script_video_renders_staged_visual_and_platform_dimensions()
    {
        var tools = new ToolLocator();
        var availability = tools.Locate();
        Assert.True(availability.IsReady, availability.Message);
        var root = Path.Combine(Path.GetTempPath(), "AutoCut Script Visual ภาษาไทย " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var imagePath = Path.Combine(root, "ป่าชายเลน.bmp");
            await File.WriteAllBytesAsync(imagePath, CreateSolidBmp(16, 16, 24, 120, 60));
            var project = new ProjectDocument
            {
                DisplayName = "Script Video Test",
                RootPath = root
            };
            var recipe = new TemplateVideoRecipe
            {
                Title = "ทดสอบภาพประกอบ",
                Width = 180,
                Height = 320,
                FrameRate = 15,
                ThemeId = "cinematic_visual",
                GenerateWindowsVoiceover = false,
                Sections =
                [
                    new DocumentSection
                    {
                        Index = 0,
                        Heading = "ป่าชายเลน",
                        Body = "วิดีโอจากสคริปต์พร้อมภาพประกอบ",
                        SuggestedDurationSeconds = 2.5
                    }
                ]
            };
            var repository = new JobRepository();
            var job = await new TemplateVideoJobFactory(repository).CreateScriptVideoAsync(
                project,
                recipe,
                new Dictionary<int, string> { [0] = imagePath },
                voiceoverOptions: null,
                pronunciationDictionary: null);
            var report = await new FfmpegTemplateVideoProcessor(tools).ProcessAsync(
                job,
                _ => Task.CompletedTask,
                _ => Task.CompletedTask);
            var metadata = await new FfprobeMediaProbe(tools).ProbeAsync(job.OutputPath);

            Assert.False(report.SourceWasModified);
            Assert.True(metadata.HasVideo);
            Assert.False(metadata.HasAudio);
            Assert.Equal(180, metadata.Width);
            Assert.Equal(320, metadata.Height);
            Assert.InRange(metadata.DurationSeconds, 2.2, 2.9);
            Assert.True(new FileInfo(job.OutputPath).Length > 1000);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static byte[] CreateSolidBmp(int width, int height, byte red, byte green, byte blue)
    {
        var rowSize = (width * 3 + 3) & ~3;
        var pixelBytes = rowSize * height;
        var fileSize = 54 + pixelBytes;
        var bytes = new byte[fileSize];
        bytes[0] = (byte)'B';
        bytes[1] = (byte)'M';
        BitConverter.GetBytes(fileSize).CopyTo(bytes, 2);
        BitConverter.GetBytes(54).CopyTo(bytes, 10);
        BitConverter.GetBytes(40).CopyTo(bytes, 14);
        BitConverter.GetBytes(width).CopyTo(bytes, 18);
        BitConverter.GetBytes(height).CopyTo(bytes, 22);
        BitConverter.GetBytes((short)1).CopyTo(bytes, 26);
        BitConverter.GetBytes((short)24).CopyTo(bytes, 28);
        BitConverter.GetBytes(pixelBytes).CopyTo(bytes, 34);
        for (var y = 0; y < height; y++)
        {
            var offset = 54 + y * rowSize;
            for (var x = 0; x < width; x++)
            {
                bytes[offset + x * 3] = blue;
                bytes[offset + x * 3 + 1] = green;
                bytes[offset + x * 3 + 2] = red;
            }
        }
        return bytes;
    }
}
