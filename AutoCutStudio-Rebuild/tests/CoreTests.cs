using AutoCutStudio.Rebuild;
using Xunit;

namespace AutoCutStudio.Rebuild.Tests;

public sealed class CoreTests
{
    [Fact]
    public void SplitCreatesTwoAdjacentClips()
    {
        var project = new ProjectDocument { DisplayName = "test", RootPath = Path.GetTempPath() };
        var media = new MediaAsset { SourcePath = "input.mp4", DisplayName = "input.mp4", Probe = new MediaProbe("mp4", "h264", "aac", 1920, 1080, 30, 10, true, true, 1, false) };
        project.Media.Add(media);
        var editor = new TimelineEditor();
        var left = editor.Add(project, media);
        var right = editor.Split(project, left.ClipId, 4);
        Assert.Equal(4, left.SourceOutSeconds);
        Assert.Equal(4, right.SourceInSeconds);
        Assert.Equal(2, project.Timeline.Count);
    }

    [Fact]
    public void ExistingOutputGetsVersionSuffix()
    {
        var root = Path.Combine(Path.GetTempPath(), "autocut-rebuild-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "output.mp4"), "x");
        Assert.EndsWith("output_v2.mp4", VersionedOutput.Next(root, "output", ".mp4"), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void OutputOutsideProjectIsRejected()
    {
        var project = Path.Combine(Path.GetTempPath(), "project-" + Guid.NewGuid().ToString("N"));
        Assert.Throws<UnauthorizedAccessException>(() => Security.OutputUnderProject(project, Path.Combine(Path.GetTempPath(), "outside.mp4")));
    }
}
