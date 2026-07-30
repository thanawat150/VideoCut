using AutoCutStudio.Core.Models;
using OpenCvSharp;

namespace AutoCutStudio.Infrastructure;

public sealed class OpenCvFaceTracker
{
    private readonly ToolLocator _mediaTools;
    private readonly ComputerVisionToolLocator _visionTools;

    public OpenCvFaceTracker(ToolLocator mediaTools, ComputerVisionToolLocator visionTools)
    {
        _mediaTools = mediaTools;
        _visionTools = visionTools;
    }

    public async Task<FaceTrackingResult> AnalyzeAsync(
        string inputPath,
        int sourceWidth,
        int sourceHeight,
        double durationSeconds,
        double sampleIntervalSeconds = 0.5,
        float confidenceThreshold = 0.72f,
        CancellationToken cancellationToken = default)
    {
        var availability = _visionTools.Locate();
        if (string.IsNullOrWhiteSpace(availability.FaceModelPath))
            throw new InvalidOperationException(availability.Message);

        var workingDirectory = Path.Combine(Path.GetTempPath(), "AutoCut-FaceTracking", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workingDirectory);
        try
        {
            var maximumFrames = Math.Min(300, Math.Max(1, (int)Math.Ceiling(durationSeconds / sampleIntervalSeconds)));
            var frames = await new FfmpegFrameSampler(_mediaTools).ExtractAsync(
                inputPath, workingDirectory, sampleIntervalSeconds, maximumFrames, cancellationToken);
            var allFaces = new List<DetectedFace>();

            foreach (var frame in frames)
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var image = ReadImageUnicode(frame.Path);
                if (image.Empty()) continue;
                using var detector = FaceDetectorYN.Create(
                    availability.FaceModelPath, string.Empty, image.Size(), confidenceThreshold, 0.3f, 5000);
                using var faces = new Mat();
                detector.Detect(image, faces);
                for (var row = 0; row < faces.Rows; row++)
                {
                    var score = faces.At<float>(row, 14);
                    if (score < confidenceThreshold) continue;
                    var x = faces.At<float>(row, 0);
                    var y = faces.At<float>(row, 1);
                    var width = faces.At<float>(row, 2);
                    var height = faces.At<float>(row, 3);
                    if (width <= 2 || height <= 2) continue;
                    allFaces.Add(new DetectedFace
                    {
                        TimeSeconds = frame.TimeSeconds,
                        Confidence = score,
                        X = Math.Clamp(x / image.Width, 0, 1),
                        Y = Math.Clamp(y / image.Height, 0, 1),
                        Width = Math.Clamp(width / image.Width, 0, 1),
                        Height = Math.Clamp(height / image.Height, 0, 1)
                    });
                }
            }

            return new FaceTrackingResult
            {
                SourcePath = inputPath,
                SourceWidth = sourceWidth,
                SourceHeight = sourceHeight,
                SampleIntervalSeconds = sampleIntervalSeconds,
                Tracks = BuildTracks(allFaces, sampleIntervalSeconds)
            };
        }
        finally { TryDeleteDirectory(workingDirectory); }
    }

    public static List<FaceTrack> BuildTracks(IReadOnlyList<DetectedFace> faces, double sampleIntervalSeconds)
    {
        var trackBuilders = new List<TrackBuilder>();
        foreach (var timeGroup in faces.GroupBy(face => face.TimeSeconds).OrderBy(group => group.Key))
        {
            var assignedTracks = new HashSet<Guid>();
            foreach (var face in timeGroup.OrderByDescending(item => item.Confidence))
            {
                TrackBuilder? best = null;
                var bestIou = 0d;
                foreach (var candidate in trackBuilders)
                {
                    if (assignedTracks.Contains(candidate.Id) || candidate.Keyframes.Count == 0) continue;
                    var last = candidate.Keyframes[^1];
                    if (face.TimeSeconds - last.TimeSeconds > sampleIntervalSeconds * 2.2) continue;
                    var iou = IoU(last.X, last.Y, last.Width, last.Height, face.X, face.Y, face.Width, face.Height);
                    if (iou > bestIou) { bestIou = iou; best = candidate; }
                }
                if (best is null || bestIou < 0.22)
                {
                    best = new TrackBuilder();
                    trackBuilders.Add(best);
                }
                best.Keyframes.Add(new FaceTrackKeyframe
                {
                    TimeSeconds = face.TimeSeconds,
                    X = face.X,
                    Y = face.Y,
                    Width = face.Width,
                    Height = face.Height,
                    Confidence = face.Confidence
                });
                assignedTracks.Add(best.Id);
            }
        }
        return trackBuilders
            .Where(builder => builder.Keyframes.Count >= 2)
            .OrderByDescending(builder => builder.Keyframes.Count)
            .ThenByDescending(builder => builder.Keyframes.Average(item => item.Confidence))
            .Select((builder, index) => new FaceTrack
            {
                TrackId = builder.Id,
                DisplayIndex = index + 1,
                Keyframes = builder.Keyframes,
                IsSelected = index < 5
            }).ToList();
    }

    private static Mat ReadImageUnicode(string path)
    {
        var image = Cv2.ImDecode(File.ReadAllBytes(path), ImreadModes.Color);
        if (image.Empty())
        {
            image.Dispose();
            throw new InvalidDataException($"OpenCV อ่าน sampled frame ไม่สำเร็จ: {path}");
        }
        return image;
    }

    private static double IoU(double x1, double y1, double w1, double h1, double x2, double y2, double w2, double h2)
    {
        var left = Math.Max(x1, x2);
        var top = Math.Max(y1, y2);
        var right = Math.Min(x1 + w1, x2 + w2);
        var bottom = Math.Min(y1 + h1, y2 + h2);
        var intersection = Math.Max(0, right - left) * Math.Max(0, bottom - top);
        var union = w1 * h1 + w2 * h2 - intersection;
        return union <= 0 ? 0 : intersection / union;
    }

    private static void TryDeleteDirectory(string path) { try { if (Directory.Exists(path)) Directory.Delete(path, true); } catch { } }
    private sealed class TrackBuilder
    {
        public Guid Id { get; } = Guid.NewGuid();
        public List<FaceTrackKeyframe> Keyframes { get; } = [];
    }
}
