using AutoCutStudio.Core.Models;
using AutoCutStudio.Infrastructure;

namespace AutoCutStudio.Tests;

public sealed class ProviderFrameworkTests
{
    [Fact]
    public async Task RegistryProvidesRealFolderDeliveryAndReviewGatedSocialProviders()
    {
        var root = Path.Combine(Path.GetTempPath(), "AutoCut Providers ภาษาไทย " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var source = Path.Combine(root, "output มีช่องว่าง.mp4");
            await File.WriteAllBytesAsync(source, Enumerable.Range(0, 8192).Select(index => (byte)(index % 251)).ToArray());
            var delivery = new DeliveryPackageService();
            var registry = new ProviderRegistry(delivery);

            var capabilities = registry.GetCapabilities();
            Assert.Contains(capabilities, item =>
                item.ProviderId == "local-folder" && item.IsConfigured && item.CanPublishDirectly);
            Assert.Contains(capabilities, item =>
                item.ProviderId == "youtube" && !item.CanPublishDirectly);
            Assert.Contains(capabilities, item => item.ProviderId == "tiktok");
            Assert.Contains(capabilities, item => item.ProviderId == "instagram");
            Assert.Contains(capabilities, item => item.ProviderId == "facebook");

            var delivered = await registry.GetCloud("local-folder").DeliverAsync(
                source,
                Path.Combine(root, "Cloud Sync Folder"));
            Assert.True(File.Exists(delivered.DeliveredPath));
            Assert.True(File.Exists(delivered.DeliveredPath + ".delivery.json"));

            var youtube = registry.GetSocial("youtube");
            await Assert.ThrowsAsync<InvalidDataException>(() => youtube.PrepareAsync(
                source,
                Path.Combine(root, "outbox"),
                new SocialPublishMetadata { Platform = "tiktok", Title = "Wrong provider" }));
            var package = await youtube.PrepareAsync(
                source,
                Path.Combine(root, "outbox"),
                new SocialPublishMetadata
                {
                    Platform = "youtube",
                    Title = "ทดสอบ Provider",
                    Privacy = "private"
                });
            Assert.True(File.Exists(Path.Combine(package, "publish.json")));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
