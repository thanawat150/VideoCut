using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Windows;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace AutoCutStudio.Rebuild;

public sealed partial class MainWindow
{
    private async Task RunJob(JobDocument job)
    {
        if (_project is null || _workerProcess is not null) return;
        await JobStore.SaveAsync(job);
        _project.JobHistory.Add(job.JobId);
        await ProjectStore.SaveAsync(_project);
        var executable = Environment.ProcessPath ?? throw new InvalidOperationException("Application path is unavailable.");
        var info = new ProcessStartInfo { FileName = executable, UseShellExecute = false, CreateNoWindow = true };
        info.ArgumentList.Add("--worker"); info.ArgumentList.Add(JobStore.JobPath(job));
        _workerProcess = new Process { StartInfo = info };
        _runningJob = job;
        _status.Text = "กำลังทำงาน " + job.Kind;
        _progress.Value = 0;
        try
        {
            if (!_workerProcess.Start()) throw new InvalidOperationException("Unable to start Worker process.");
            var progressPath = Path.Combine(JobStore.DirectoryFor(job), "progress.json");
            while (!_workerProcess.HasExited)
            {
                if (File.Exists(progressPath))
                {
                    try
                    {
                        var current = await AtomicJson.ReadAsync<ProgressDocument>(progressPath);
                        _progress.Value = Math.Clamp(current.Progress * 100, 0, 100);
                        _status.Text = string.IsNullOrWhiteSpace(current.Message) ? current.Step : current.Step + " — " + current.Message;
                    }
                    catch (IOException) { }
                    catch (JsonException) { }
                }
                await Task.Delay(250);
            }
            await _workerProcess.WaitForExitAsync();
            var exit = _workerProcess.ExitCode;
            _progress.Value = exit == 0 ? 100 : 0;
            _status.Text = exit == 0 ? "เสร็จ: " + job.OutputPath : "งานล้มเหลว: " + JobStore.JobPath(job);
            if (exit == 0) { _project.OutputHistory.Add(job.OutputPath); await ProjectStore.SaveAsync(_project); }
        }
        finally
        {
            _workerProcess.Dispose(); _workerProcess = null; _runningJob = null;
        }
    }

    private async void CancelJob(object? sender, RoutedEventArgs e)
    {
        if (_workerProcess is null || _runningJob is null || _workerProcess.HasExited) return;
        _workerProcess.Kill(entireProcessTree: true);
        _runningJob.State = JobState.Cancelled;
        _runningJob.FinishedAt = DateTimeOffset.UtcNow;
        await JobStore.SaveAsync(_runningJob);
        _status.Text = "ยกเลิกงานแล้ว";
        _progress.Value = 0;
    }
    private bool Selected(out MediaAsset? media) { media = _media.SelectedItem as MediaAsset; if (_project is null || media is null) { MessageBox.Show("กรุณาเปิดโปรเจกต์และเลือกไฟล์"); return false; } return true; }
    private bool RequireProject() { if (_project is not null) return true; MessageBox.Show("กรุณาสร้างหรือเปิดโปรเจกต์ก่อน"); return false; }
    private void Refresh(string text) { _media.ItemsSource = null; _media.ItemsSource = _project?.Media; _media.DisplayMemberPath = nameof(MediaAsset.DisplayName); _timeline.ItemsSource = null; _timeline.ItemsSource = _project?.Timeline.OrderBy(x => x.Order).Select(x => { var media = _project.Media.Single(m => m.MediaId == x.MediaId); return new TimelineRow(x.ClipId, x.Order, media.DisplayName, x.SourceInSeconds, x.SourceOutSeconds, x.DurationSeconds); }).ToList(); _status.Text = text; Title = _project is null ? "AutoCut Studio Rebuild" : "AutoCut Studio Rebuild — " + _project.DisplayName; }
    private sealed record TimelineRow(Guid ClipId, int Order, string File, double InSeconds, double OutSeconds, double DurationSeconds);}
