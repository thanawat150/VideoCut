using AutoCutStudio;
using Xunit;

namespace AutoCutStudio.Tests;

public sealed class CoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "AutoCutStudioTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task CreateProject_CreatesRequiredFoldersAndJson()
    {
        var service = new ProjectService(new FakeProbe());
        var created = await service.CreateAsync(_root, "งานทดสอบ ภาษาไทย");

        Assert.True(File.Exists(Path.Combine(created.ProjectDirectory, "project.json")));
        foreach (var folder in new[] { "source", "proxy", "cache", "exports", "reports", "versions" })
            Assert.True(Directory.Exists(Path.Combine(created.ProjectDirectory, folder)), folder);
        Assert.Equal("งานทดสอบ ภาษาไทย", created.Project.Name);
    }

    [Fact]
    public async Task ImportMedia_CopiesFileAndKeepsOriginalUntouched()
    {
        Directory.CreateDirectory(_root);
        var original = Path.Combine(_root, "คลิป ทดสอบ.mp4");
        var bytes = new byte[] { 1, 2, 3, 4, 5 };
        await File.WriteAllBytesAsync(original, bytes);

        var service = new ProjectService(new FakeProbe());
        var created = await service.CreateAsync(_root, "Project");
        var item = await service.ImportMediaAsync(created.Project, created.ProjectDirectory, original);

        Assert.Equal(bytes, await File.ReadAllBytesAsync(original));
        Assert.Equal(bytes, await File.ReadAllBytesAsync(Path.Combine(created.ProjectDirectory, item.ProjectPath)));
        Assert.Single(created.Project.Tracks[0].Clips);
    }

    [Fact]
    public async Task SaveProject_CreatesPreviousVersion()
    {
        var service = new ProjectService(new FakeProbe());
        var created = await service.CreateAsync(_root, "Versioned");
        created.Project.Name = "Changed";
        await service.SaveAsync(created.Project, created.ProjectDirectory);

        Assert.NotEmpty(Directory.EnumerateFiles(Path.Combine(created.ProjectDirectory, "versions"), "project-*.json"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    private sealed class FakeProbe : FfprobeService
    {
        public override Task<MediaProbeResult> ProbeAsync(string filePath, CancellationToken cancellationToken = default) =>
            Task.FromResult(new MediaProbeResult
            {
                VideoCodec = "h264", AudioCodec = "aac", Width = 1920, Height = 1080,
                FrameRate = 30, Duration = TimeSpan.FromSeconds(5)
            });
    }
}
