using AutoCutStudio.Core.Models;

namespace AutoCutStudio.Infrastructure;

public sealed class ComputerVisionToolLocator
{
    public ComputerVisionToolAvailability Locate()
    {
        var facePath = Find(
            "AUTOCUT_FACE_MODEL_PATH",
            Path.Combine(AppContext.BaseDirectory, "models", "opencv", "face_detection_yunet_2023mar.onnx"));
        var objectPath = Find(
            "AUTOCUT_OBJECT_MODEL_PATH",
            Path.Combine(AppContext.BaseDirectory, "models", "opencv", "yolov8n.onnx"));
        var missing = new List<string>();
        if (facePath is null) missing.Add("YuNet face model");
        if (objectPath is null) missing.Add("YOLOv8 object model");
        return missing.Count > 0
            ? new ComputerVisionToolAvailability(
                false,
                facePath,
                objectPath,
                "missing",
                $"ต้องติดตั้ง Computer Vision Model เพิ่ม: {string.Join(", ", missing)}")
            : new ComputerVisionToolAvailability(
                true,
                facePath,
                objectPath,
                "ready",
                "OpenCV YuNet Face Detection และ YOLO Object Detection พร้อมใช้งาน");
    }

    private static string? Find(string environmentVariable, string bundledPath)
    {
        var candidates = new[]
        {
            Environment.GetEnvironmentVariable(environmentVariable),
            bundledPath
        };
        return candidates
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => Path.GetFullPath(value!))
            .FirstOrDefault(File.Exists);
    }
}
