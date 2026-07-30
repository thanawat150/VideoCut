using System.Diagnostics;
using System.Security.Cryptography;
using AutoCutStudio.Core.Models;
using AutoCutStudio.Core.Services;
using AutoCutStudio.Infrastructure;
using OpenCvSharp;
using OpenCvSharp.Dnn;

namespace AutoCutStudio.Tests;

public sealed class AdvancedAiTests
{
    [Fact]
    public void FaceTrackerBuildsStableReviewableTracks()
    {
        var faces = new List<DetectedFace>();
        for (var index = 0; index < 5; index++)
        {
            faces.Add(new DetectedFace
            {
                TimeSeconds = index * 0.5,
                Confidence = 0.9,
                X = 0.20 + index * 0.005,
                Y = 0.22,
                Width = 0.18,
                Height = 0.28
            });
        }
        faces.AddRange([
            new DetectedFace { TimeSeconds = 0, Confidence = 0.86, X = 0.68, Y = 0.18, Width = 0.16, Height = 0.25 },
            new DetectedFace { TimeSeconds = 0.5, Confidence = 0.84, X = 0.67, Y = 0.18, Width = 0.16, Height = 0.25 }
        ]);

        var tracks = OpenCvFaceTracker.BuildTracks(faces, 0.5);

        Assert.Equal(2, tracks.Count);
        Assert.Equal(5, tracks[0].Keyframes.Count);
        Assert.Equal(1, tracks[0].DisplayIndex);
        Assert.True(tracks[0].IsSelected);
        Assert.All(tracks.SelectMany(item => item.Keyframes), item =>
        {
            Assert.InRange(item.X, 0, 1);
            Assert.InRange(item.Y, 0, 1);
            Assert.InRange(item.Width, 0, 1);
            Assert.InRange(item.Height, 0, 1);
        });
    }

    [Fact]
    public void BrollSuggestionsUseTranscriptObjectsAndLocalAssets()
    {
        var root = Path.Combine(Path.GetTempPath(), "AutoCut Broll ภาษาไทย " + Guid.NewGuid().ToString("N"));
        var assets = Path.Combine(root, "assets");
        Directory.CreateDirectory(assets);
        var localAsset = Path.Combine(assets, "ป่าชายเลน_drone.mp4");
        File.WriteAllBytes(localAsset, [1, 2, 3]);
        try
        {
            var transcript = new TranscriptDocument
            {
                Segments =
                [
                    new TranscriptSegment
                    {
                        Sequence = 1,
                        StartSeconds = 2,
                        EndSeconds = 6,
                        Text = "สำรวจป่าชายเลนด้วยโดรนและแผนที่ GIS",
                        OriginalText = "สำรวจป่าชายเลนด้วยโดรนและแผนที่ GIS"
                    }
                ]
            };
            var objects = new ObjectAnalysisResult
            {
                Detections =
                [
                    new ObjectDetection { TimeSeconds = 4, Label = "person", Confidence = 0.91 }
                ]
            };

            var suggestions = new BrollSuggestionService().Suggest(transcript, objects, assets, 20);

            Assert.Contains(suggestions, item => item.Keyword == "ป่าชายเลน");
            Assert.Contains(suggestions, item => item.Keyword == "person" && item.ReasonSummary.Contains("YOLO"));
            Assert.Contains(suggestions, item => item.MatchedLocalAsset == localAsset);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task DocumentReaderCreatesEditableSectionsFromThaiMarkdown()
    {
        var root = Path.Combine(Path.GetTempPath(), "AutoCut Document ภาษาไทย " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "เอกสาร มีช่องว่าง.md");
        await File.WriteAllTextAsync(path,
            "# ป่าชายเลน\nป่าชายเลนช่วยกักเก็บคาร์บอนและป้องกันชายฝั่ง\n\n# การสำรวจ\nใช้โดรนและ GIS เพื่อติดตามพื้นที่");
        try
        {
            var sections = await new DocumentContentReader().ReadAsync(path);

            Assert.Equal(2, sections.Count);
            Assert.Equal("ป่าชายเลน", sections[0].Heading);
            Assert.Contains("คาร์บอน", sections[0].Body);
            Assert.All(sections, section => Assert.InRange(section.SuggestedDurationSeconds, 4, 12));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void TemplateAssBuilderCreatesTimedEditableSlides()
    {
        var recipe = new TemplateVideoRecipe
        {
            Title = "รายงานป่าชายเลน",
            Width = 1280,
            Height = 720,
            Sections =
            [
                new DocumentSection { Index = 1, Heading = "บทนำ", Body = "ข้อความภาษาไทยสำหรับสไลด์", SuggestedDurationSeconds = 3 },
                new DocumentSection { Index = 2, Heading = "ผลลัพธ์", Body = "พื้นที่ฟื้นตัวดีขึ้น", SuggestedDurationSeconds = 4 }
            ]
        };

        var builder = new TemplateVideoAssBuilder();
        var ass = builder.Build(recipe);

        Assert.Contains("PlayResX: 1280", ass);
        Assert.Contains("ข้อความภาษาไทยสำหรับสไลด์", ass);
        Assert.Contains("Dialogue:", ass);
        Assert.Equal(7, builder.TotalDuration(recipe));
    }

    [Fact]
    public void PrivacyFilterGraphUsesOnlyReviewedFaceTracks()
    {
        var job = new JobDocument
        {
            ExpectedDurationSeconds = 3,
            PrivacyBlurRecipe = new PrivacyBlurRecipe
            {
                BlurStrength = 18,
                PaddingRatio = 0.1,
                SampleIntervalSeconds = 0.5,
                Tracks =
                [
                    new FaceTrack
                    {
                        DisplayIndex = 1,
                        Keyframes =
                        [
                            new FaceTrackKeyframe { TimeSeconds = 0.5, X = 0.2, Y = 0.2, Width = 0.25, Height = 0.35, Confidence = 0.9 },
                            new FaceTrackKeyframe { TimeSeconds = 1, X = 0.22, Y = 0.2, Width = 0.25, Height = 0.35, Confidence = 0.9 }
                        ]
                    }
                ]
            }
        };

        var graph = FfmpegPrivacyBlurProcessor.BuildFilterGraph(job, out var outputLabel);

        Assert.Contains("crop=", graph);
        Assert.Contains("boxblur=", graph);
        Assert.Contains("overlay=", graph);
        Assert.Contains("between(t,", graph);
        Assert.StartsWith("[v", outputLabel);
    }

    [Fact]
    public void BundledVisionModelsLoadAndRunOnCpu()
    {
        var availability = new ComputerVisionToolLocator().Locate();
        Assert.NotNull(availability.FaceModelPath);
        Assert.NotNull(availability.ObjectModelPath);

        using var faceImage = new Mat(new Size(320, 240), MatType.CV_8UC3, Scalar.All(0));
        using var faceDetector = FaceDetectorYN.Create(
            availability.FaceModelPath!, string.Empty, faceImage.Size(), 0.6f, 0.3f, 5000);
        using var faces = new Mat();
        faceDetector.Detect(faceImage, faces);

        using var objectImage = new Mat(new Size(640, 640), MatType.CV_8UC3, Scalar.All(0));
        using var net = CvDnn.ReadNetFromOnnx(availability.ObjectModelPath!);
        net.SetPreferableBackend(Backend.OPENCV);
        net.SetPreferableTarget(Target.CPU);
        using var blob = CvDnn.BlobFromImage(objectImage, 1d / 255d, new Size(640, 640), new Scalar(), true, false);
        net.SetInput(blob);
        using var output = net.Forward();
        Assert.False(output.Empty());
        Assert.True(output.Total() > 0);
    }

    [Fact]
    public async Task RealTemplateVideoAndVoiceoverCreatePlayableOutputs()
    {
        var tools = new ToolLocator();
        var availability = tools.Locate();
        Assert.True(availability.IsReady, availability.Message);
        var root = Path.Combine(Path.GetTempPath(), "AutoCut Template ภาษาไทย " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var project = new ProjectDocument { DisplayName = "Template Test", RootPath = root };
            var recipe = new TemplateVideoRecipe
            {
                Title = "ทดสอบเอกสาร",
                Width = 320,
                Height = 180,
                FrameRate = 15,
                GenerateWindowsVoiceover = false,
                Sections =
                [
                    new DocumentSection { Index = 1, Heading = "หนึ่ง", Body = "เนื้อหาส่วนแรก", SuggestedDurationSeconds = 2 },
                    new DocumentSection { Index = 2, Heading = "สอง", Body = "เนื้อหาส่วนที่สอง", SuggestedDurationSeconds = 2 }
                ]
            };
            var repository = new JobRepository();
            var job = await new TemplateVideoJobFactory(repository).CreateAsync(project, recipe);
            JobProgress? progress = null;

            var report = await new FfmpegTemplateVideoProcessor(tools).ProcessAsync(
                job,
                _ => Task.CompletedTask,
                item => { progress = item; return Task.CompletedTask; });
            var metadata = await new FfprobeMediaProbe(tools).ProbeAsync(job.OutputPath);

            Assert.False(report.SourceWasModified);
            Assert.True(metadata.HasVideo);
            Assert.False(metadata.HasAudio);
            Assert.Equal(320, metadata.Width);
            Assert.Equal(180, metadata.Height);
            Assert.InRange(metadata.DurationSeconds, 3.7, 4.3);
            Assert.Equal(100, progress?.Progress);

            var voicePath = Path.Combine(root, "voice output.wav");
            await new WindowsVoiceoverService().GenerateAsync("Auto Cut Studio voiceover test.", voicePath);
            Assert.True(new FileInfo(voicePath).Length > 1000);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task RealPrivacyBlurChangesPixelsAndPreservesSource()
    {
        var tools = new ToolLocator();
        var availability = tools.Locate();
        Assert.True(availability.IsReady, availability.Message);
        var root = Path.Combine(Path.GetTempPath(), "AutoCut Privacy ภาษาไทย " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var source = Path.Combine(root, "source face region.mp4");
            await RunFfmpegAsync(availability.FfmpegPath!,
                "-hide_banner", "-y",
                "-f", "lavfi", "-i", "testsrc2=size=320x180:rate=25:duration=3",
                "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=48000:duration=3",
                "-c:v", "libx264", "-pix_fmt", "yuv420p", "-c:a", "aac", "-shortest", source);
            var beforeHash = await HashAsync(source);
            var metadata = await new FfprobeMediaProbe(tools).ProbeAsync(source);
            var media = new MediaAsset { SourcePath = source, Metadata = metadata };
            var project = new ProjectDocument { DisplayName = "Privacy", RootPath = root, SourceMedia = [media] };
            var track = new FaceTrack
            {
                DisplayIndex = 1,
                Keyframes =
                [
                    new FaceTrackKeyframe { TimeSeconds = 0, X = 0.2, Y = 0.15, Width = 0.42, Height = 0.55, Confidence = 1 },
                    new FaceTrackKeyframe { TimeSeconds = 1, X = 0.2, Y = 0.15, Width = 0.42, Height = 0.55, Confidence = 1 },
                    new FaceTrackKeyframe { TimeSeconds = 2, X = 0.2, Y = 0.15, Width = 0.42, Height = 0.55, Confidence = 1 }
                ]
            };
            var job = await new PrivacyBlurJobFactory(new JobRepository()).CreateAsync(
                project, media,
                new FaceTrackingResult
                {
                    SourcePath = source,
                    SourceWidth = 320,
                    SourceHeight = 180,
                    SampleIntervalSeconds = 1,
                    Tracks = [track]
                },
                new PrivacyBlurPlan { SelectedTracks = [track], BlurStrength = 24, BoxPaddingRatio = 0.1 });

            var report = await new FfmpegPrivacyBlurProcessor(tools).ProcessAsync(
                job, _ => Task.CompletedTask, _ => Task.CompletedTask);
            var afterHash = await HashAsync(source);
            Assert.False(report.SourceWasModified);
            Assert.Equal(beforeHash, afterHash);

            var sourceFrame = Path.Combine(root, "source.png");
            var outputFrame = Path.Combine(root, "output.png");
            await RunFfmpegAsync(availability.FfmpegPath!, "-hide_banner", "-y", "-ss", "1", "-i", source, "-frames:v", "1", sourceFrame);
            await RunFfmpegAsync(availability.FfmpegPath!, "-hide_banner", "-y", "-ss", "1", "-i", job.OutputPath, "-frames:v", "1", outputFrame);
            using var left = ReadImageUnicode(sourceFrame);
            using var right = ReadImageUnicode(outputFrame);
            using var difference = new Mat();
            Cv2.Absdiff(left, right, difference);
            var changed = Cv2.Sum(difference).Val0 + Cv2.Sum(difference).Val1 + Cv2.Sum(difference).Val2;
            Assert.True(changed > 10000, $"Expected privacy blur pixel changes, observed {changed}.");
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static Mat ReadImageUnicode(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var image = Cv2.ImDecode(bytes, ImreadModes.Color);
        if (image.Empty())
        {
            image.Dispose();
            throw new InvalidDataException($"OpenCV could not decode image bytes: {path}");
        }
        return image;
    }

    private static async Task RunFfmpegAsync(string ffmpeg, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = ffmpeg,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Unable to start FFmpeg.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        _ = await stdout;
        Assert.True(process.ExitCode == 0, await stderr);
    }

    private static async Task<string> HashAsync(string path)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream));
    }
}
