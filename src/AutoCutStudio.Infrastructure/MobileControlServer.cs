using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AutoCutStudio.Core.Models;
using AutoCutStudio.Core.Services;

namespace AutoCutStudio.Infrastructure;

public sealed class MobileControlServer : IAsyncDisposable
{
    private const long MaximumUploadBytes = 12L * 1024 * 1024 * 1024;
    private const int MaximumHeaderBytes = 64 * 1024;
    private const int MaximumJsonBytes = 128 * 1024;

    private readonly Func<ProjectDocument?> _getProject;
    private readonly Func<Task<IReadOnlyList<JobDocument>>> _listJobs;
    private readonly Func<Guid, Task<WorkflowExecutionResult>> _runWorkflow;
    private readonly Func<string, Task> _mediaUploaded;
    private readonly VisualWorkflowRepository _workflowRepository = new();
    private readonly ConcurrentDictionary<Guid, PendingApproval> _approvals = new();
    private readonly ConcurrentDictionary<Guid, RemoteRunState> _runs = new();

    private TcpListener? _listener;
    private CancellationTokenSource? _serverCancellation;
    private Task? _acceptLoop;

    public MobileControlServer(
        Func<ProjectDocument?> getProject,
        Func<Task<IReadOnlyList<JobDocument>>> listJobs,
        Func<Guid, Task<WorkflowExecutionResult>> runWorkflow,
        Func<string, Task> mediaUploaded)
    {
        _getProject = getProject;
        _listJobs = listJobs;
        _runWorkflow = runWorkflow;
        _mediaUploaded = mediaUploaded;
        AccessToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(4)).ToLowerInvariant();
    }

    public bool IsRunning => _listener is not null;
    public int Port { get; private set; }
    public string AccessToken { get; }
    public string DisplayUrl => $"http://{ResolveLanAddress()}:{Port}/?token={AccessToken}";

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (IsRunning)
        {
            return;
        }

        Port = FindAvailablePort(8787, 40);
        _listener = new TcpListener(IPAddress.Any, Port);
        _listener.Start(32);
        _serverCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _acceptLoop = AcceptLoopAsync(_serverCancellation.Token);
        await Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        if (_listener is null)
        {
            return;
        }

        _serverCancellation?.Cancel();
        _listener.Stop();
        try
        {
            if (_acceptLoop is not null)
            {
                await _acceptLoop;
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (SocketException)
        {
        }
        finally
        {
            _listener = null;
            _acceptLoop = null;
            _serverCancellation?.Dispose();
            _serverCancellation = null;
            foreach (var pending in _approvals.Values)
            {
                pending.Completion.TrySetCanceled();
            }
            _approvals.Clear();
        }
    }

    public async ValueTask DisposeAsync() => await StopAsync();

    public Task<bool> RequestApprovalAsync(
        WorkflowApprovalRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var pending = new PendingApproval(request);
        if (!_approvals.TryAdd(request.NodeId, pending))
        {
            throw new InvalidOperationException("มี Approval Request ของ Node นี้อยู่แล้ว");
        }

        if (cancellationToken.CanBeCanceled)
        {
            cancellationToken.Register(() => pending.Completion.TrySetCanceled(cancellationToken));
        }
        return AwaitApprovalAsync(request.NodeId, pending);
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && _listener is not null)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (SocketException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            _ = Task.Run(async () =>
            {
                using (client)
                {
                    try
                    {
                        client.NoDelay = true;
                        await HandleClientAsync(client, cancellationToken);
                    }
                    catch
                    {
                        // One malformed or disconnected request must not terminate the LAN server.
                    }
                }
            }, CancellationToken.None);
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        await using var stream = client.GetStream();
        var request = await ReadRequestAsync(stream, cancellationToken);
        if (request is null)
        {
            return;
        }

        if (request.Method == "OPTIONS")
        {
            await WriteResponseAsync(stream, 204, "text/plain", [], cancellationToken);
            return;
        }

        var uri = new Uri("http://autocut.local" + request.Target);
        var query = ParseQuery(uri.Query);
        var suppliedToken = query.GetValueOrDefault("token") ??
                            request.Headers.GetValueOrDefault("x-autocut-token");
        if (uri.AbsolutePath != "/" && !FixedTimeEquals(suppliedToken, AccessToken))
        {
            await WriteJsonAsync(stream, 401, new { error = "invalid_access_token" }, cancellationToken);
            return;
        }

        switch (request.Method, uri.AbsolutePath)
        {
            case ("GET", "/"):
                await WriteResponseAsync(
                    stream,
                    200,
                    "text/html; charset=utf-8",
                    Encoding.UTF8.GetBytes(BuildDashboardHtml(AccessToken)),
                    cancellationToken);
                break;
            case ("GET", "/api/status"):
                await WriteJsonAsync(stream, 200, await BuildStatusAsync(cancellationToken), cancellationToken);
                break;
            case ("POST", "/api/run"):
                await HandleRunAsync(stream, request, cancellationToken);
                break;
            case ("POST", "/api/upload"):
                await HandleUploadAsync(stream, request, query, cancellationToken);
                break;
            case ("POST", "/api/job-control"):
                await HandleJobControlAsync(stream, request, cancellationToken);
                break;
            case ("POST", "/api/approval"):
                await HandleApprovalAsync(stream, request, cancellationToken);
                break;
            default:
                await WriteJsonAsync(stream, 404, new { error = "not_found" }, cancellationToken);
                break;
        }
    }

    private async Task<object> BuildStatusAsync(CancellationToken cancellationToken)
    {
        var project = _getProject();
        if (project is null)
        {
            return new { connected = true, project = (object?)null };
        }

        var workflows = await _workflowRepository.ListAsync(project.RootPath, cancellationToken);
        var jobs = await _listJobs();
        var jobItems = new List<object>();
        foreach (var job in jobs.OrderByDescending(item => item.CreatedAt).Take(40))
        {
            JobProgress? progress = null;
            var progressPath = Path.Combine(
                job.ProjectRoot,
                "jobs",
                job.JobId.ToString("N"),
                "progress.json");
            if (File.Exists(progressPath))
            {
                try
                {
                    progress = await AtomicJsonFile.ReadAsync<JobProgress>(progressPath, cancellationToken);
                }
                catch
                {
                    // Preserve the job row even if a partial progress file cannot be read.
                }
            }

            jobItems.Add(new
            {
                id = job.JobId,
                type = job.JobType,
                status = progress?.Status ?? job.Status,
                progress = progress?.Progress ?? 0,
                message = progress?.Message ?? job.FailureMessage ?? string.Empty,
                output = Path.GetFileName(job.OutputPath),
                outputExists = File.Exists(job.OutputPath)
            });
        }

        return new
        {
            connected = true,
            serverTime = DateTimeOffset.Now,
            project = new
            {
                id = project.ProjectId,
                name = project.DisplayName,
                mediaCount = project.SourceMedia.Count
            },
            workflows = workflows.Select(item => new
            {
                id = item.WorkflowId,
                name = item.Name,
                nodes = item.Nodes.Count,
                modifiedAt = item.ModifiedAt
            }),
            runs = _runs.Values.OrderByDescending(item => item.StartedAt).Take(20),
            approvals = _approvals.Values.Select(item => new
            {
                nodeId = item.Request.NodeId,
                title = item.Request.Title,
                message = item.Request.Message,
                items = item.Request.Items
            }),
            jobs = jobItems
        };
    }

    private async Task HandleRunAsync(
        NetworkStream stream,
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        using var document = ParseJsonBody(request);
        if (!document.RootElement.TryGetProperty("workflowId", out var idElement) ||
            !Guid.TryParse(idElement.GetString(), out var workflowId))
        {
            await WriteJsonAsync(stream, 400, new { error = "workflowId_required" }, cancellationToken);
            return;
        }

        var remoteRun = new RemoteRunState
        {
            RunId = Guid.NewGuid(),
            WorkflowId = workflowId,
            Status = WorkflowRunStatuses.Queued,
            Message = "รับคำสั่งจากมือถือแล้ว",
            StartedAt = DateTimeOffset.UtcNow
        };
        _runs[remoteRun.RunId] = remoteRun;

        _ = Task.Run(async () =>
        {
            try
            {
                remoteRun.Status = WorkflowRunStatuses.Running;
                remoteRun.Message = "กำลัง Run Workflow";
                var result = await _runWorkflow(workflowId);
                remoteRun.Status = result.Run.Status;
                remoteRun.Message = $"สร้าง {result.CreatedJobs.Count} Job";
                remoteRun.JobIds = result.CreatedJobs.Select(item => item.JobId).ToList();
            }
            catch (Exception exception)
            {
                remoteRun.Status = WorkflowRunStatuses.Failed;
                remoteRun.Message = exception.Message;
            }
            finally
            {
                remoteRun.CompletedAt = DateTimeOffset.UtcNow;
            }
        }, CancellationToken.None);

        await WriteJsonAsync(
            stream,
            202,
            new { accepted = true, runId = remoteRun.RunId },
            cancellationToken);
    }

    private async Task HandleUploadAsync(
        NetworkStream stream,
        HttpRequest request,
        IReadOnlyDictionary<string, string> query,
        CancellationToken cancellationToken)
    {
        var project = _getProject();
        if (project is null)
        {
            await WriteJsonAsync(stream, 409, new { error = "project_not_open" }, cancellationToken);
            return;
        }
        if (!query.TryGetValue("name", out var requestedName))
        {
            await WriteJsonAsync(stream, 400, new { error = "file_name_required" }, cancellationToken);
            return;
        }
        if (request.ContentLength <= 0 || request.ContentLength > MaximumUploadBytes)
        {
            await WriteJsonAsync(stream, 413, new { error = "invalid_file_size" }, cancellationToken);
            return;
        }

        var extension = Path.GetExtension(requestedName).ToLowerInvariant();
        if (extension != ".mp4")
        {
            await WriteJsonAsync(stream, 415, new { error = "only_mp4_supported" }, cancellationToken);
            return;
        }

        var uploadDirectory = PathSecurity.EnsureUnderRoot(
            Path.Combine(project.RootPath, "source", "mobile-uploads"),
            project.RootPath);
        Directory.CreateDirectory(uploadDirectory);
        var safeStem = VersionedPathService.SanitizeFileName(
            Path.GetFileNameWithoutExtension(requestedName));
        var target = VersionedPathService.GetNextAvailablePath(uploadDirectory, safeStem, extension);

        await using (var output = new FileStream(
                         target,
                         FileMode.CreateNew,
                         FileAccess.Write,
                         FileShare.None,
                         1024 * 1024,
                         FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
            await output.WriteAsync(request.Body, cancellationToken);
            await output.FlushAsync(cancellationToken);
        }

        try
        {
            await _mediaUploaded(target);
        }
        catch
        {
            TryDelete(target);
            throw;
        }

        await WriteJsonAsync(
            stream,
            201,
            new { uploaded = true, name = Path.GetFileName(target) },
            cancellationToken);
    }

    private async Task HandleJobControlAsync(
        NetworkStream stream,
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        var project = _getProject();
        if (project is null)
        {
            await WriteJsonAsync(stream, 409, new { error = "project_not_open" }, cancellationToken);
            return;
        }

        using var document = ParseJsonBody(request);
        if (!document.RootElement.TryGetProperty("jobId", out var idElement) ||
            !Guid.TryParse(idElement.GetString(), out var jobId))
        {
            await WriteJsonAsync(stream, 400, new { error = "jobId_required" }, cancellationToken);
            return;
        }

        var action = document.RootElement.TryGetProperty("action", out var actionElement)
            ? actionElement.GetString()?.Trim().ToLowerInvariant()
            : null;
        if (action is not "pause" and not "resume" and not "cancel")
        {
            await WriteJsonAsync(stream, 400, new { error = "invalid_action" }, cancellationToken);
            return;
        }

        var controlPath = PathSecurity.EnsureUnderRoot(
            Path.Combine(project.RootPath, "jobs", jobId.ToString("N"), "control.json"),
            project.RootPath);
        if (!File.Exists(controlPath))
        {
            await WriteJsonAsync(stream, 404, new { error = "job_not_found" }, cancellationToken);
            return;
        }

        await AtomicJsonFile.WriteAsync(
            controlPath,
            new JobControl { RequestedAction = action, UpdatedAt = DateTimeOffset.UtcNow },
            cancellationToken);
        await WriteJsonAsync(stream, 200, new { updated = true, jobId, action }, cancellationToken);
    }

    private async Task HandleApprovalAsync(
        NetworkStream stream,
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        using var document = ParseJsonBody(request);
        if (!document.RootElement.TryGetProperty("nodeId", out var idElement) ||
            !Guid.TryParse(idElement.GetString(), out var nodeId) ||
            !_approvals.TryGetValue(nodeId, out var pending))
        {
            await WriteJsonAsync(stream, 404, new { error = "approval_not_found" }, cancellationToken);
            return;
        }

        var approved = document.RootElement.TryGetProperty("approved", out var approvedElement) &&
                       approvedElement.ValueKind == JsonValueKind.True;
        pending.Completion.TrySetResult(approved);
        await WriteJsonAsync(stream, 200, new { updated = true, nodeId, approved }, cancellationToken);
    }

    private async Task<bool> AwaitApprovalAsync(Guid nodeId, PendingApproval pending)
    {
        try
        {
            return await pending.Completion.Task;
        }
        finally
        {
            _approvals.TryRemove(nodeId, out _);
        }
    }

    private static JsonDocument ParseJsonBody(HttpRequest request)
    {
        if (request.Body.Length > MaximumJsonBytes)
        {
            throw new InvalidDataException("JSON request body ใหญ่เกินกำหนด");
        }
        return JsonDocument.Parse(request.Body.Length == 0 ? "{}"u8 : request.Body);
    }

    private static async Task<HttpRequest?> ReadRequestAsync(
        NetworkStream stream,
        CancellationToken cancellationToken)
    {
        using var received = new MemoryStream();
        var temporary = new byte[8192];
        var headerEnd = -1;
        while (received.Length < MaximumHeaderBytes)
        {
            var read = await stream.ReadAsync(temporary, cancellationToken);
            if (read == 0)
            {
                return null;
            }
            received.Write(temporary, 0, read);
            headerEnd = FindHeaderEnd(received.GetBuffer().AsSpan(0, (int)received.Length));
            if (headerEnd >= 0)
            {
                break;
            }
        }
        if (headerEnd < 0)
        {
            throw new InvalidDataException("HTTP header ใหญ่เกินกำหนด");
        }

        var bytes = received.ToArray();
        var headerText = Encoding.ASCII.GetString(bytes, 0, headerEnd);
        var lines = headerText.Split("\r\n", StringSplitOptions.None);
        var requestLine = lines[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (requestLine.Length < 2)
        {
            throw new InvalidDataException("Invalid HTTP request line");
        }

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines.Skip(1))
        {
            var separator = line.IndexOf(':');
            if (separator > 0)
            {
                headers[line[..separator].Trim().ToLowerInvariant()] = line[(separator + 1)..].Trim();
            }
        }

        var contentLength = headers.TryGetValue("content-length", out var lengthText) &&
                            long.TryParse(lengthText, out var parsedLength)
            ? parsedLength
            : 0;
        if (contentLength < 0 || contentLength > MaximumUploadBytes)
        {
            throw new InvalidDataException("Invalid Content-Length");
        }

        var bodyOffset = headerEnd + 4;
        var alreadyBuffered = Math.Max(0, bytes.Length - bodyOffset);
        using var body = new MemoryStream(
            contentLength > int.MaxValue ? 0 : (int)contentLength);
        if (alreadyBuffered > 0)
        {
            var count = (int)Math.Min(alreadyBuffered, contentLength);
            body.Write(bytes, bodyOffset, count);
        }

        var remaining = contentLength - body.Length;
        while (remaining > 0)
        {
            var read = await stream.ReadAsync(
                temporary.AsMemory(0, (int)Math.Min(temporary.Length, remaining)),
                cancellationToken);
            if (read == 0)
            {
                throw new EndOfStreamException("HTTP request body ขาดช่วง");
            }
            body.Write(temporary, 0, read);
            remaining -= read;
        }

        return new HttpRequest(
            requestLine[0].ToUpperInvariant(),
            requestLine[1],
            headers,
            contentLength,
            body.ToArray());
    }

    private static int FindHeaderEnd(ReadOnlySpan<byte> data)
    {
        for (var index = 0; index <= data.Length - 4; index++)
        {
            if (data[index] == 13 && data[index + 1] == 10 &&
                data[index + 2] == 13 && data[index + 3] == 10)
            {
                return index;
            }
        }
        return -1;
    }

    private static async Task WriteJsonAsync(
        NetworkStream stream,
        int status,
        object value,
        CancellationToken cancellationToken)
    {
        var body = JsonSerializer.SerializeToUtf8Bytes(value, JsonDefaults.Options);
        await WriteResponseAsync(stream, status, "application/json; charset=utf-8", body, cancellationToken);
    }

    private static async Task WriteResponseAsync(
        NetworkStream stream,
        int status,
        string contentType,
        byte[] body,
        CancellationToken cancellationToken)
    {
        var reason = status switch
        {
            200 => "OK",
            201 => "Created",
            202 => "Accepted",
            204 => "No Content",
            400 => "Bad Request",
            401 => "Unauthorized",
            404 => "Not Found",
            409 => "Conflict",
            413 => "Payload Too Large",
            415 => "Unsupported Media Type",
            _ => "Error"
        };
        var header =
            $"HTTP/1.1 {status} {reason}\r\n" +
            $"Content-Type: {contentType}\r\n" +
            $"Content-Length: {body.Length}\r\n" +
            "Cache-Control: no-store\r\n" +
            "Access-Control-Allow-Origin: *\r\n" +
            "Access-Control-Allow-Headers: content-type,x-autocut-token\r\n" +
            "Access-Control-Allow-Methods: GET,POST,OPTIONS\r\n" +
            "X-Content-Type-Options: nosniff\r\n" +
            "Connection: close\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(header), cancellationToken);
        if (body.Length > 0)
        {
            await stream.WriteAsync(body, cancellationToken);
        }
        await stream.FlushAsync(cancellationToken);
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = pair.IndexOf('=');
            var key = Uri.UnescapeDataString(separator < 0 ? pair : pair[..separator]);
            var value = Uri.UnescapeDataString(separator < 0 ? string.Empty : pair[(separator + 1)..]);
            result[key] = value;
        }
        return result;
    }

    private static bool FixedTimeEquals(string? supplied, string expected)
    {
        if (string.IsNullOrWhiteSpace(supplied))
        {
            return false;
        }
        var suppliedBytes = Encoding.UTF8.GetBytes(supplied);
        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        return suppliedBytes.Length == expectedBytes.Length &&
               CryptographicOperations.FixedTimeEquals(suppliedBytes, expectedBytes);
    }

    private static int FindAvailablePort(int start, int attempts)
    {
        var usedPorts = IPGlobalProperties.GetIPGlobalProperties()
            .GetActiveTcpListeners()
            .Select(item => item.Port)
            .ToHashSet();
        for (var port = start; port < start + attempts; port++)
        {
            if (!usedPorts.Contains(port))
            {
                return port;
            }
        }
        throw new InvalidOperationException("ไม่พบ Port ว่างสำหรับ Mobile Control");
    }

    private static string ResolveLanAddress()
    {
        try
        {
            var addresses = Dns.GetHostAddresses(Dns.GetHostName())
                .Where(address => address.AddressFamily == AddressFamily.InterNetwork &&
                                  !IPAddress.IsLoopback(address))
                .ToList();
            return addresses.FirstOrDefault(address =>
                       !address.ToString().StartsWith("169.254.", StringComparison.Ordinal))?.ToString()
                   ?? addresses.FirstOrDefault()?.ToString()
                   ?? "127.0.0.1";
        }
        catch
        {
            return "127.0.0.1";
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }

    private static string BuildDashboardHtml(string token) => DashboardHtml.Replace(
        "__AUTOCUT_TOKEN__",
        JsonSerializer.Serialize(token),
        StringComparison.Ordinal);

    private const string DashboardHtml = """
<!doctype html>
<html lang="th">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1,viewport-fit=cover">
<title>AutoCut Mobile Control</title>
<style>
:root{color-scheme:dark;--bg:#080d18;--panel:#111b2e;--line:#263752;--text:#f5f8ff;--muted:#91a0ba;--green:#4adeb8;--blue:#4a7dff;--purple:#8d5cf6;--red:#f85d6f}
*{box-sizing:border-box}body{margin:0;background:radial-gradient(circle at top,#17284a 0,#080d18 42%);font-family:system-ui,"Noto Sans Thai",sans-serif;color:var(--text)}
header{position:sticky;top:0;z-index:4;padding:16px;background:#0b1220e8;backdrop-filter:blur(14px);border-bottom:1px solid var(--line)}
.brand{display:flex;gap:12px;align-items:center}.logo{width:42px;height:42px;border-radius:13px;background:linear-gradient(135deg,var(--blue),var(--purple),var(--green));display:grid;place-items:center;font-weight:900}
main{padding:14px;max-width:900px;margin:auto}.card{background:#111b2ee8;border:1px solid var(--line);border-radius:16px;padding:14px;margin:12px 0;box-shadow:0 12px 35px #0005}
h2{font-size:16px;margin:0 0 10px}.muted{color:var(--muted);font-size:13px}.grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(150px,1fr));gap:8px}
button,select,input{width:100%;border:1px solid #385077;border-radius:11px;background:#182640;color:var(--text);padding:12px;font:inherit;margin:4px 0}button{font-weight:700}button.primary{background:linear-gradient(135deg,var(--blue),var(--purple));border-color:#7c8fff}button.good{background:#176b5a}button.bad{background:#6e2634}
.job{border-top:1px solid var(--line);padding:11px 0}.job:first-child{border:0}.row{display:flex;gap:8px;align-items:center;justify-content:space-between}.pill{padding:4px 8px;border-radius:999px;background:#223451;font-size:11px}.bar{height:7px;background:#26344b;border-radius:99px;overflow:hidden;margin-top:7px}.bar i{display:block;height:100%;background:linear-gradient(90deg,var(--blue),var(--green))}.approval{border:1px solid #9c7b23;background:#332b16;padding:12px;border-radius:12px;margin:8px 0}.toast{position:fixed;bottom:16px;left:16px;right:16px;max-width:600px;margin:auto;background:#17243a;border:1px solid var(--line);padding:13px;border-radius:12px;display:none}
</style>
</head>
<body>
<header><div class="brand"><div class="logo">▶</div><div><b>AutoCut Mobile Control</b><div class="muted" id="project">กำลังเชื่อมต่อ...</div></div></div></header>
<main>
<div class="card"><h2>อัปโหลดคลิปจากมือถือ</h2><input id="file" type="file" accept="video/mp4"><button class="primary" onclick="uploadClip()">อัปโหลด MP4 เข้าคอม</button><div class="muted" id="uploadState">ไฟล์จะถูกเก็บใน Project และตรวจด้วย FFprobe</div></div>
<div class="card"><h2>Run Automation</h2><select id="workflow"></select><button class="primary" onclick="runFlow()">▶ เริ่ม Workflow</button><div class="muted">คอมพิวเตอร์เป็นเครื่องประมวลผล มือถือใช้ควบคุมและตรวจสถานะ</div></div>
<div class="card"><h2>รอการอนุมัติ</h2><div id="approvals" class="muted">ไม่มีรายการรออนุมัติ</div></div>
<div class="card"><h2>Workflow Runs</h2><div id="runs" class="muted">ยังไม่มี Run</div></div>
<div class="card"><h2>Job Queue</h2><div id="jobs" class="muted">กำลังโหลด...</div></div>
</main>
<div class="toast" id="toast"></div>
<script>
const TOKEN=__AUTOCUT_TOKEN__;
const api=p=>p+(p.includes('?')?'&':'?')+'token='+encodeURIComponent(TOKEN);
const esc=s=>String(s??'').replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
function toast(message){const box=document.querySelector('#toast');box.textContent=message;box.style.display='block';setTimeout(()=>box.style.display='none',3500)}
async function refresh(){
 try{
  const d=await fetch(api('/api/status'),{cache:'no-store'}).then(r=>r.json());
  document.querySelector('#project').textContent=d.project?d.project.name+' · '+d.project.mediaCount+' คลิป':'ยังไม่มี Project';
  const select=document.querySelector('#workflow');const old=select.value;
  select.innerHTML=(d.workflows||[]).map(w=>`<option value="${w.id}">${esc(w.name)} · ${w.nodes} nodes</option>`).join('');if(old)select.value=old;
  document.querySelector('#runs').innerHTML=(d.runs||[]).length?(d.runs||[]).map(r=>`<div class="job"><div class="row"><b>${esc(r.status)}</b><span class="pill">${esc(r.runId).slice(0,8)}</span></div><div class="muted">${esc(r.message)}</div></div>`).join(''):'ยังไม่มี Run';
  document.querySelector('#approvals').innerHTML=(d.approvals||[]).length?(d.approvals||[]).map(a=>`<div class="approval"><b>${esc(a.title)}</b><div class="muted">${esc(a.message)}</div><ul>${(a.items||[]).slice(0,8).map(x=>`<li>${esc(x)}</li>`).join('')}</ul><div class="grid"><button class="good" onclick="approve('${a.nodeId}',true)">อนุมัติ</button><button class="bad" onclick="approve('${a.nodeId}',false)">ไม่ใช้</button></div></div>`).join(''):'ไม่มีรายการรออนุมัติ';
  document.querySelector('#jobs').innerHTML=(d.jobs||[]).length?(d.jobs||[]).map(j=>`<div class="job"><div class="row"><b>${esc(j.type)}</b><span class="pill">${esc(j.status)}</span></div><div class="muted">${esc(j.message)} · ${Number(j.progress||0).toFixed(0)}%</div><div class="bar"><i style="width:${Math.max(0,Math.min(100,j.progress||0))}%"></i></div><div class="grid"><button onclick="controlJob('${j.id}','pause')">Pause</button><button onclick="controlJob('${j.id}','resume')">Resume</button><button class="bad" onclick="controlJob('${j.id}','cancel')">Cancel</button></div></div>`).join(''):'ยังไม่มี Job';
 }catch(error){document.querySelector('#project').textContent='เชื่อมต่อไม่ได้';}
}
async function runFlow(){const workflowId=document.querySelector('#workflow').value;if(!workflowId)return toast('ยังไม่มี Workflow ที่บันทึกไว้');const response=await fetch(api('/api/run'),{method:'POST',headers:{'content-type':'application/json'},body:JSON.stringify({workflowId})});toast(response.ok?'รับคำสั่งแล้ว':'เริ่มไม่ได้');refresh()}
async function uploadClip(){const file=document.querySelector('#file').files[0];if(!file)return toast('เลือก MP4 ก่อน');const state=document.querySelector('#uploadState');state.textContent='กำลังอัปโหลด '+file.name;const response=await fetch(api('/api/upload')+'&name='+encodeURIComponent(file.name),{method:'POST',headers:{'content-type':'application/octet-stream'},body:file});const data=await response.json().catch(()=>({}));state.textContent=response.ok?'อัปโหลดและนำเข้าแล้ว: '+(data.name||file.name):'อัปโหลดไม่สำเร็จ';toast(state.textContent);refresh()}
async function controlJob(jobId,action){await fetch(api('/api/job-control'),{method:'POST',headers:{'content-type':'application/json'},body:JSON.stringify({jobId,action})});refresh()}
async function approve(nodeId,approved){await fetch(api('/api/approval'),{method:'POST',headers:{'content-type':'application/json'},body:JSON.stringify({nodeId,approved})});refresh()}
setInterval(refresh,2000);refresh();
</script>
</body>
</html>
""";

    private sealed record HttpRequest(
        string Method,
        string Target,
        IReadOnlyDictionary<string, string> Headers,
        long ContentLength,
        byte[] Body);

    private sealed class PendingApproval
    {
        public PendingApproval(WorkflowApprovalRequest request) => Request = request;
        public WorkflowApprovalRequest Request { get; }
        public TaskCompletionSource<bool> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class RemoteRunState
    {
        public Guid RunId { get; init; }
        public Guid WorkflowId { get; init; }
        public string Status { get; set; } = WorkflowRunStatuses.Queued;
        public string Message { get; set; } = string.Empty;
        public List<Guid> JobIds { get; set; } = [];
        public DateTimeOffset StartedAt { get; init; }
        public DateTimeOffset? CompletedAt { get; set; }
    }
}
