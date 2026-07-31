using System.Text.Json.Nodes;
using AutoCutStudio.Infrastructure;

namespace AutoCutStudio.Tests;

public sealed class LocalMediaGenerationProviderTests
{
    [Fact]
    public void Comfy_workflow_replaces_tokens_and_injects_known_inputs()
    {
        const string workflow = """
        {
          "1": { "class_type": "CLIPTextEncode", "inputs": { "text": "{{PROMPT}}" } },
          "2": { "class_type": "EmptyLatentImage", "inputs": { "width": {{WIDTH}}, "height": {{HEIGHT}}, "batch_size": 1 } },
          "3": { "class_type": "KSampler", "inputs": { "seed": {{SEED}}, "steps": 20 } },
          "4": { "class_type": "SaveImage", "inputs": { "filename_prefix": "{{OUTPUT_PREFIX}}" } }
        }
        """;

        var result = ComfyUiWorkflowTemplate.Prepare(
            workflow,
            "mangrove forest at sunrise",
            1080,
            1920,
            durationSeconds: 4,
            inputImageName: null,
            seed: 12345,
            outputPrefix: "AutoCut-test");

        Assert.Equal("mangrove forest at sunrise", result["1"]!["inputs"]!["text"]!.GetValue<string>());
        Assert.Equal(1080L, result["2"]!["inputs"]!["width"]!.GetValue<long>());
        Assert.Equal(1920L, result["2"]!["inputs"]!["height"]!.GetValue<long>());
        Assert.Equal(12345L, result["3"]!["inputs"]!["seed"]!.GetValue<long>());
        Assert.Equal("AutoCut-test", result["4"]!["inputs"]!["filename_prefix"]!.GetValue<string>());
    }

    [Fact]
    public void Comfy_history_extracts_supported_image_and_video_outputs()
    {
        var history = JsonNode.Parse("""
        {
          "outputs": {
            "10": { "images": [{ "filename": "scene.png", "subfolder": "", "type": "output" }] },
            "20": { "videos": [{ "filename": "scene.mp4", "subfolder": "video", "type": "output" }] },
            "30": { "files": [{ "filename": "notes.txt", "subfolder": "", "type": "output" }] }
          }
        }
        """)!;

        var images = ComfyUiWorkflowTemplate.ExtractOutputReferences(history, "image");
        var videos = ComfyUiWorkflowTemplate.ExtractOutputReferences(history, "video");

        Assert.Single(images);
        Assert.Equal("scene.png", images[0].FileName);
        Assert.Single(videos);
        Assert.Equal("scene.mp4", videos[0].FileName);
        Assert.Equal("video", videos[0].Subfolder);
    }

    [Fact]
    public void Piper_resolves_explicit_executable_and_model_files()
    {
        var root = Path.Combine(Path.GetTempPath(), "autocut-piper-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var executable = Path.Combine(root, "piper.exe");
            var model = Path.Combine(root, "en_US-test-medium.onnx");
            File.WriteAllBytes(executable, [1]);
            File.WriteAllBytes(model, [2]);

            Assert.Equal(Path.GetFullPath(executable), PiperSpeechSynthesisProvider.ResolveExecutablePath(executable));
            Assert.Equal(Path.GetFullPath(model), PiperSpeechSynthesisProvider.ResolveModelPath(model, "auto"));
            Assert.Equal(Path.GetFullPath(model), PiperSpeechSynthesisProvider.ResolveModelPath(null, model));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
