using AutoCutStudio.Core.Services;

namespace AutoCutStudio.Tests;

public sealed class MediaGenerationProviderTests
{
    [Theory]
    [InlineData("ป่าชายเลนช่วยกักเก็บคาร์บอน", "th")]
    [InlineData("これは日本語のナレーションです", "ja")]
    [InlineData("这是中文旁白", "zh")]
    [InlineData("이것은 한국어 내레이션입니다", "ko")]
    [InlineData("هذا تعليق صوتي باللغة العربية", "ar")]
    [InlineData("Это русский текст", "ru")]
    [InlineData("This is an English narration", "en")]
    public void Language_detector_recognizes_common_scripts(string text, string expected)
    {
        var service = new LanguageDetectionService();

        var actual = service.Detect(text);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Manual_language_selection_is_preserved_for_latin_languages()
    {
        var service = new LanguageDetectionService();

        Assert.Equal("es", service.Normalize("es-ES", "Hola a todos"));
        Assert.Equal("fr", service.Normalize("fr-FR", "Bonjour"));
        Assert.Equal("de", service.Normalize("de-DE", "Guten Tag"));
    }

    [Theory]
    [InlineData(1080, 1920, "vertical 9:16")]
    [InlineData(1920, 1080, "landscape 16:9")]
    [InlineData(1080, 1080, "square composition")]
    public void Scene_prompt_builder_uses_platform_orientation(int width, int height, string expected)
    {
        var prompt = new ScenePromptBuilder().Build(
            "Mangrove restoration",
            "Communities restore mangrove forests to protect the coast.",
            width,
            height);

        Assert.Contains(expected, prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("no text", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Mangrove restoration", prompt, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(1080, 1920, "1024x1536")]
    [InlineData(1920, 1080, "1536x1024")]
    [InlineData(1080, 1080, "1024x1024")]
    public void Image_size_mapping_uses_supported_generation_sizes(int width, int height, string expected)
    {
        Assert.Equal(expected, ScenePromptBuilder.OpenAiImageSize(width, height));
    }

    [Theory]
    [InlineData(1080, 1920, "720x1280")]
    [InlineData(1920, 1080, "1280x720")]
    public void Video_size_mapping_uses_supported_generation_sizes(int width, int height, string expected)
    {
        Assert.Equal(expected, ScenePromptBuilder.OpenAiVideoSize(width, height));
    }
}
