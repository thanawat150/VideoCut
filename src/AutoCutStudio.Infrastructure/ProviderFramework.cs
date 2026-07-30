using AutoCutStudio.Core.Models;

namespace AutoCutStudio.Infrastructure;

public interface ICloudDeliveryProvider
{
    ProviderCapability Capability { get; }

    Task<DeliveryPackageManifest> DeliverAsync(
        string sourceVideo,
        string destination,
        CancellationToken cancellationToken = default);
}

public interface ISocialPublishingProvider
{
    ProviderCapability Capability { get; }

    Task<string> PrepareAsync(
        string sourceVideo,
        string outboxRoot,
        SocialPublishMetadata metadata,
        CancellationToken cancellationToken = default);
}

public sealed class LocalFolderCloudProvider : ICloudDeliveryProvider
{
    private readonly DeliveryPackageService _delivery;

    public LocalFolderCloudProvider(DeliveryPackageService delivery) => _delivery = delivery;

    public ProviderCapability Capability => new()
    {
        ProviderId = "local-folder",
        DisplayName = "Local / Synced Cloud Folder",
        Category = "cloud",
        IsConfigured = true,
        CanPublishDirectly = true,
        Message = "ส่งไฟล์จริงไปยัง Local, Network, OneDrive, Google Drive Desktop หรือโฟลเดอร์ Sync อื่น"
    };

    public Task<DeliveryPackageManifest> DeliverAsync(
        string sourceVideo,
        string destination,
        CancellationToken cancellationToken = default) =>
        _delivery.DeliverToFolderAsync(sourceVideo, destination, cancellationToken);
}

public sealed class SocialOutboxProvider : ISocialPublishingProvider
{
    private readonly DeliveryPackageService _delivery;
    private readonly string _providerId;
    private readonly string _displayName;
    private readonly string _credentialEnvironmentVariable;

    public SocialOutboxProvider(
        DeliveryPackageService delivery,
        string providerId,
        string displayName,
        string credentialEnvironmentVariable)
    {
        _delivery = delivery;
        _providerId = providerId;
        _displayName = displayName;
        _credentialEnvironmentVariable = credentialEnvironmentVariable;
    }

    public ProviderCapability Capability => new()
    {
        ProviderId = _providerId,
        DisplayName = _displayName,
        Category = "social",
        IsConfigured = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(_credentialEnvironmentVariable)),
        CanPublishDirectly = false,
        Message = "สร้าง Publishing Outbox จริงได้ แต่ Direct API Publish ถูกปิดจนกว่าจะมี authenticated provider implementation และสิทธิ์จากแพลตฟอร์ม"
    };

    public Task<string> PrepareAsync(
        string sourceVideo,
        string outboxRoot,
        SocialPublishMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        if (!string.Equals(metadata.Platform, _providerId, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Metadata platform '{metadata.Platform}' ไม่ตรงกับ Provider '{_providerId}'");
        return _delivery.CreateSocialOutboxAsync(sourceVideo, outboxRoot, metadata, cancellationToken);
    }
}

public sealed class ProviderRegistry
{
    public ProviderRegistry(DeliveryPackageService delivery)
    {
        CloudProviders = [new LocalFolderCloudProvider(delivery)];
        SocialProviders =
        [
            new SocialOutboxProvider(delivery, "youtube", "YouTube Data API", "AUTOCUT_YOUTUBE_CLIENT_ID"),
            new SocialOutboxProvider(delivery, "tiktok", "TikTok Content Posting API", "AUTOCUT_TIKTOK_CLIENT_KEY"),
            new SocialOutboxProvider(delivery, "instagram", "Instagram Graph API", "AUTOCUT_INSTAGRAM_APP_ID"),
            new SocialOutboxProvider(delivery, "facebook", "Facebook Graph API", "AUTOCUT_FACEBOOK_APP_ID")
        ];
    }

    public IReadOnlyList<ICloudDeliveryProvider> CloudProviders { get; }
    public IReadOnlyList<ISocialPublishingProvider> SocialProviders { get; }

    public IReadOnlyList<ProviderCapability> GetCapabilities() =>
        CloudProviders.Select(item => item.Capability)
            .Concat(SocialProviders.Select(item => item.Capability))
            .OrderBy(item => item.Category)
            .ThenBy(item => item.DisplayName)
            .ToList();

    public ICloudDeliveryProvider GetCloud(string providerId) =>
        CloudProviders.FirstOrDefault(item => item.Capability.ProviderId.Equals(providerId, StringComparison.OrdinalIgnoreCase))
        ?? throw new KeyNotFoundException($"ไม่พบ Cloud Provider: {providerId}");

    public ISocialPublishingProvider GetSocial(string providerId) =>
        SocialProviders.FirstOrDefault(item => item.Capability.ProviderId.Equals(providerId, StringComparison.OrdinalIgnoreCase))
        ?? throw new KeyNotFoundException($"ไม่พบ Social Provider: {providerId}");
}
