using AutoCutStudio.Rebuild;
using Xunit;

namespace AutoCutStudio.Rebuild.Tests;

public sealed class SystemDoctorTests
{
    [Fact]
    public void RequiredFailureBlocksDoctor()
    {
        var report = new DoctorReport
        {
            Checks =
            [
                new DoctorCheck
                {
                    Category = "ระบบสื่อ",
                    Name = "FFmpeg",
                    State = DoctorState.Failed,
                    Required = true,
                    Summary = "ไม่พร้อม"
                }
            ]
        };

        Assert.True(report.HasRequiredFailures);
    }

    [Fact]
    public void OptionalUnavailableDoesNotBlockDoctor()
    {
        var report = new DoctorReport
        {
            Checks =
            [
                new DoctorCheck
                {
                    Category = "AI ภายในเครื่อง",
                    Name = "โมเดลตรวจจับใบหน้า",
                    State = DoctorState.Unavailable,
                    Required = false,
                    Summary = "ยังไม่ได้ติดตั้ง"
                }
            ]
        };

        Assert.False(report.HasRequiredFailures);
    }

    [Fact]
    public void SummaryContainsEveryStateCount()
    {
        var report = new DoctorReport
        {
            Checks =
            [
                NewCheck(DoctorState.Ready),
                NewCheck(DoctorState.Warning),
                NewCheck(DoctorState.Unavailable),
                NewCheck(DoctorState.Failed)
            ]
        };

        Assert.Contains("พร้อม 1", report.Summary, StringComparison.Ordinal);
        Assert.Contains("เตือน 1", report.Summary, StringComparison.Ordinal);
        Assert.Contains("ใช้ไม่ได้ 1", report.Summary, StringComparison.Ordinal);
        Assert.Contains("ล้มเหลว 1", report.Summary, StringComparison.Ordinal);
    }

    private static DoctorCheck NewCheck(DoctorState state) => new()
    {
        Category = "test",
        Name = state.ToString(),
        State = state,
        Summary = "test"
    };
}
