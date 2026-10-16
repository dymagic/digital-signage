namespace Signage.Web.Domain;

/// <summary>A device asking to be paired with a screen (R-40). Flow details land in WP-06.</summary>
public class PairingRequest : IHasCreatedAt
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public required string Code { get; set; }
    public required string DeviceSecretHash { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public Guid? ClaimedScreenId { get; set; }
    public DateTimeOffset? ClaimedAt { get; set; }
    public DateTimeOffset? TokenDeliveredAt { get; set; }

    public Screen? ClaimedScreen { get; set; }
}
