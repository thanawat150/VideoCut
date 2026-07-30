using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace AutoCutStudio.Infrastructure;

internal static class ProcessThreadController
{
    private const uint ThreadSuspendResume = 0x0002;

    public static void Suspend(Process process)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Pause is supported on Windows only.");
        }

        Apply(process, suspend: true);
    }

    public static void Resume(Process process)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Resume is supported on Windows only.");
        }

        Apply(process, suspend: false);
    }

    private static void Apply(Process process, bool suspend)
    {
        process.Refresh();
        foreach (ProcessThread thread in process.Threads)
        {
            var handle = OpenThread(ThreadSuspendResume, false, (uint)thread.Id);
            if (handle == IntPtr.Zero)
            {
                continue;
            }

            try
            {
                var result = suspend ? SuspendThread(handle) : ResumeThread(handle);
                if (result == uint.MaxValue)
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }
            }
            finally
            {
                CloseHandle(handle);
            }
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenThread(uint desiredAccess, bool inheritHandle, uint threadId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint SuspendThread(IntPtr threadHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint ResumeThread(IntPtr threadHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);
}

public sealed class JobCancelledException : OperationCanceledException
{
    public JobCancelledException(string message) : base(message)
    {
    }
}
