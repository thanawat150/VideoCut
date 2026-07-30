using AutoCutStudio.Core.Models;
using AutoCutStudio.Infrastructure;

namespace AutoCutStudio.Tests;

public sealed class ProjectRepositoryTests
{
    [Fact]
    public async Task ProjectRoundTripSupportsThaiPathAndSpaces()
    {
        var parent = Path.Combine(
            Path.GetTempPath(),
            "AutoCut Studio ทดสอบ " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(parent);

        try
        {
            var repository = new ProjectRepository();
            var project = await repository.CreateAsync(parent, "โครงการ วิดีโอ");
            var projectPath = Path.Combine(project.RootPath, "project.json");

            var updated = project with
            {
                SourceMedia =
                [
                    new MediaAsset
                    {
                        SourcePath = @"C:\สื่อ ทดสอบ\video.mp4",
                        Metadata = new MediaMetadata
                        {
                            SourcePath = @"C:\สื่อ ทดสอบ\video.mp4",
                            HasVideo = true,
                            DurationSeconds = 10
                        }
                    }
                ]
            };
            await repository.SaveAsync(updated);

            var reopened = await repository.OpenAsync(projectPath);

            Assert.Equal(project.ProjectId, reopened.ProjectId);
            Assert.Equal("โครงการ วิดีโอ", reopened.DisplayName);
            Assert.Single(reopened.SourceMedia);
            Assert.True(File.Exists(projectPath));
        }
        finally
        {
            Directory.Delete(parent, true);
        }
    }
}
