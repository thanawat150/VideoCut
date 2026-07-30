using System.Diagnostics;
using AutoCutStudio.Core.Models;
using AutoCutStudio.Infrastructure;

namespace AutoCutStudio.App;

internal static class WorkerLauncher
{
    public static int Launch(JobDocument job)
    {
        var jobFile = JobRepository.GetJobFilePath(job);
        var executable = Path.Combine(AppContext.BaseDirectory, "AutoCutStudio.Worker.exe");
        var workerDll = Path.Combine(AppContext.BaseDirectory, "AutoCutStudio.Worker.dll");

        ProcessStartInfo startInfo;
        if (File.Exists(executable))
        {
            startInfo = new ProcessStartInfo
            {
                FileName = executable,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = AppContext.BaseDirectory
            };
        }
        else if (File.Exists(workerDll))
        {
            startInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = AppContext.BaseDirectory
            };
            startInfo.ArgumentList.Add(workerDll);
        }
        else
        {
            throw new FileNotFoundException(
                "ไม่พบ AutoCutStudio.Worker.exe หรือ AutoCutStudio.Worker.dll");
        }

        startInfo.ArgumentList.Add("--job");
        startInfo.ArgumentList.Add(jobFile);

        var process = Process.Start(startInfo)
                      ?? throw new InvalidOperationException("ไม่สามารถเริ่ม Worker Process ได้");
        return process.Id;
    }

    public static async Task WriteControlAsync(
        JobDocument job,
        string action,
        CancellationToken cancellationToken = default)
    {
        var jobDirectory = Path.Combine(job.ProjectRoot, "jobs", job.JobId.ToString("N"));
        var path = Path.Combine(jobDirectory, "control.json");
        var temporary = path + ".ui.tmp";
        var json = System.Text.Json.JsonSerializer.Serialize(
            new JobControl
            {
                RequestedAction = action,
                UpdatedAt = DateTimeOffset.UtcNow
            },
            AutoCutStudio.Core.Services.JsonDefaults.Options);

        await File.WriteAllTextAsync(temporary, json, cancellationToken);
        File.Move(temporary, path, true);
    }

    public static bool IsProcessAlive(int? processId)
    {
        if (processId is null)
        {
            return false;
        }

        try
        {
            using var process = Process.GetProcessById(processId.Value);
            return !process.HasExited;
        }
        catch
        {
            return false;
        }
    }
}
