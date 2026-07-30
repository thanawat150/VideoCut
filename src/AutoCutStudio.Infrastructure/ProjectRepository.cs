using AutoCutStudio.Core.Interfaces;
using AutoCutStudio.Core.Models;
using AutoCutStudio.Core.Services;

namespace AutoCutStudio.Infrastructure;

public sealed class ProjectRepository : IProjectRepository
{
    private static readonly string[] Directories =
    [
        "source", "proxy", "audio", "images", "transcript", "subtitles", "assets", "music",
        "templates", "cache", "drafts", "exports", "thumbnails", "reports", "jobs", "logs", "backups"
    ];

    public async Task<ProjectDocument> CreateAsync(
        string parentDirectory,
        string displayName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parentDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        var parent = Path.GetFullPath(parentDirectory);
        Directory.CreateDirectory(parent);

        var folderName = VersionedPathService.SanitizeFileName(displayName);
        var root = Path.Combine(parent, folderName);
        if (Directory.Exists(root) && Directory.EnumerateFileSystemEntries(root).Any())
        {
            root = Path.Combine(parent, $"{folderName}_{DateTime.Now:yyyyMMdd_HHmmss}");
        }

        Directory.CreateDirectory(root);
        foreach (var directory in Directories)
        {
            Directory.CreateDirectory(Path.Combine(root, directory));
        }

        var project = new ProjectDocument
        {
            ProjectId = Guid.NewGuid(),
            DisplayName = displayName.Trim(),
            RootPath = root,
            CreatedAt = DateTimeOffset.UtcNow,
            ModifiedAt = DateTimeOffset.UtcNow,
            ApplicationVersion = "0.1.0"
        };

        await SaveAsync(project, cancellationToken);
        return project;
    }

    public Task<ProjectDocument> OpenAsync(
        string projectFilePath,
        CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(projectFilePath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("Project file was not found.", fullPath);
        }

        return AtomicJsonFile.ReadAsync<ProjectDocument>(fullPath, cancellationToken);
    }

    public async Task SaveAsync(ProjectDocument project, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        var root = Path.GetFullPath(project.RootPath);
        Directory.CreateDirectory(root);

        var projectPath = PathSecurity.EnsureUnderRoot(Path.Combine(root, "project.json"), root);
        if (File.Exists(projectPath))
        {
            var backupDirectory = Path.Combine(root, "backups");
            Directory.CreateDirectory(backupDirectory);
            var backupPath = Path.Combine(
                backupDirectory,
                $"project_{DateTime.UtcNow:yyyyMMdd_HHmmss_fff}.json");
            File.Copy(projectPath, backupPath, false);
            TrimBackups(backupDirectory, 20);
        }

        await AtomicJsonFile.WriteAsync(projectPath, project with
        {
            ModifiedAt = DateTimeOffset.UtcNow
        }, cancellationToken);
    }

    private static void TrimBackups(string backupDirectory, int maximum)
    {
        var backups = new DirectoryInfo(backupDirectory)
            .EnumerateFiles("project_*.json")
            .OrderByDescending(file => file.CreationTimeUtc)
            .Skip(maximum)
            .ToList();

        foreach (var backup in backups)
        {
            backup.Delete();
        }
    }
}
