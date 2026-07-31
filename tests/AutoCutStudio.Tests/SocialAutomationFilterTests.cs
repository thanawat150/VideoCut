using AutoCutStudio.Core.Models;
using AutoCutStudio.Infrastructure;

namespace AutoCutStudio.Tests;

public sealed class SocialAutomationFilterTests
{
    [Fact]
    public void SocialRenderAppliesCaptionPlatformAndEnhancementFiltersTogether()
    {
        var job = new JobDocument
        {
            JobId = Guid.NewGuid(),
            ProjectId = Guid.NewGuid(),
            ProjectRoot = Path.GetTempPath(),
            JobType = JobTypes.SocialClipExport,
            ExpectedInputHasAudio = true,
            ExpectedDurationSeconds = 12,
            Segments =
            [
                new TimelineSegment { StartSeconds = 3, EndSeconds = 15 }
            ],
            RenderRecipe = new RenderRecipe
            {
                OutputWidth = 1080,
                OutputHeight = 1920,
                OutputFrameRate = 30,
                AspectStrategy = "center_crop",
                BurnCaptions = true,
                CaptionAssPath = Path.Combine(Path.GetTempPath(), "captions.ass"),
                Stabilize = true,
                ColorPreset = ColorPresets.Natural,
                AudioEnhancementPreset = AudioEnhancementPresets.NoiseAndVoice
            }
        };

        var arguments = FfmpegSocialProcessor.BuildArguments(job, "input.mp4", "output.mp4").ToList();
        var videoFilter = arguments[arguments.IndexOf("-vf") + 1];
        var audioFilter = arguments[arguments.IndexOf("-af") + 1];

        Assert.Contains("crop=1080:1920", videoFilter);
        Assert.Contains("deshake", videoFilter);
        Assert.Contains("eq=contrast", videoFilter);
        Assert.Contains("ass=filename='captions.ass'", videoFilter);
        Assert.Contains("afftdn", audioFilter);
        Assert.Contains("dynaudnorm", audioFilter);
    }

    [Fact]
    public void SocialRenderWithBrollMapsEnhancedAudioFromFilterComplex()
    {
        var temp = Path.GetTempPath();
        var job = new JobDocument
        {
            JobId = Guid.NewGuid(),
            ProjectId = Guid.NewGuid(),
            ProjectRoot = temp,
            JobType = JobTypes.SocialClipExport,
            ExpectedInputHasAudio = true,
            ExpectedDurationSeconds = 10,
            Segments =
            [
                new TimelineSegment { StartSeconds = 0, EndSeconds = 10 }
            ],
            RenderRecipe = new RenderRecipe
            {
                OutputWidth = 1080,
                OutputHeight = 1920,
                AspectStrategy = "center_crop",
                AudioEnhancementPreset = AudioEnhancementPresets.VoiceEnhance,
                BrollOverlays =
                [
                    new BrollOverlayRecipe
                    {
                        AssetPath = Path.Combine(temp, "broll.jpg"),
                        StartSeconds = 1,
                        EndSeconds = 4,
                        Confidence = 0.9
                    }
                ]
            }
        };

        var arguments = FfmpegSocialProcessor.BuildArguments(job, "input.mp4", "output.mp4").ToList();
        var complex = arguments[arguments.IndexOf("-filter_complex") + 1];

        Assert.Contains("[0:a:0]highpass", complex);
        Assert.Contains("[enhanced_audio]", complex);
        Assert.Contains("[enhanced_audio]", arguments);
    }
}
