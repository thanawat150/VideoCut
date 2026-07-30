using AutoCutStudio.Core.Models;
using AutoCutStudio.Core.Services;

namespace AutoCutStudio.Tests;

public sealed class CoreTests
{
    [Fact]
    public void EventBusPublishesTheExactBackendEvent()
    {
        var bus = new AgentEventBus();
        AgentEvent? received = null;
        bus.Published += (_, value) => received = value;

        var emitted = new AgentEvent
        {
            EventType = "ffmpeg.progress",
            ProjectId = Guid.NewGuid(),
            JobId = Guid.NewGuid(),
            AgentId = AgentIds.Render,
            Status = AgentStatuses.Rendering,
            Progress = 42.5,
            Message = "real progress"
        };

        bus.Publish(emitted);

        Assert.Same(emitted, received);
        Assert.Equal(42.5, received!.Progress);
    }

    [Fact]
    public void VersionedPathNeverReturnsAnExistingOutput()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            File.WriteAllText(Path.Combine(root, "video.mp4"), "existing");
            File.WriteAllText(Path.Combine(root, "video_v2.mp4"), "existing");

            var result = VersionedPathService.GetNextAvailablePath(root, "video", ".mp4");

            Assert.Equal(Path.Combine(root, "video_v3.mp4"), result);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void PathTraversalOutsideProjectIsRejected()
    {
        var root = CreateTemporaryDirectory();
        var outside = Path.Combine(Path.GetDirectoryName(root)!, "outside.mp4");

        try
        {
            Assert.Throws<UnauthorizedAccessException>(() =>
                PathSecurity.EnsureUnderRoot(outside, root));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "AutoCut Core " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
