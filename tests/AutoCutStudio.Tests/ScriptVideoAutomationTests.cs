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
}
