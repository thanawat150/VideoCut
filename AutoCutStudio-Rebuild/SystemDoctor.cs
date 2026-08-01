using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Media;
using Microsoft.Win32;

namespace AutoCutStudio.Rebuild;

public enum DoctorState
{
    Ready,
    Warning,
    Unavailable,
    Failed
}

public sealed class DoctorCheck
{
    public required string Category { get; init; }
    public required string Name { get; init; }
    public DoctorState State { get; init; }
    public bool Required { get; init; }
    public required string Summary { get; init; }
    public string Details { get; init; } = string.Empty;
    public string Location { get; init; } = string.Empty;
}

public sealed class DoctorReport
{
    public DateTimeOffset GeneratedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public string Product { get; init; } = "AutoCut Studio Rebuild";
    public string ApplicationVersion { get; init; } = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown";
    public string OperatingSystem { get; init; } = RuntimeInformation.OSDescription;
    public string ProcessArchitecture { get; init; } = RuntimeInformation.ProcessArchitecture.ToString();
    public string TargetDirectory { get; init; } = string.Empty;
    public List<DoctorCheck> Checks { get; init; } = [];

    [JsonIgnore]
    public bool HasRequiredFailures => Checks.Any(check =>
        check.Required && check.State is DoctorState.Failed or DoctorState.Unavailable);

    [JsonIgnore]
    public string Summary
    {
        get
        {
            var ready = Checks.Count(check => check.State == DoctorState.Ready);
            var warning = Checks.Count(check => check.State == DoctorState.Warning);
            var unavailable = Checks.Count(check => check.State == DoctorState.Unavailable);
            var failed = Checks.Count(check => check.State == DoctorState.Failed);
            return $"พร้อม {ready} • เตือน {warning} • ใช้ไม่ได้ {unavailable} • ล้มเหลว {failed}";
        }
    }
}

public sealed class SystemDoctorService
{
    private static readonly TimeSpan ToolTimeout = TimeSpan.FromSeconds(12);

    public async Task<DoctorReport> RunAsync(string? projectRoot, CancellationToken cancellationToken = default)
    {
        var targetDirectory = ResolveTargetDirectory(projectRoot);
        var checks = new List<DoctorCheck>();

        checks.Add(await CheckToolAsync(
            "ระบบสื่อ",
            "FFmpeg",
            Tools.Find("ffmpeg"),
            ["-hide_banner", "-version"],
            required: true,
            cancellationToken));

        checks.Add(await CheckToolAsync(
            "ระบบสื่อ",
            "FFprobe",
            Tools.Find("ffprobe"),
            ["-hide_banner", "-version"],
            required: true,
            cancellationToken));

        checks.Add(await CheckGpuEncodersAsync(cancellationToken));

        checks.Add(await CheckToolAsync(
            "การถอดเสียง",
            "Whisper CLI",
            Tools.Find("whisper-cli"),
            ["--help"],
            required: false,
            cancellationToken));

        checks.Add(CheckWhisperModel());
        checks.AddRange(CheckOpenCvModels());
        checks.Add(CheckThaiFonts());
        checks.Add(CheckDiskSpace(targetDirectory));
        checks.Add(await CheckWritePermissionAsync(targetDirectory, cancellationToken));
        checks.Add(CheckWindowsVoices());
        checks.Add(CheckOpenAiProvider());

        return new DoctorReport
        {
            TargetDirectory = targetDirectory,
            Checks = checks
        };
    }

    public static async Task ExportAsync(DoctorReport report, string path, CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        await File.WriteAllTextAsync(
            fullPath,
            JsonSerializer.Serialize(report, JsonConfig.Options),
            cancellationToken);
    }

    private static async Task<DoctorCheck> CheckToolAsync(
        string category,
        string name,
        string executable,
        IReadOnlyList<string> arguments,
        bool required,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await RunCommandAsync(executable, arguments, ToolTimeout, cancellationToken);
            if (result.TimedOut)
            {
                return NewCheck(category, name, DoctorState.Failed, required,
                    "โปรแกรมไม่ตอบสนองภายในเวลาที่กำหนด", result.Error, executable);
            }

            if (result.ExitCode != 0)
            {
                return NewCheck(category, name, DoctorState.Failed, required,
                    $"เปิดได้แต่คืนค่า Exit Code {result.ExitCode}", FirstUsefulLine(result.Error), executable);
            }

            return NewCheck(category, name, DoctorState.Ready, required,
                "พร้อมใช้งาน", FirstUsefulLine(result.Output, result.Error), ResolveDisplayPath(executable));
        }
        catch (Exception exception) when (exception is FileNotFoundException or System.ComponentModel.Win32Exception)
        {
            return NewCheck(category, name, DoctorState.Unavailable, required,
                "ไม่พบโปรแกรม", exception.Message, ResolveDisplayPath(executable));
        }
        catch (Exception exception)
        {
            return NewCheck(category, name, DoctorState.Failed, required,
                "ตรวจสอบไม่สำเร็จ", exception.Message, ResolveDisplayPath(executable));
        }
    }

    private static async Task<DoctorCheck> CheckGpuEncodersAsync(CancellationToken cancellationToken)
    {
        try
        {
            var executable = Tools.Find("ffmpeg");
            var result = await RunCommandAsync(
                executable,
                ["-hide_banner", "-encoders"],
                ToolTimeout,
                cancellationToken);

            if (result.ExitCode != 0 || result.TimedOut)
            {
                return NewCheck("ประสิทธิภาพ", "GPU Encoder", DoctorState.Warning, false,
                    "อ่านรายการ Encoder ไม่สำเร็จ",
                    FirstUsefulLine(result.Error),
                    ResolveDisplayPath(executable));
            }

            var text = result.Output + Environment.NewLine + result.Error;
            var candidates = new[] { "h264_nvenc", "h264_qsv", "h264_amf", "hevc_nvenc", "hevc_qsv", "hevc_amf" };
            var available = candidates.Where(name => text.Contains(name, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (available.Length == 0)
            {
                return NewCheck("ประสิทธิภาพ", "GPU Encoder", DoctorState.Warning, false,
                    "ไม่พบ Hardware Encoder ใน FFmpeg build นี้",
                    "ยัง Export ด้วย CPU/libx264 ได้ตามปกติ",
                    ResolveDisplayPath(executable));
            }

            return NewCheck("ประสิทธิภาพ", "GPU Encoder", DoctorState.Ready, false,
                "FFmpeg มี Hardware Encoder",
                string.Join(", ", available) + " — ต้องทดสอบกับ GPU จริงอีกครั้งตอน Export",
                ResolveDisplayPath(executable));
        }
        catch (Exception exception)
        {
            return NewCheck("ประสิทธิภาพ", "GPU Encoder", DoctorState.Warning, false,
                "ยังตรวจ Hardware Encoder ไม่ได้", exception.Message);
        }
    }

    private static DoctorCheck CheckWhisperModel()
    {
        var configured = Environment.GetEnvironmentVariable("AUTOCUT_REBUILD_WHISPER_MODEL");
        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(configured)) candidates.Add(configured);
        candidates.Add(Path.Combine(AppContext.BaseDirectory, "models", "whisper", "ggml-base-q5_1.bin"));
        candidates.Add(Path.Combine(AppContext.BaseDirectory, "models", "whisper", "ggml-base.bin"));

        var modelDirectory = Path.Combine(AppContext.BaseDirectory, "models", "whisper");
        if (Directory.Exists(modelDirectory))
            candidates.AddRange(Directory.EnumerateFiles(modelDirectory, "*.bin", SearchOption.TopDirectoryOnly));

        var model = candidates.Select(TryFullPath).FirstOrDefault(path => path is not null && File.Exists(path));
        if (model is null)
        {
            return NewCheck("การถอดเสียง", "Whisper Model", DoctorState.Unavailable, false,
                "ยังไม่มีโมเดลถอดเสียง",
                "เลือกไฟล์ .bin ตอนเริ่มถอดเสียง หรือตั้งค่า AUTOCUT_REBUILD_WHISPER_MODEL");
        }

        var length = new FileInfo(model).Length;
        var state = length >= 10 * 1024 * 1024 ? DoctorState.Ready : DoctorState.Warning;
        return NewCheck("การถอดเสียง", "Whisper Model", state, false,
            state == DoctorState.Ready ? "พบโมเดล" : "พบไฟล์โมเดลที่มีขนาดเล็กผิดปกติ",
            FormatBytes(length), model);
    }

    private static IEnumerable<DoctorCheck> CheckOpenCvModels()
    {
        yield return CheckOptionalModel(
            "AI ภายในเครื่อง",
            "โมเดลตรวจจับใบหน้า",
            "AUTOCUT_REBUILD_FACE_MODEL",
            Path.Combine("models", "opencv", "face_detection_yunet_2023mar.onnx"));

        yield return CheckOptionalModel(
            "AI ภายในเครื่อง",
            "โมเดลตรวจจับวัตถุ",
            "AUTOCUT_REBUILD_OBJECT_MODEL",
            Path.Combine("models", "opencv", "object_detection_yolox_2022nov.onnx"));
    }

    private static DoctorCheck CheckOptionalModel(string category, string name, string environmentVariable, string relativePath)
    {
        var configured = Environment.GetEnvironmentVariable(environmentVariable);
        var candidates = new[]
        {
            configured,
            Path.Combine(AppContext.BaseDirectory, relativePath)
        };
        var path = candidates.Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => TryFullPath(value!))
            .FirstOrDefault(value => value is not null && File.Exists(value));

        if (path is null)
        {
            return NewCheck(category, name, DoctorState.Unavailable, false,
                "ยังไม่ได้ติดตั้ง",
                $"ฟังก์ชันที่ใช้โมเดลนี้จะยังไม่พร้อม ตั้งค่า {environmentVariable} หรือวางไฟล์ใน {relativePath}");
        }

        var length = new FileInfo(path).Length;
        return NewCheck(category, name, length > 100_000 ? DoctorState.Ready : DoctorState.Warning, false,
            length > 100_000 ? "พบโมเดล" : "ไฟล์โมเดลมีขนาดเล็กผิดปกติ",
            FormatBytes(length), path);
    }

    private static DoctorCheck CheckThaiFonts()
    {
        try
        {
            var preferred = new[] { "Leelawadee UI", "Nirmala UI", "Tahoma", "TH Sarabun New", "Sarabun" };
            var installed = Fonts.SystemFontFamilies.Select(font => font.Source).ToArray();
            var found = preferred.Where(name => installed.Contains(name, StringComparer.OrdinalIgnoreCase)).ToArray();
            if (found.Length == 0)
            {
                return NewCheck("ภาษาไทย", "ฟอนต์ภาษาไทย", DoctorState.Warning, false,
                    "ไม่พบฟอนต์ไทยที่แนะนำ",
                    "ติดตั้ง Sarabun หรือ TH Sarabun New ก่อนสร้าง Subtitle ภาษาไทย");
            }

            return NewCheck("ภาษาไทย", "ฟอนต์ภาษาไทย", DoctorState.Ready, false,
                "พร้อมแสดงผลภาษาไทย", string.Join(", ", found));
        }
        catch (Exception exception)
        {
            return NewCheck("ภาษาไทย", "ฟอนต์ภาษาไทย", DoctorState.Warning, false,
                "อ่านรายการฟอนต์ไม่สำเร็จ", exception.Message);
        }
    }

    private static DoctorCheck CheckDiskSpace(string targetDirectory)
    {
        try
        {
            var existing = FindExistingDirectory(targetDirectory);
            var root = Path.GetPathRoot(existing) ?? existing;
            var drive = new DriveInfo(root);
            var free = drive.AvailableFreeSpace;
            var state = free >= 10L * 1024 * 1024 * 1024
                ? DoctorState.Ready
                : free >= 2L * 1024 * 1024 * 1024
                    ? DoctorState.Warning
                    : DoctorState.Failed;
            var summary = state switch
            {
                DoctorState.Ready => "พื้นที่ว่างเพียงพอ",
                DoctorState.Warning => "พื้นที่ว่างเริ่มน้อย",
                _ => "พื้นที่ว่างต่ำกว่า 2 GB"
            };
            return NewCheck("พื้นที่จัดเก็บ", "พื้นที่ดิสก์", state, false,
                summary, $"ว่าง {FormatBytes(free)} จาก {FormatBytes(drive.TotalSize)}", root);
        }
        catch (Exception exception)
        {
            return NewCheck("พื้นที่จัดเก็บ", "พื้นที่ดิสก์", DoctorState.Warning, false,
                "อ่านพื้นที่ดิสก์ไม่สำเร็จ", exception.Message, targetDirectory);
        }
    }

    private static async Task<DoctorCheck> CheckWritePermissionAsync(string targetDirectory, CancellationToken cancellationToken)
    {
        var testPath = Path.Combine(targetDirectory, $".autocut-write-test-{Guid.NewGuid():N}.tmp");
        try
        {
            Directory.CreateDirectory(targetDirectory);
            await File.WriteAllTextAsync(testPath, "AutoCut Studio write test", cancellationToken);
            File.Delete(testPath);
            return NewCheck("พื้นที่จัดเก็บ", "สิทธิ์เขียน Output", DoctorState.Ready, true,
                "เขียนไฟล์ได้", "ผ่านการสร้างและลบไฟล์ทดสอบ", targetDirectory);
        }
        catch (Exception exception)
        {
            try { if (File.Exists(testPath)) File.Delete(testPath); } catch { }
            return NewCheck("พื้นที่จัดเก็บ", "สิทธิ์เขียน Output", DoctorState.Failed, true,
                "ไม่สามารถเขียนไฟล์ในโฟลเดอร์เป้าหมาย", exception.Message, targetDirectory);
        }
    }

    private static DoctorCheck CheckWindowsVoices()
    {
        if (!OperatingSystem.IsWindows())
        {
            return NewCheck("เสียงพากย์", "Windows Voices", DoctorState.Unavailable, false,
                "รองรับเฉพาะ Windows");
        }

        try
        {
            var count = 0;
            count += CountRegistrySubKeys(RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Speech\Voices\Tokens");
            count += CountRegistrySubKeys(RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Speech_OneCore\Voices\Tokens");
            count += CountRegistrySubKeys(RegistryHive.CurrentUser, @"SOFTWARE\Microsoft\Speech\Voices\Tokens");
            if (count == 0)
            {
                return NewCheck("เสียงพากย์", "Windows Voices", DoctorState.Unavailable, false,
                    "ไม่พบเสียงพูดที่ติดตั้ง",
                    "เพิ่ม Language/Speech pack ใน Windows Settings");
            }

            return NewCheck("เสียงพากย์", "Windows Voices", DoctorState.Ready, false,
                $"พบเสียงพูด {count} รายการ",
                "รายงานเฉพาะจำนวน ไม่บันทึกข้อมูลส่วนตัว");
        }
        catch (Exception exception)
        {
            return NewCheck("เสียงพากย์", "Windows Voices", DoctorState.Warning, false,
                "อ่านรายการเสียงพูดไม่สำเร็จ", exception.Message);
        }
    }

    private static DoctorCheck CheckOpenAiProvider()
    {
        var configured = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OPENAI_API_KEY"));
        return configured
            ? NewCheck("Cloud Provider", "OpenAI", DoctorState.Ready, false,
                "พบการตั้งค่า API Key",
                "รายงานไม่อ่านหรือบันทึกค่าของ API Key")
            : NewCheck("Cloud Provider", "OpenAI", DoctorState.Unavailable, false,
                "ยังไม่ได้ตั้งค่า",
                "การตัดต่อ Local และ FFmpeg ยังใช้งานได้ตามปกติ");
    }

    private static async Task<CommandResult> RunCommandAsync(
        string executable,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var info = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = info };
        if (!process.Start()) throw new InvalidOperationException($"Unable to start {executable}.");

        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        try
        {
            await process.WaitForExitAsync(timeoutSource.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            await Task.WhenAll(outputTask, errorTask);
            return new CommandResult(-1, await outputTask, await errorTask, true);
        }

        return new CommandResult(process.ExitCode, await outputTask, await errorTask, false);
    }

    private static DoctorCheck NewCheck(
        string category,
        string name,
        DoctorState state,
        bool required,
        string summary,
        string details = "",
        string location = "") => new()
        {
            Category = category,
            Name = name,
            State = state,
            Required = required,
            Summary = summary,
            Details = details,
            Location = location
        };

    private static string ResolveTargetDirectory(string? projectRoot)
    {
        if (!string.IsNullOrWhiteSpace(projectRoot))
            return Path.GetFullPath(Path.Combine(projectRoot, "exports"));

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AutoCutStudio-Rebuild",
            "diagnostics");
    }

    private static string FindExistingDirectory(string path)
    {
        var current = Path.GetFullPath(path);
        while (!Directory.Exists(current))
        {
            var parent = Path.GetDirectoryName(current);
            if (string.IsNullOrWhiteSpace(parent) || string.Equals(parent, current, StringComparison.OrdinalIgnoreCase))
                return Path.GetPathRoot(current) ?? current;
            current = parent;
        }
        return current;
    }

    private static string? TryFullPath(string path)
    {
        try { return Path.GetFullPath(path); }
        catch { return null; }
    }

    private static string ResolveDisplayPath(string executable)
    {
        try { return Path.IsPathRooted(executable) ? Path.GetFullPath(executable) : executable; }
        catch { return executable; }
    }

    private static string FirstUsefulLine(params string[] values) => values
        .SelectMany(value => (value ?? string.Empty).Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries))
        .Select(line => line.Trim())
        .FirstOrDefault(line => line.Length > 0) ?? string.Empty;

    private static int CountRegistrySubKeys(RegistryHive hive, string path)
    {
        using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
        using var key = baseKey.OpenSubKey(path, writable: false);
        return key?.SubKeyCount ?? 0;
    }

    private static string FormatBytes(long value)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var size = (double)Math.Max(0, value);
        var index = 0;
        while (size >= 1024 && index < units.Length - 1)
        {
            size /= 1024;
            index++;
        }
        return $"{size:0.##} {units[index]}";
    }

    private sealed record CommandResult(int ExitCode, string Output, string Error, bool TimedOut);
}
