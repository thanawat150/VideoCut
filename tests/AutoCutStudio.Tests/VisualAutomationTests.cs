using AutoCutStudio.Core.Models;
using AutoCutStudio.Core.Services;
using AutoCutStudio.Infrastructure;

namespace AutoCutStudio.Tests;

public sealed class VisualAutomationTests
{
    [Fact]
    public void DefaultTemplatesAreValidDirectedAcyclicWorkflows()
    {
        var projectId = Guid.NewGuid();
        var templates = new WorkflowTemplateCatalog().CreateDefaults(projectId);
        var validator = new VisualWorkflowValidator();

        Assert.NotEmpty(templates);
        foreach (var workflow in templates)
        {
            var result = validator.Validate(workflow);
            Assert.True(result.IsValid, string.Join(Environment.NewLine, result.Errors));
            Assert.Equal(workflow.Nodes.Count, result.TopologicalOrder.Count);
            Assert.Equal(projectId, workflow.ProjectId);
        }
    }

    [Fact]
    public void ValidatorRejectsCycles()
    {
        var first = new VisualWorkflowNode { NodeType = VisualNodeTypes.MediaInput, DisplayName = "Input" };
        var second = new VisualWorkflowNode { NodeType = VisualNodeTypes.ExportVideo, DisplayName = "Export" };
        var workflow = new VisualWorkflowDocument
        {
            ProjectId = Guid.NewGuid(),
            Nodes = [first, second],
            Connections =
            [
                new VisualWorkflowConnection { SourceNodeId = first.NodeId, TargetNodeId = second.NodeId },
                new VisualWorkflowConnection { SourceNodeId = second.NodeId, TargetNodeId = first.NodeId }
            ]
        };

        var result = new VisualWorkflowValidator().Validate(workflow);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, item => item.Contains("วงจร", StringComparison.Ordinal));
    }

    [Fact]
    public void TikTokPresetReservesRightAndBottomUiSafeZones()
    {
        var preset = new PlatformPresetCatalog().Get("tiktok");

        Assert.Equal(1080, preset.Width);
        Assert.Equal(1920, preset.Height);
        Assert.True(preset.CaptionSafeZone.MarginRight > preset.CaptionSafeZone.MarginLeft);
        Assert.True(preset.CaptionSafeZone.MarginBottom >= 280);
        Assert.Equal(2, preset.CaptionSafeZone.MaximumLines);
        Assert.True(preset.CaptionSafeZone.MaximumWidthRatio <= 0.8);
    }

    [Fact]
    public void ThaiCaptionLayoutNeverExceedsConfiguredLineCount()
    {
        var preset = new PlatformPresetCatalog().Get("tiktok");
        var text = "การปลูกป่าชายเลนช่วยกักเก็บคาร์บอนและฟื้นฟูระบบนิเวศชายฝั่งพร้อมสร้างประโยชน์ให้ชุมชนอย่างยั่งยืน";

        var pages = new CaptionLayoutService().Layout(text, preset);

        Assert.NotEmpty(pages);
        Assert.All(pages, page =>
        {
            Assert.InRange(page.Lines.Count, 1, preset.CaptionSafeZone.MaximumLines);
            Assert.InRange(page.FontSize, preset.CaptionSafeZone.MinimumFontSize, preset.CaptionSafeZone.PreferredFontSize);
            Assert.All(page.Lines, line => Assert.False(string.IsNullOrWhiteSpace(line)));
        });
    }

    [Fact]
    public void AssCaptionsUseTikTokMarginsAndSplitLongThaiSegmentsIntoPages()
    {
        var preset = new PlatformPresetCatalog().Get("tiktok");
        var transcript = new TranscriptDocument
        {
            ProjectId = Guid.NewGuid(),
            MediaAssetId = Guid.NewGuid(),
            Language = "th",
            Segments =
            [
                new TranscriptSegment
                {
                    StartSeconds = 0,
                    EndSeconds = 8,
                    Text = "การปลูกป่าชายเลนช่วยกักเก็บคาร์บอนและฟื้นฟูระบบนิเวศชายฝั่งพร้อมสร้างประโยชน์ให้ชุมชนอย่างยั่งยืน"
                }
            ]
        };
        var candidate = new HighlightCandidate
        {
            Rank = 1,
            StartSeconds = 0,
            EndSeconds = 8,
            Score = 90
        };

        var ass = new AssCaptionBuilder().Build(transcript, candidate, preset, string.Empty, string.Empty);

        Assert.Contains($",{preset.CaptionSafeZone.MarginLeft},{preset.CaptionSafeZone.MarginRight},{preset.CaptionSafeZone.MarginBottom},1", ass);
        var dialogues = ass.Split('\n').Where(line => line.StartsWith("Dialogue: 1", StringComparison.Ordinal)).ToList();
        Assert.True(dialogues.Count >= 2);
        Assert.All(dialogues, line => Assert.True(line.Split("\\N", StringSplitOptions.None).Length <= 2));
    }

    [Fact]
    public void MultiClipProcessorBuildsRealConcatFilterForCut()
    {
        var job = new JobDocument
        {
            JobId = Guid.NewGuid(),
            ProjectId = Guid.NewGuid(),
            ProjectRoot = Path.GetTempPath(),
            JobType = JobTypes.MultiClipExport,
            ExpectedDurationSeconds = 5,
            RenderRecipe = new RenderRecipe
            {
                OutputWidth = 1080,
                OutputHeight = 1920,
                OutputFrameRate = 30,
                AspectStrategy = "center_crop"
            },
            MultiClipRecipe = new MultiClipRenderRecipe
            {
                Transition = "cut",
                Inputs =
                [
                    new MultiClipInput { SourcePath = "a.mp4", Order = 0, TrimEndSeconds = 2 },
                    new MultiClipInput { SourcePath = "b.mp4", Order = 1, TrimEndSeconds = 3 }
                ]
            }
        };
        var metadata = new[]
        {
            new MediaMetadata { HasVideo = true, HasAudio = true, DurationSeconds = 2 },
            new MediaMetadata { HasVideo = true, HasAudio = false, DurationSeconds = 3 }
        };

        var arguments = FfmpegMultiClipProcessor.BuildArguments(
            job,
            job.MultiClipRecipe.Inputs,
            metadata,
            "output.mp4").ToList();
        var filterIndex = arguments.IndexOf("-filter_complex");
        Assert.True(filterIndex >= 0);
        var filter = arguments[filterIndex + 1];

        Assert.Contains("concat=n=2:v=1:a=1", filter);
        Assert.Contains("anullsrc", filter);
        Assert.Contains("crop=1080:1920", filter);
    }
}
