using AutoCutStudio.Core.Models;

namespace AutoCutStudio.Infrastructure;

public sealed class ComputerVisionToolLocator
{
    public ComputerVisionToolAvailability Locate()
    {
        var explicitPath = Environment.GetEnvironmentVariable("AUTOCUT_FACE_MODEL_PATH");
        var candidates = new[]
        {
            explicitPath,
            Path.Combine(AppContext.BaseDirectory, "models", "opencv", "face_detection_yunet_2023mar.onnx")
        };
        var path = candidates
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => Path.GetFullPath(value!))
            .FirstOrDefault(File.Exists);
        return path is null
            ? new ComputerVisionToolAvailability(
                false,
                null,
                "missing",
                "ต้องติดตั้งโมเดล Face Detection เพิ่ม: YuNet ONNX")
            : new ComputerVisionToolAvailability(
                true,
                path,
                "ready",
                "OpenCV YuNet Face Detection พร้อมใช้งาน");
    }
}
