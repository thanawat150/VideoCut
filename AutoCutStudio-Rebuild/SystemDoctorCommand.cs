namespace AutoCutStudio.Rebuild;

public static class SystemDoctorCommand
{
    public static int Run()
    {
        try
        {
            var report = new SystemDoctorService().RunAsync(projectRoot: null).GetAwaiter().GetResult();
            var path = Path.Combine(Path.GetTempPath(), "autocut-rebuild-doctor.json");
            SystemDoctorService.ExportAsync(report, path).GetAwaiter().GetResult();
            return report.HasRequiredFailures ? 1 : 0;
        }
        catch (Exception exception)
        {
            try
            {
                File.WriteAllText(
                    Path.Combine(Path.GetTempPath(), "autocut-rebuild-doctor-error.log"),
                    exception.ToString());
            }
            catch
            {
                // The command must still return a truthful failure when even logging is unavailable.
            }
            return 1;
        }
    }
}
