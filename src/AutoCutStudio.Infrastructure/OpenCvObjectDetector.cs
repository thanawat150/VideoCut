using AutoCutStudio.Core.Models;
using OpenCvSharp;
using OpenCvSharp.Dnn;

namespace AutoCutStudio.Infrastructure;

public sealed class OpenCvObjectDetector
{
    private static readonly string[] CocoLabels =
    [
        "person", "bicycle", "car", "motorcycle", "airplane", "bus", "train", "truck", "boat", "traffic light",
        "fire hydrant", "stop sign", "parking meter", "bench", "bird", "cat", "dog", "horse", "sheep", "cow",
        "elephant", "bear", "zebra", "giraffe", "backpack", "umbrella", "handbag", "tie", "suitcase", "frisbee",
        "skis", "snowboard", "sports ball", "kite", "baseball bat", "baseball glove", "skateboard", "surfboard",
        "tennis racket", "bottle", "wine glass", "cup", "fork", "knife", "spoon", "bowl", "banana", "apple",
        "sandwich", "orange", "broccoli", "carrot", "hot dog", "pizza", "donut", "cake", "chair", "couch",
        "potted plant", "bed", "dining table", "toilet", "tv", "laptop", "mouse", "remote", "keyboard",
        "cell phone", "microwave", "oven", "toaster", "sink", "refrigerator", "book", "clock", "vase", "scissors",
        "teddy bear", "hair drier", "toothbrush"
    ];

    private readonly ToolLocator _mediaTools;
    private readonly ComputerVisionToolLocator _visionTools;

    public OpenCvObjectDetector(ToolLocator mediaTools, ComputerVisionToolLocator visionTools)
    {
        _mediaTools = mediaTools;
        _visionTools = visionTools;
    }

    public async Task<ObjectAnalysisResult> AnalyzeAsync(
        string inputPath,
        double durationSeconds,
        double sampleIntervalSeconds = 2,
        float confidenceThreshold = 0.35f,
        CancellationToken cancellationToken = default)
    {
        var availability = _visionTools.Locate();
        if (string.IsNullOrWhiteSpace(availability.ObjectModelPath))
            throw new InvalidOperationException(availability.Message);
        var working = Path.Combine(Path.GetTempPath(), "AutoCut-Objects", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(working);
        try
        {
            var maximumFrames = Math.Min(90, Math.Max(1, (int)Math.Ceiling(durationSeconds / sampleIntervalSeconds)));
            var frames = await new FfmpegFrameSampler(_mediaTools).ExtractAsync(
                inputPath, working, sampleIntervalSeconds, maximumFrames, cancellationToken);
            using var net = CvDnn.ReadNetFromOnnx(availability.ObjectModelPath);
            net.SetPreferableBackend(Backend.OPENCV);
            net.SetPreferableTarget(Target.CPU);
            var detections = new List<ObjectDetection>();
            foreach (var frame in frames)
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var image = Cv2.ImRead(frame.Path, ImreadModes.Color);
                if (image.Empty()) continue;
                detections.AddRange(DetectFrame(net, image, frame.TimeSeconds, confidenceThreshold));
            }
            return new ObjectAnalysisResult
            {
                SourcePath = inputPath,
                SampleIntervalSeconds = sampleIntervalSeconds,
                Detections = detections
            };
        }
        finally
        {
            TryDeleteDirectory(working);
        }
    }

    internal static IReadOnlyList<ObjectDetection> DetectFrame(
        Net net,
        Mat image,
        double timeSeconds,
        float confidenceThreshold = 0.35f)
    {
        const int inputSize = 640;
        using var blob = CvDnn.BlobFromImage(
            image,
            1.0 / 255.0,
            new Size(inputSize, inputSize),
            new Scalar(),
            swapRB: true,
            crop: false);
        net.SetInput(blob);
        using var output = net.Forward();
        if (output.Empty() || output.Total() < 84)
            return [];

        var dimensions = Enumerable.Range(0, output.Dims).Select(output.Size).ToArray();
        var channelsFirst = dimensions.Length >= 3 && dimensions[^2] is 84 or 85;
        var rows = channelsFirst ? dimensions[^2] : dimensions[^2];
        using var matrix = output.Reshape(1, rows);
        var candidateCount = channelsFirst ? matrix.Cols : matrix.Rows;
        var featureCount = channelsFirst ? matrix.Rows : matrix.Cols;
        if (featureCount < 84) return [];

        var hasObjectness = featureCount >= 85;
        var classOffset = hasObjectness ? 5 : 4;
        var candidates = new List<Candidate>();
        for (var index = 0; index < candidateCount; index++)
        {
            float Value(int feature) => channelsFirst
                ? matrix.At<float>(feature, index)
                : matrix.At<float>(index, feature);

            var objectness = hasObjectness ? Math.Clamp(Value(4), 0, 1) : 1f;
            var bestClass = -1;
            var bestScore = 0f;
            for (var classIndex = classOffset; classIndex < featureCount; classIndex++)
            {
                var score = Math.Clamp(Value(classIndex), 0, 1) * objectness;
                if (score > bestScore)
                {
                    bestScore = score;
                    bestClass = classIndex - classOffset;
                }
            }
            if (bestScore < confidenceThreshold || bestClass < 0 || bestClass >= CocoLabels.Length)
                continue;

            var centerX = Value(0) / inputSize;
            var centerY = Value(1) / inputSize;
            var width = Value(2) / inputSize;
            var height = Value(3) / inputSize;
            var x = Math.Clamp(centerX - width / 2, 0, 1);
            var y = Math.Clamp(centerY - height / 2, 0, 1);
            width = Math.Clamp(width, 0, 1 - x);
            height = Math.Clamp(height, 0, 1 - y);
            if (width <= 0.002 || height <= 0.002) continue;
            candidates.Add(new Candidate(bestClass, bestScore, x, y, width, height));
        }

        var selected = new List<Candidate>();
        foreach (var candidate in candidates.OrderByDescending(item => item.Score))
        {
            if (selected.Any(existing => existing.ClassId == candidate.ClassId && IoU(existing, candidate) > 0.45))
                continue;
            selected.Add(candidate);
            if (selected.Count >= 100) break;
        }

        return selected.Select(candidate => new ObjectDetection
        {
            TimeSeconds = timeSeconds,
            ClassId = candidate.ClassId,
            Label = CocoLabels[candidate.ClassId],
            Confidence = candidate.Score,
            X = candidate.X,
            Y = candidate.Y,
            Width = candidate.Width,
            Height = candidate.Height
        }).ToList();
    }

    private static double IoU(Candidate left, Candidate right)
    {
        var x1 = Math.Max(left.X, right.X);
        var y1 = Math.Max(left.Y, right.Y);
        var x2 = Math.Min(left.X + left.Width, right.X + right.Width);
        var y2 = Math.Min(left.Y + left.Height, right.Y + right.Height);
        var intersection = Math.Max(0, x2 - x1) * Math.Max(0, y2 - y1);
        var union = left.Width * left.Height + right.Width * right.Height - intersection;
        return union <= 0 ? 0 : intersection / union;
    }

    private static void TryDeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, true); }
        catch { }
    }

    private sealed record Candidate(int ClassId, float Score, double X, double Y, double Width, double Height);
}
